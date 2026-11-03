using System.ComponentModel;
using System.Diagnostics;

namespace ScreenWave.Services;

/// <summary>
/// For a laptop plugged straight into the TV with an Ethernet cable (no router, no internet):
/// gives this PC's Ethernet port a fixed address the TV can be set to talk to, and puts it back afterwards.
/// </summary>
public static class DirectCable
{
    public const string PcAddress = "192.168.50.1";
    public const string TvAddress = "192.168.50.2";
    public const string Mask = "255.255.255.0";

    public static Task<bool> SetUpAsync(string adapter) =>
        RunElevatedAsync($"interface ipv4 set address name=\"{adapter}\" static {PcAddress} {Mask}",
            $"Set {adapter} to {PcAddress} for a direct cable to the TV");

    public static Task<bool> UndoAsync(string adapter) =>
        RunElevatedAsync($"interface ipv4 set address name=\"{adapter}\" source=dhcp",
            $"Set {adapter} back to automatic (DHCP)");

    /// <summary>netsh needs admin, so Windows asks once. Nothing else in ScreenWave needs admin.</summary>
    private static async Task<bool> RunElevatedAsync(string netshArgs, string what)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(SystemPath("netsh.exe"), netshArgs)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p == null) return false;
            await p.WaitForExitAsync();
            Log.Write($"{what}: {(p.ExitCode == 0 ? "done" : $"netsh code {p.ExitCode}")}");
            return p.ExitCode == 0;
        }
        catch (Win32Exception) // the admin prompt was cancelled
        {
            Log.Write($"{what}: cancelled");
            return false;
        }
    }

    private static string SystemPath(string exe) => System.IO.Path.Combine(Environment.SystemDirectory, exe);
}
