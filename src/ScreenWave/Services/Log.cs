using System.IO;

namespace ScreenWave.Services;

/// <summary>Local log at %LocalAppData%\ScreenWave\ScreenWave.log – never uploaded anywhere.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppInfo.DataFolder, "ScreenWave.log");

    public static event Action<string>? LineWritten;

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { }
        }
        LineWritten?.Invoke(line);
    }
}
