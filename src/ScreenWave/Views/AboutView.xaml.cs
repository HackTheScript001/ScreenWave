using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ScreenWave.Services;

namespace ScreenWave.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        VersionLine.Text = $"Version {AppInfo.VersionText}";
        Loaded += async (_, _) => await CheckFirewallAsync();
    }

    /// <summary>Shows whether the installer's "local network only" firewall rules are in place.</summary>
    private async Task CheckFirewallAsync()
    {
        var on = await FirewallLock.IsOnAsync();
        FirewallText.Foreground = (System.Windows.Media.Brush)FindResource(on ? "Good" : "Warn");
        FirewallText.Text = on
            ? "✓ Firewall lock is on: ScreenWave and sdb can't reach the internet."
            : "Firewall lock isn't in place (it's added by the installer). Reinstall ScreenWave to add it.";
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.DataFolder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"Couldn't open the folder: {ex.Message}"); }
    }
}
