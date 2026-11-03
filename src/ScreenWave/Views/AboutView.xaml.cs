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
        try
        {
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                "advfirewall firewall show rule name=\"ScreenWave - local network only\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var rules = output.Split('\n').Count(l => l.TrimStart().StartsWith("Rule Name", StringComparison.OrdinalIgnoreCase));
            FirewallText.Foreground = (System.Windows.Media.Brush)FindResource(rules >= 4 ? "Good" : "Warn");
            FirewallText.Text = rules >= 4
                ? "✓ Firewall lock is on: ScreenWave and sdb can't reach the internet."
                : "Firewall lock isn't in place (it's added by the installer). Reinstall ScreenWave to add it.";
        }
        catch
        {
            FirewallText.Text = "";
        }
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.DataFolder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"Couldn't open the folder: {ex.Message}"); }
    }
}
