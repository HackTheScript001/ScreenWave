using System.Diagnostics;
using System.IO;

namespace ScreenWave.Services;

/// <summary>Checks the installer's Windows Firewall rules that keep ScreenWave and sdb off the internet.</summary>
public static class FirewallLock
{
    public const string RuleName = "ScreenWave - local network only";

    /// <summary>True when all four rules (in/out for ScreenWave and sdb) are in place.</summary>
    public static async Task<bool> IsOnAsync()
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                $"advfirewall firewall show rule name=\"{RuleName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            // Each rule is listed with a "Rule Name:" line; the name itself appears on each of them
            var rules = output.Split('\n').Count(l => l.Contains(RuleName, StringComparison.OrdinalIgnoreCase));
            return p.ExitCode == 0 && rules >= 4;
        }
        catch
        {
            return false;
        }
    }
}
