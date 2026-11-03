using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenWave.Services;

namespace ScreenWave.Views;

public partial class ConnectView : UserControl
{
    private bool _busy;

    public ConnectView()
    {
        InitializeComponent();
        Tv.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateButtons);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAddresses();
        var adapters = Tv.EthernetAdapters();
        AdapterBox.ItemsSource = adapters.Select(a => a.Name).ToList();
        AdapterBox.SelectedIndex = adapters.Count > 0 ? 0 : -1;
        CableButton.IsEnabled = CableUndoButton.IsEnabled = adapters.Count > 0;
        UpdateButtons();
    }

    private void RefreshAddresses()
    {
        var addrs = Tv.LocalAddresses().Where(a => Tv.IsLocal(a.Address)).ToList();
        AddressList.ItemsSource = addrs;
        NoAddressText.Visibility = addrs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtons()
    {
        ConnectButton.Visibility = Tv.Connected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectButton.Visibility = Tv.Connected ? Visibility.Visible : Visibility.Collapsed;
        FindButton.IsEnabled = ConnectButton.IsEnabled = !_busy && !Tv.Connected;
        IpBox.IsEnabled = !Tv.Connected;
        if (Tv.Connected) StatusText.Text = $"Connected to {Tv.Serial}. Go to 2. Remove apps.";
    }

    private void SetBusy(bool busy, string? status = null)
    {
        if (busy && !_busy) Busy.Enter();
        if (!busy && _busy) Busy.Exit();
        _busy = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        if (status != null) StatusText.Text = status;
        UpdateButtons();
    }

    // ───────────── Finding and connecting ─────────────

    private async void OnFind(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, "Looking for TVs in Developer Mode on this PC's network…");
        BusyBar.IsIndeterminate = false;
        BusyBar.Maximum = 100;
        try
        {
            var found = await Tv.ScanAsync(new Progress<int>(p => BusyBar.Value = p));
            FoundList.ItemsSource = found.Select(ip => ip.ToString()).ToList();
            if (found.Count == 1) IpBox.Text = found[0].ToString();
            StatusText.Text = found.Count switch
            {
                0 => "No TV found. Check Developer Mode is on with this PC's address, and that the TV was restarted. Or type the TV's address.",
                1 => $"Found a TV at {found[0]}. Click Connect.",
                _ => "Found more than one – pick yours, then click Connect.",
            };
        }
        catch (Exception ex)
        {
            Log.Write($"Scan failed: {ex.Message}");
            StatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnPickFound(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string ip }) IpBox.Text = ip;
    }

    private void OnIpKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnConnect(sender, e);
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (_busy || Tv.Connected) return;
        SetBusy(true, $"Connecting to {IpBox.Text.Trim()}…");
        BusyBar.IsIndeterminate = true;
        try
        {
            var (ok, message) = await Tv.ConnectAsync(IpBox.Text);
            if (!ok)
            {
                StatusText.Text = message;
                return;
            }
            StatusText.Text = "Connected.";
            if (Window.GetWindow(this) is MainWindow w) w.GoTo("apps");
        }
        catch (Exception ex)
        {
            Log.Write($"Connect failed: {ex}");
            StatusText.Text = $"Connect failed: {ex.Message}";
        }
        finally
        {
            BusyBar.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        await Tv.DisconnectAsync();
        StatusText.Text = "Disconnected.";
    }

    // ───────────── Direct cable ─────────────

    private async void OnCableSetup(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter) return;
        StatusText.Text = $"Setting {adapter} to {DirectCable.PcAddress}… Windows will ask for permission.";
        var ok = await DirectCable.SetUpAsync(adapter);
        StatusText.Text = ok
            ? $"{adapter} is now {DirectCable.PcAddress}. Set the TV to {DirectCable.TvAddress} as shown, then use {DirectCable.PcAddress} as the Host PC IP."
            : "This PC's address wasn't changed (the permission prompt was cancelled, or Windows refused).";
        if (ok)
        {
            IpBox.Text = DirectCable.TvAddress;
            await Task.Delay(1500);
            RefreshAddresses();
        }
    }

    private async void OnCableUndo(object sender, RoutedEventArgs e)
    {
        if (AdapterBox.SelectedItem is not string adapter) return;
        var ok = await DirectCable.UndoAsync(adapter);
        StatusText.Text = ok ? $"{adapter} is back to automatic." : "Nothing was changed.";
        await Task.Delay(1500);
        RefreshAddresses();
    }
}
