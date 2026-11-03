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
        });
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateButtons();
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

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_busy || Tv.Serial is not { } serial) return;
        var chosen = _rows.Where(r => r.Selected && r.CanRemove).ToList();
        if (chosen.Count == 0) return;

        var names = string.Join("\n", chosen.Take(15).Select(r => "•  " + r.Title)) + (chosen.Count > 15 ? $"\n…and {chosen.Count - 15} more" : "");
        if (MessageBox.Show(OwnerWindow!, $"Remove {chosen.Count} app(s) from the TV?\n\n{names}\n\nYou can reinstall them later from the TV's Apps store.",
                "Remove apps", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
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
            var removed = new List<RemovedApp>();
            foreach (var r in chosen)
            {
                if (stillThere.Contains(r.Id))
                {
                    r.Status = "TV refused";
                }
                else
                {
                    r.MarkRemoved();
                    removed.Add(new RemovedApp(r.Title, r.Id, DateTime.Now, Tv.Details));
                }
            }
            TvApps.RecordRemoved(removed);

            var refused = chosen.Count - removed.Count;
            StatusText.Text = $"Removed {removed.Count} app(s)" +
                              (refused > 0 ? $". The TV wouldn't remove {refused} – open Details to see what it said." : ".") +
                              " Next: 3. Speed-up settings.";
        }
        catch (Exception ex)
        {
            Log.Write($"Remove failed: {ex}");
            StatusText.Text = $"Remove failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
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
