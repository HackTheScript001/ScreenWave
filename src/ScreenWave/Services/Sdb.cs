using System.Diagnostics;
using System.IO;

namespace ScreenWave.Services;

public sealed record SdbResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Ok => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs Samsung's sdb tool (shipped inside ScreenWave, in the sdb folder next to the app).
/// sdb only talks to the TV's address on your local network – nothing goes to the internet.
/// </summary>
public static class Sdb
{
    public const int TvPort = 26101;

    public static string ExePath => Path.Combine(AppInfo.AppFolder, "sdb", "sdb.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    public static async Task<SdbResult> RunAsync(IEnumerable<string> args, TimeSpan? timeout = null)
    {
        if (!IsAvailable) return new SdbResult(-1, $"sdb.exe wasn't found at {ExePath}. Reinstall ScreenWave.", false);

        var argList = args.ToList();
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (var a in argList) psi.ArgumentList.Add(a);

        Log.Write("sdb " + string.Join(" ", argList));
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start sdb");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        var timedOut = false;
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { p.Kill(entireProcessTree: true); } catch { }
        }

        var output = ((await stdout) + (await stderr)).Replace("\r\n", "\n").Trim();
        var code = timedOut ? -1 : p.ExitCode;
        foreach (var line in output.Split('\n').Take(400))
            if (!string.IsNullOrWhiteSpace(line)) Log.Write("  " + line.TrimEnd());
        if (timedOut) Log.Write("  (sdb didn't answer in time)");
        return new SdbResult(code, output, timedOut);
    }

    public static Task<SdbResult> OnTvAsync(string serial, params string[] args) =>
        RunAsync(new[] { "-s", serial }.Concat(args));

    /// <summary>Stops sdb's local helper so nothing is left running after ScreenWave closes.</summary>
    public static void KillServer()
    {
        if (!IsAvailable) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(ExePath, "kill-server")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(5000);
        }
        catch { }
    }
}
