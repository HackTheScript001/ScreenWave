using System.Windows;
using System.Windows.Controls;
using ScreenWave.Services;

namespace ScreenWave.Views;

public partial class FinishView : UserControl
{
    public FinishView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DisconnectButton.IsEnabled = Tv.Connected;
        var removed = TvApps.Removed();
        RemovedText.Text = removed.Count == 0
            ? "None yet."
            : string.Join("\n", removed.OrderByDescending(r => r.When).Select(r => $"{r.When:dd/MM/yyyy HH:mm}   {r.Title}"));
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        await Tv.DisconnectAsync();
        DisconnectButton.IsEnabled = false;
    }
}
