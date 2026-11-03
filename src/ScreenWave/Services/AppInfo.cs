using System.IO;
using System.Reflection;

namespace ScreenWave.Services;

public static class AppInfo
{
    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    public static string AppFolder => AppContext.BaseDirectory;

    /// <summary>
    /// Everything ScreenWave keeps (log, list of removed apps, checklist) lives here on this PC only:
    /// %LocalAppData%\ScreenWave. Nothing is ever sent anywhere.
    /// </summary>
    public static string DataFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenWave");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
