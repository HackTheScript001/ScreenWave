using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ScreenWave.Models;
using ScreenWave.Services;

namespace ScreenWave.Views;

public sealed class AppRow : ObservableItem
{
    public required TvApp App { get; init; }
    public string Title => App.Title;
    public string Id => App.Id;
    public bool CanRemove => App.Kind == AppKind.Removable && Status != "Removed";
    public string KindText => App.Kind == AppKind.Removable ? "Can be removed" : "Samsung system – left alone";
    public string SuggestedText => App.Suggested ? "Suggested" : "";
    public int UseSort => App.UseCount ?? -1;
    public string UsedText => App.UseCount switch
    {
        null => "",
        0 => "Never",
        1 => "Once",
        var n => $"{n} times",
    };

    public void MarkRemoved()
    {
        Status = "Removed";
        Selected = false;
        Raise(nameof(CanRemove));
    }
}

public partial class AppsView : UserControl
{
    private readonly ObservableCollection<AppRow> _rows = [];
    private readonly ICollectionView _view;
    private bool _busy;
    private string? _listedFor;

    public AppsView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is AppRow r && (r.App.Kind == AppKind.Removable || ShowCore.IsChecked == true);
        AppGrid.ItemsSource = _view;
        Tv.ConnectionChanged += () => Dispatcher.InvokeAsync(() =>
        {
            if (!Tv.Connected) { _rows.Clear(); _listedFor = null; ShowEmpty("Connect to the TV first (1. Connect to the TV)."); }
            UpdateButtons();
            UpdateRemotePanel();
        });
        TvRemote.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateRemotePanel);
        Routines.TeachModeChanged += () => Dispatcher.InvokeAsync(UpdateRemotePanel);
        Pad.Pressed += OnPadPressed;
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateButtons();
        UpdateRemotePanel();
        if (Tv.Connected && _listedFor != Tv.Serial) await LoadAsync();
        else if (!Tv.Connected) ShowEmpty("Connect to the TV first (1. Connect to the TV).");
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_busy || Tv.Serial is not { } serial) return;
        SetBusy(true, "Reading the TV's apps…");
        try
        {
            var (apps, raw) = await TvApps.ListAsync(serial);
            _rows.Clear();
            foreach (var a in apps)
            {
                var row = new AppRow { App = a };
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppRow.Selected)) UpdateButtons(); };
                _rows.Add(row);
            }
            _listedFor = serial;

            if (apps.Count == 0)
            {
                ShowEmpty("The TV sent back a list ScreenWave couldn't read. Open Details below to see exactly what it sent.");
                Log.Write("Unrecognised app list:\n" + raw);
            }
            else
            {
                EmptyText.Visibility = Visibility.Collapsed;
            }
            UpdateCount();
            StatusText.Text = $"{apps.Count(a => a.Kind == AppKind.Removable)} app(s) can be removed. Tick the ones you don't use, or click Tick suggested.";
        }
        catch (Exception ex)
        {
            Log.Write($"App list failed: {ex.Message}");
            ShowEmpty(ex.Message);
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    private void OnSuggest(object sender, RoutedEventArgs e)
    {
        var n = 0;
        foreach (var r in _rows.Where(r => r.App.Suggested && r.CanRemove)) { r.Selected = true; n++; }
        StatusText.Text = n > 0 ? $"Ticked {n} suggested app(s). Untick any you use." : "None of the usual unused apps are on this TV.";
    }

    private static int PlatformMajor =>
        int.TryParse(Tv.PlatformVersion.Split('.')[0], out var v) ? v : 0;

    /// <summary>Tizen 6 and newer only allow removal through the TV's own Apps settings, so the remote does it.</summary>
    private static bool NeedsRemote => PlatformMajor >= 6;

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_busy || Tv.Serial is not { } serial) return;
        var chosen = _rows.Where(r => r.Selected && r.CanRemove).ToList();
        if (chosen.Count == 0) return;

        if (NeedsRemote && !RemoteRemoval.IsTaught)
        {
            MessageBox.Show(OwnerWindow!, "This TV only lets apps be removed through its own Apps settings screen, and ScreenWave hasn't learnt that screen for this TV yet.",
                "Remove apps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (NeedsRemote && !TvRemote.Connected)
        {
            MessageBox.Show(OwnerWindow!, "Click Connect remote (above the list) first – ScreenWave removes apps on this TV by pressing its remote buttons.",
                "Remove apps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var names = string.Join("\n", chosen.Take(15).Select(r => "•  " + r.Title)) + (chosen.Count > 15 ? $"\n…and {chosen.Count - 15} more" : "");
        var how = NeedsRemote ? "\n\nScreenWave will press the TV's remote buttons to delete each one – watch the TV while it works." : "";
        if (MessageBox.Show(OwnerWindow!, $"Remove {chosen.Count} app(s) from the TV?\n\n{names}{how}\n\nYou can reinstall them later from the TV's Apps store.",
                "Remove apps", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SetBusy(true);
        _stop = new CancellationTokenSource();
        StopButton.Visibility = Visibility.Visible;
        var removed = new List<RemovedApp>();
        try
        {
            if (NeedsRemote) await RemoveWithRemoteAsync(serial, chosen, removed, _stop.Token);
            else await RemoveWithDeveloperModeAsync(serial, chosen, removed);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            Log.Write($"Remove failed: {ex}");
            StatusText.Text = $"Remove failed: {ex.Message}";
        }
        finally
        {
            TvApps.RecordRemoved(removed);
            StopButton.Visibility = Visibility.Collapsed;
            _stop?.Dispose();
            _stop = null;
            SetBusy(false);
        }
    }

    private async Task RemoveWithDeveloperModeAsync(string serial, List<AppRow> chosen, List<RemovedApp> removed)
    {
        var n = 0;
        foreach (var r in chosen)
        {
            n++;
            StatusText.Text = $"Removing {r.Title} ({n} of {chosen.Count})…";
            r.Status = "Removing…";
            var res = await TvApps.RemoveAsync(serial, r.App);
            r.Status = res.TimedOut ? "No answer" : "Checking…";
        }

        // Confirm by listing again: anything no longer listed was removed
        StatusText.Text = "Checking what was removed…";
        var (after, _) = await TvApps.ListAsync(serial);
        var stillThere = after.Select(a => a.Id).ToHashSet();
        foreach (var r in chosen)
        {
            if (stillThere.Contains(r.Id)) r.Status = "TV refused";
            else
            {
                r.MarkRemoved();
                removed.Add(new RemovedApp(r.Title, r.Id, DateTime.Now, Tv.Details));
            }
        }

        var refused = chosen.Count - removed.Count;
        StatusText.Text = $"Removed {removed.Count} app(s)" +
                          (refused > 0 ? $". The TV wouldn't remove {refused} – open Details to see what it said." : ".") +
                          " Next: 3. Speed-up settings.";
    }

    private async Task RemoveWithRemoteAsync(string serial, List<AppRow> chosen, List<RemovedApp> removed, CancellationToken ct)
    {
        var n = 0;
        foreach (var r in chosen)
        {
            n++;
            r.Status = "Removing…";
            var result = await RemoteRemoval.RemoveAsync(serial, r.App,
                s => Dispatcher.Invoke(() => StatusText.Text = $"({n} of {chosen.Count}) {s}  Watch the TV."), ct);

            if (result.Removed)
            {
                r.MarkRemoved();
                removed.Add(new RemovedApp(r.Title, r.Id, DateTime.Now, Tv.Details));
                continue;
            }

            r.Status = "Not removed";
            StatusText.Text = result.Message;
            if (result.WrongAppRemoved != null)
            {
                MessageBox.Show(OwnerWindow!, result.Message, "Stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await LoadAfterAsync(serial);
            return; // stop at the first problem rather than carry on blind
        }

        StatusText.Text = $"Removed {removed.Count} app(s). Next: 3. Speed-up settings.";
        try { await TvRemote.PressAsync("KEY_EXIT"); } catch { }
    }

    private async Task LoadAfterAsync(string serial)
    {
        try
        {
            var (after, _) = await TvApps.ListAsync(serial);
            var ids = after.Select(a => a.Id).ToHashSet();
            foreach (var r in _rows.Where(r => !ids.Contains(r.Id) && r.Status != "Removed")) r.MarkRemoved();
        }
        catch { }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _stop?.Cancel();

    // ───────────── Remote panel and teaching ─────────────

    private CancellationTokenSource? _stop;
    private int _teachStep;                         // 0 = not teaching, 1 open list, 2 move to app, 3 delete
    private readonly List<string> _openKeys = [], _moveKeys = [], _deleteKeys = [];
    private List<TvApp> _teachBefore = [];

    private void UpdateRemotePanel()
    {
        var show = Tv.Connected && (NeedsRemote || Routines.TeachMode);
        RemotePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var connected = TvRemote.Connected;
        var taught = RemoteRemoval.IsTaught;
        RemoteConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        TeachButton.Visibility = connected && Routines.TeachMode && _teachStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = CancelTeachButton.Visibility = _teachStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _teachStep == 3 ? "Finish" : "Next";
        Pad.Visibility = _teachStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        ColsRow.Visibility = _teachStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        RecordedText.Visibility = _teachStep > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_teachStep > 0) { ShowTeachStep(); return; }

        if (!connected)
        {
            RemoteTitle.Text = "This TV removes apps through its own settings screen";
            RemoteHelp.Text = "ScreenWave does that for you by pressing the TV's remote buttons over your network. Click Connect remote – the first time, the TV asks you to Allow ScreenWave.";
        }
        else if (taught)
        {
            RemoteTitle.Text = "✓ Remote connected – ready to remove apps";
            RemoteHelp.Text = "Tick the apps, click Remove selected and watch the TV. ScreenWave checks each one has gone, and you can stop at any time.";
        }
        else
        {
            RemoteTitle.Text = "Removing apps hasn't been taught for this TV yet";
            RemoteHelp.Text = Routines.TeachMode
                ? "Click Teach removal and remove one app you don't need, in three short steps."
                : "Support for this TV is coming in a ScreenWave update.";
        }
    }

    private async void OnRemoteConnect(object sender, RoutedEventArgs e)
    {
        RemoteConnectButton.IsEnabled = false;
        RemoteHelp.Text = "Connecting… If the TV asks whether to allow ScreenWave, choose Allow with your remote.";
        try
        {
            var (ok, message) = await TvRemote.ConnectAsync(Tv.Ip);
            if (!ok) RemoteHelp.Text = message;
        }
        finally
        {
            RemoteConnectButton.IsEnabled = true;
            if (TvRemote.Connected) UpdateRemotePanel();
        }
    }

    private async void OnTeach(object sender, RoutedEventArgs e)
    {
        if (Tv.Serial is not { } serial || !TvRemote.Connected) return;
        TeachButton.IsEnabled = false;
        try
        {
            (_teachBefore, _) = await TvApps.ListAsync(serial);
            await TvRemote.PressAsync("KEY_EXIT");
            await Task.Delay(Routines.Gap);
            await TvRemote.PressAsync("KEY_EXIT");
        }
        catch (Exception ex)
        {
            RemoteHelp.Text = $"Couldn't start: {ex.Message}";
            return;
        }
        finally { TeachButton.IsEnabled = true; }

        _openKeys.Clear(); _moveKeys.Clear(); _deleteKeys.Clear();
        _teachStep = 1;
        UpdateRemotePanel();
    }

    private void OnPadPressed(string key)
    {
        var list = _teachStep switch { 1 => _openKeys, 2 => _moveKeys, 3 => _deleteKeys, _ => null };
        list?.Add(key);
        ShowTeachStep();
    }

    private void ShowTeachStep()
    {
        (RemoteTitle.Text, RemoteHelp.Text) = _teachStep switch
        {
            1 => ("Step 1 of 3 – open the app list",
                  "Using the buttons on the right, go to Home › Apps › ⚙ Settings (top right), so the first app in the grid is highlighted. Then click Next."),
            2 => ("Step 2 of 3 – pick an app you don't need",
                  "Enter how many apps there are in each row on the TV, then use the arrows to move to an app you don't need (for example QVC+). Then click Next."),
            _ => ("Step 3 of 3 – delete it",
                  "Press OK, choose Delete and confirm, using the buttons on the right. When the TV shows it's gone, click Finish."),
        };
        var keys = _teachStep switch { 1 => _openKeys, 2 => _moveKeys, _ => _deleteKeys };
        RecordedText.Text = keys.Count == 0 ? "Nothing pressed yet." : "Pressed: " + string.Join(" › ", keys.Select(TvRemote.Label));
    }

    private async void OnTeachNext(object sender, RoutedEventArgs e)
    {
        if (_teachStep == 1 && _openKeys.Count == 0) { RecordedText.Text = "Use the buttons on the right first."; return; }
        if (_teachStep == 2 && !int.TryParse(ColsBox.Text.Trim(), out _)) { RecordedText.Text = "Enter how many apps there are in each row."; return; }
        if (_teachStep == 3 && _deleteKeys.Count == 0) { RecordedText.Text = "Use the buttons on the right to delete the app first."; return; }

        if (_teachStep < 3)
        {
            _teachStep++;
            UpdateRemotePanel();
            return;
        }

        // Finish: see which app actually went, and check it matches the grid
        if (Tv.Serial is not { } serial) return;
        NextButton.IsEnabled = false;
        RecordedText.Text = "Checking what was removed…";
        try
        {
            var cols = int.Parse(ColsBox.Text.Trim());
            await Task.Delay(TimeSpan.FromSeconds(3));
            var (after, _) = await TvApps.ListAsync(serial);
            var arrows = _moveKeys.Where(k => k is "KEY_UP" or "KEY_DOWN" or "KEY_LEFT" or "KEY_RIGHT").ToList();
            var (notes, message, confirmed) = RemoteRemoval.CheckTeaching(_teachBefore, after, arrows, cols);

            if (notes.Count > 0)
            {
                Routines.Set(Tv.Model, Tv.PlatformVersion, RemoteRemoval.OpenKey, [.. _openKeys]);
                Routines.Set(Tv.Model, Tv.PlatformVersion, RemoteRemoval.DeleteKey, [.. _deleteKeys]);
                Routines.Set(Tv.Model, Tv.PlatformVersion, RemoteRemoval.LayoutKey, notes);
            }
            Log.Write($"Teach removal: {message} (layout: {string.Join(", ", notes)})");
            MessageBox.Show(OwnerWindow!, message + (confirmed ? "\n\nUse Export recordings on 3. Speed-up settings to save it for the next ScreenWave release." : ""),
                "Teach removal", MessageBoxButton.OK, confirmed ? MessageBoxImage.Information : MessageBoxImage.Warning);
            _teachStep = 0;
            UpdateRemotePanel();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            RecordedText.Text = $"Couldn't check: {ex.Message}";
        }
        finally { NextButton.IsEnabled = true; }
    }

    private void OnTeachCancel(object sender, RoutedEventArgs e)
    {
        _teachStep = 0;
        UpdateRemotePanel();
    }

    // ───────────── Helpers ─────────────

    private void ShowEmpty(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void UpdateCount()
    {
        var removable = _rows.Count(r => r.App.Kind == AppKind.Removable);
        var core = _rows.Count - removable;
        CountText.Text = _rows.Count == 0 ? "" : $"{removable} removable  ·  {core} Samsung system apps{(ShowCore.IsChecked == true ? "" : " (hidden)")}";
    }

    private void UpdateButtons()
    {
        var n = _rows.Count(r => r.Selected && r.CanRemove);
        RemoveButton.IsEnabled = !_busy && Tv.Connected && n > 0;
        RemoveButton.Content = n > 0 ? $"Remove selected ({n})" : "Remove selected";
        RefreshButton.IsEnabled = SuggestButton.IsEnabled = !_busy && Tv.Connected;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        if (busy && !_busy) Busy.Enter();
        if (!busy && _busy) Busy.Exit();
        _busy = busy;
        BusyBar.IsIndeterminate = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        if (status != null) StatusText.Text = status;
        UpdateButtons();
    }
}
