using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ScreenWave.Services;

namespace ScreenWave.Views;

public partial class SettingsView : UserControl
{
    // Passwords typed this session but not saved – kept in memory only
    public static string AdGuardPasswordThisSession { get; private set; } = "";
    public static string PiHolePasswordThisSession { get; private set; } = "";

    public SettingsView()
    {
        InitializeComponent();
        var s = Settings.Current;
        AgAddress.Text = s.AdGuardAddress;
        AgUser.Text = s.AdGuardUser;
        AgPassword.Password = Settings.Unprotect(s.AdGuardPasswordProtected);
        AgRemember.IsChecked = s.AdGuardPasswordProtected.Length > 0;
        PhAddress.Text = s.PiHoleAddress;
        PhPassword.Password = Settings.Unprotect(s.PiHolePasswordProtected);
        PhRemember.IsChecked = s.PiHolePasswordProtected.Length > 0;
    }

    public static string AdGuardPassword =>
        AdGuardPasswordThisSession.Length > 0 ? AdGuardPasswordThisSession : Settings.Unprotect(Settings.Current.AdGuardPasswordProtected);

    public static string PiHolePassword =>
        PiHolePasswordThisSession.Length > 0 ? PiHolePasswordThisSession : Settings.Unprotect(Settings.Current.PiHolePasswordProtected);

    private async void OnSaveAdGuard(object sender, RoutedEventArgs e)
    {
        try
        {
            await Blockers.LocalBaseAsync(AgAddress.Text);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }
        var s = Settings.Current;
        s.AdGuardAddress = AgAddress.Text.Trim();
        s.AdGuardUser = AgUser.Text.Trim();
        AdGuardPasswordThisSession = AgPassword.Password;
        s.AdGuardPasswordProtected = AgRemember.IsChecked == true ? Settings.Protect(AgPassword.Password) : "";
        s.Save();
        StatusText.Text = "AdGuard Home saved. Use Add to AdGuard Home on 4. Stop apps coming back.";
    }

    private async void OnSavePiHole(object sender, RoutedEventArgs e)
    {
        try
        {
            await Blockers.LocalBaseAsync(PhAddress.Text);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }
        var s = Settings.Current;
        s.PiHoleAddress = PhAddress.Text.Trim();
        PiHolePasswordThisSession = PhPassword.Password;
        s.PiHolePasswordProtected = PhRemember.IsChecked == true ? Settings.Protect(PhPassword.Password) : "";
        s.Save();
        StatusText.Text = "Pi-hole saved. Use Add to Pi-hole on 4. Stop apps coming back.";
    }

    private async void OnOpenAdGuard(object sender, RoutedEventArgs e)
    {
        try { Open(await Blockers.LocalBaseAsync(AgAddress.Text)); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OnOpenPiHole(object sender, RoutedEventArgs e)
    {
        try { Open(await Blockers.PiHolePageAsync(PhAddress.Text)); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    public static void Open(Uri localPage)
    {
        try { Process.Start(new ProcessStartInfo(localPage.ToString()) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"Couldn't open {localPage}: {ex.Message}"); }
    }
}
