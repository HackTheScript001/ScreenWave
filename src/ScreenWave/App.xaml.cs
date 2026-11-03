using System.Windows;
using System.Windows.Threading;
using ScreenWave.Services;

namespace ScreenWave;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        Log.Write($"ScreenWave {AppInfo.VersionText} starting");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Leave nothing running: stop sdb's local helper
        Sdb.KillServer();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write($"Unhandled: {e.Exception}");
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails are in {Log.FilePath}",
            "ScreenWave", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
