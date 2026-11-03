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
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.DataFolder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"Couldn't open the folder: {ex.Message}"); }
    }
}
