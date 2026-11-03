using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenWave.Models;
using ScreenWave.Services;

namespace ScreenWave.Views;

public sealed class TipRow : ObservableItem
{
    private bool _hasRoutine, _teaching, _canUse, _remote, _teachMode;

    public required Tip Tip { get; init; }

    /// <summary>Settings that need a PIN or confirmation screens aren't automated.</summary>
    public bool Automatable => Tip.Key is not ("smarthub-reset" or "firmware" or "cache" or "restart");

    public bool HasRoutine { get => _hasRoutine; set { Set(ref _hasRoutine, value); Refresh(); } }
    public bool Teaching { get => _teaching; set { Set(ref _teaching, value); Refresh(); } }
    public bool CanUse { get => _canUse; set => Set(ref _canUse, value); }
    public bool RemoteOn { get => _remote; set { Set(ref _remote, value); Refresh(); } }
    public bool TeachMode { get => _teachMode; set { Set(ref _teachMode, value); Refresh(); } }

    public Visibility DoItVisibility => Automatable && RemoteOn && HasRoutine && !Teaching ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TeachVisibility => Automatable && RemoteOn && TeachMode && !Teaching ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SaveVisibility => Teaching ? Visibility.Visible : Visibility.Collapsed;
    public string TeachLabel => HasRoutine ? "Teach again" : "Teach";

    private void Refresh()
    {
        Raise(nameof(DoItVisibility));
        Raise(nameof(TeachVisibility));
        Raise(nameof(SaveVisibility));
        Raise(nameof(TeachLabel));
    }
}

public partial class TipsView : UserControl
{
    private readonly List<TipRow> _rows;
    private TipRow? _teaching;
    private readonly List<string> _recorded = [];
    private CancellationTokenSource? _playing;
    private bool _connecting;

    public TipsView()
    {
        InitializeComponent();
        var done = Checklist.Done();
        _rows = Checklist.Tips.Select(t => new TipRow { Tip = t, Selected = done.Contains(t.Key) }).ToList();
        foreach (var r in _rows)
            r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TipRow.Selected)) Changed(); };
        TipList.ItemsSource = _rows;
        TvRemote.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateRemote);
        Tv.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateRemote);
        UpdateText();
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateRemote();

    // ───────────── Checklist ─────────────

    private void Changed()
    {
        Checklist.Save(_rows.Where(r => r.Selected).Select(r => r.Tip.Key));
        UpdateText();
    }

    private void UpdateText()
    {
        var n = _rows.Count(r => r.Selected);
        ProgressText.Text = n == _rows.Count ? "All done. Next: 4. Stop apps coming back." : $"{n} of {_rows.Count} done.";
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Selected = false;
    }

    // ───────────── Remote ─────────────

    private void UpdateRemote()
    {
        var on = TvRemote.Connected;
        RemoteDot.Foreground = on ? (Brush)FindResource("Green") : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        RemoteTitle.Text = on ? $" Remote connected – {Routines.Range(Tv.Model)} (Tizen {Tv.PlatformVersion})" : " Remote not connected";
        RemoteButton.Content = on ? "Disconnect remote" : "Connect remote";
        RemoteButton.IsEnabled = !_connecting && _playing == null && (on || Tv.Connected);
        if (!on && !Tv.Connected) RemoteHelp.Text = "Connect to the TV on page 1 first.";
        else if (!on) RemoteHelp.Text = "Lets ScreenWave press the TV's remote buttons over your network. The first time, the TV asks whether to allow ScreenWave – choose Allow with your remote.";
        else if (_rows.Any(r => r.Automatable && Routines.Get(Tv.Model, Tv.PlatformVersion, r.Tip.Key) != null))
            RemoteHelp.Text = "Click Do it for me on a setting and watch the TV – ScreenWave presses the buttons. You can also use the buttons on the right yourself.";
        else
            RemoteHelp.Text = "Do it for me isn't ready for this TV yet – do the settings with your remote, or with the buttons on the right.";
        Pad.IsEnabled = on && _playing == null;

        foreach (var r in _rows)
        {
            r.RemoteOn = on;
            r.HasRoutine = Routines.Get(Tv.Model, Tv.PlatformVersion, r.Tip.Key) != null;
            r.TeachMode = TeachModeBox.IsChecked == true;
            r.CanUse = on && _playing == null && _teaching == null;
        }
    }

    private async void OnRemoteConnect(object sender, RoutedEventArgs e)
    {
        if (TvRemote.Connected)
        {
            CancelTeaching();
            await TvRemote.DisconnectAsync();
            UpdateRemote();
            return;
        }

        _connecting = true;
        UpdateRemote();
        RemoteHelp.Text = "Connecting… If the TV asks whether to allow ScreenWave, choose Allow with your remote.";
        try
        {
            var (ok, message) = await TvRemote.ConnectAsync(Tv.Ip);
            if (!ok) RemoteHelp.Text = message;
        }
        finally
        {
            _connecting = false;
            if (TvRemote.Connected) UpdateRemote(); else { RemoteButton.IsEnabled = Tv.Connected; }
        }
    }

    private async void OnPad(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || !TvRemote.Connected) return;
        try
        {
            await TvRemote.PressAsync(key);
            if (_teaching != null)
            {
                _recorded.Add(key);
                ShowRecorded();
            }
        }
        catch (Exception ex)
        {
            RemoteHelp.Text = $"Couldn't press {TvRemote.Label(key)}: {ex.Message}";
        }
    }

    // ───────────── Teach ─────────────

    private async void OnTeach(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TipRow row } || !TvRemote.Connected) return;
        CancelTeaching();
        _teaching = row;
        row.Teaching = true;
        _recorded.Clear();

        // Start from a normal picture, the same way Do it for me starts
        try
        {
            await TvRemote.PressAsync("KEY_EXIT");
            await Task.Delay(Routines.Gap);
            await TvRemote.PressAsync("KEY_EXIT");
        }
        catch { }

        TeachText.Visibility = Visibility.Visible;
        ShowRecorded();
        UpdateRemote();
    }

    private void ShowRecorded()
    {
        if (_teaching == null) return;
        TeachText.Text = $"Recording \"{_teaching.Tip.Title}\": use the buttons on the right to do it on the TV, then click Save. " +
                         (_recorded.Count == 0 ? "Start with Settings." : "Pressed: " + string.Join(" › ", _recorded.Select(TvRemote.Label)));
    }

    private void OnSaveTeach(object sender, RoutedEventArgs e)
    {
        if (_teaching == null) return;
        if (_recorded.Count == 0)
        {
            TeachText.Text = "Nothing recorded yet – use the buttons on the right first.";
            return;
        }
        Routines.Set(Tv.Model, Tv.PlatformVersion, _teaching.Tip.Key, [.. _recorded]);
        _teaching.Status = $"Recorded for {Routines.Range(Tv.Model)} / Tizen {Tv.PlatformVersion} TVs ({_recorded.Count} presses).";
        _teaching.Selected = true;
        CancelTeaching();
    }

    private void OnCancelTeach(object sender, RoutedEventArgs e) => CancelTeaching();

    private void CancelTeaching()
    {
        if (_teaching != null) _teaching.Teaching = false;
        _teaching = null;
        _recorded.Clear();
        TeachText.Visibility = Visibility.Collapsed;
        UpdateRemote();
    }

    // ───────────── Do it for me ─────────────

    private async void OnDoIt(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TipRow row } || !TvRemote.Connected) return;
        var keys = Routines.Get(Tv.Model, Tv.PlatformVersion, row.Tip.Key);
        if (keys == null) return;

        _playing = new CancellationTokenSource();
        Busy.Enter();
        StopButton.Visibility = Visibility.Visible;
        TeachText.Visibility = Visibility.Visible;
        UpdateRemote();
        try
        {
            await Routines.PlayAsync(keys, (i, key) => Dispatcher.Invoke(() =>
                TeachText.Text = $"Watch the TV – pressing {TvRemote.Label(key)} ({i + 1} of {keys.Count}). Click Stop if it goes wrong."),
                _playing.Token);
            row.Status = "Done – check the TV. These are switches: if it was already off it's now On, so click Do it for me again to switch it back.";
            row.Selected = true;
        }
        catch (OperationCanceledException)
        {
            row.Status = "Stopped.";
        }
        catch (Exception ex)
        {
            row.Status = $"Stopped: {ex.Message}";
        }
        finally
        {
            Busy.Exit();
            _playing.Dispose();
            _playing = null;
            StopButton.Visibility = Visibility.Collapsed;
            TeachText.Visibility = Visibility.Collapsed;
            UpdateRemote();
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _playing?.Cancel();

    private void OnTeachMode(object sender, RoutedEventArgs e)
    {
        if (TeachModeBox.IsChecked != true) CancelTeaching();
        Routines.TeachMode = TeachModeBox.IsChecked == true;
        ExportButton.Visibility = ImportButton.Visibility = TeachModeBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateRemote();
    }

    // ───────────── Sharing recordings between PCs (as a file – never online) ─────────────

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (!Routines.HasAny)
        {
            ProgressText.Text = "Nothing recorded yet.";
            return;
        }
        var dlg = new SaveFileDialog { FileName = "ScreenWave recordings.json", Filter = "ScreenWave recordings|*.json" };
        if (dlg.ShowDialog(OwnerWindow!) != true) return;
        Routines.Export(dlg.FileName);
        ProgressText.Text = $"Saved to {dlg.FileName}.";
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "ScreenWave recordings|*.json" };
        if (dlg.ShowDialog(OwnerWindow!) != true) return;
        try
        {
            var n = Routines.Import(dlg.FileName);
            ProgressText.Text = $"Imported {n} recording(s).";
            UpdateRemote();
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"That file couldn't be read: {ex.Message}";
        }
    }
}
