using System.Windows;
using System.Windows.Controls;
using ScreenWave.Models;
using ScreenWave.Services;

namespace ScreenWave.Views;

public sealed class TipRow : ObservableItem
{
    public required Tip Tip { get; init; }
}

public partial class TipsView : UserControl
{
    private readonly List<TipRow> _rows;

    public TipsView()
    {
        InitializeComponent();
        var done = Checklist.Done();
        _rows = Checklist.Tips.Select(t => new TipRow { Tip = t, Selected = done.Contains(t.Key) }).ToList();
        foreach (var r in _rows)
            r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TipRow.Selected)) Changed(); };
        TipList.ItemsSource = _rows;
        UpdateText();
    }

    private void Changed()
    {
        Checklist.Save(_rows.Where(r => r.Selected).Select(r => r.Tip.Key));
        UpdateText();
    }

    private void UpdateText()
    {
        var n = _rows.Count(r => r.Selected);
        ProgressText.Text = n == _rows.Count
            ? "All done. Next: 4. Stop apps coming back."
            : $"{n} of {_rows.Count} done.";
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Selected = false;
    }
}
