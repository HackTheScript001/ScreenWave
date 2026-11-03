using System.Windows;
using System.Windows.Controls;
using ScreenWave.Services;

namespace ScreenWave.Views;

public partial class BlockListView : UserControl
{
    public BlockListView()
    {
        InitializeComponent();
        DomainBox.Text = string.Join(Environment.NewLine, Blockers.Domains);
    }

    // ───────────── One click ─────────────

    private async void OnAddAdGuard(object sender, RoutedEventArgs e)
    {
        var s = Settings.Current;
        if (s.AdGuardAddress.Length == 0) { GoToSettings("Add your AdGuard Home address in Settings first."); return; }
        if (SettingsView.AdGuardPassword.Length == 0) { GoToSettings("Enter your AdGuard Home password in Settings first."); return; }

        AdGuardButton.IsEnabled = false;
        AddText.Text = "Adding to AdGuard Home…";
        try
        {
            AddText.Text = await Blockers.AddToAdGuardAsync(s.AdGuardAddress, s.AdGuardUser, SettingsView.AdGuardPassword) + " Opening AdGuard Home so you can see them.";
            SettingsView.Open(await Blockers.AdGuardPageAsync(s.AdGuardAddress));
        }
        catch (Exception ex)
        {
            Log.Write($"AdGuard Home: {ex.Message}");
            AddText.Text = $"Couldn't add them: {ex.Message}";
        }
        finally { AdGuardButton.IsEnabled = true; }
    }

    private async void OnAddPiHole(object sender, RoutedEventArgs e)
    {
        var s = Settings.Current;
        if (s.PiHoleAddress.Length == 0) { GoToSettings("Add your Pi-hole address in Settings first."); return; }

        PiHoleButton.IsEnabled = false;
        AddText.Text = "Adding to Pi-hole…";
        try
        {
            AddText.Text = await Blockers.AddToPiHoleAsync(s.PiHoleAddress, SettingsView.PiHolePassword) + " Opening Pi-hole so you can see them.";
            SettingsView.Open(await Blockers.PiHolePageAsync(s.PiHoleAddress));
        }
        catch (Exception ex)
        {
            Log.Write($"Pi-hole: {ex.Message}");
            AddText.Text = $"Couldn't add them: {ex.Message}";
        }
        finally { PiHoleButton.IsEnabled = true; }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => GoToSettings(null);

    private void GoToSettings(string? why)
    {
        if (why != null) AddText.Text = why;
        if (Window.GetWindow(this) is MainWindow w) w.GoTo("settings");
    }

    // ───────────── Copy ─────────────

    private void Copy(string text, string what)
    {
        try
        {
            Clipboard.SetText(text);
            CopiedText.Text = $"Copied {what} – paste it into your blocker.";
        }
        catch
        {
            CopiedText.Text = "Couldn't reach the clipboard – select the addresses above and copy them instead.";
        }
    }

    private void OnCopyAdGuard(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Blockers.Domains.Select(d => $"||{d}^")), "AdGuard Home rules");

    private void OnCopyHosts(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Blockers.Domains.Select(d => $"0.0.0.0 {d}")), "hosts-file lines");

    private void OnCopyPlain(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Blockers.Domains), "the addresses");
}
