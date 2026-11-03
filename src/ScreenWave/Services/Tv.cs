using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ScreenWave.Services;

public sealed record LocalAddress(string AdapterName, string AdapterId, IPAddress Address, IPAddress Mask, bool IsEthernet)
{
    public string AddressText => Address.ToString();
}

/// <summary>The connection to the TV. Everything here stays on the local network.</summary>
public static class Tv
{
    public static string? Serial { get; private set; }          // e.g. "192.168.1.50:26101"
    public static string Details { get; private set; } = "";
    public static bool Connected => Serial != null;

    public static event Action? ConnectionChanged;

    // ───────────── This PC's addresses (for the TV's Developer Mode "Host PC IP") ─────────────

    public static List<LocalAddress> LocalAddresses()
    {
        var list = new List<LocalAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (u.Address.ToString().StartsWith("169.254.")) continue; // no address yet
                list.Add(new LocalAddress(nic.Name, nic.Id, u.Address, u.IPv4Mask,
                    nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet));
            }
        }
        return list;
    }

    /// <summary>Ethernet adapters (connected or not) – for the direct-cable setup.</summary>
    public static List<(string Name, bool Up)> EthernetAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet &&
                        !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            .Select(n => (n.Name, n.OperationalStatus == OperationalStatus.Up))
            .ToList();

    // ───────────── Finding the TV ─────────────

    /// <summary>Looks for TVs in Developer Mode on this PC's own networks (port 26101). Local network only.</summary>
    public static async Task<List<IPAddress>> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var targets = new List<IPAddress>();
        foreach (var a in LocalAddresses())
        {
            // Scan the /24 the PC is on (bigger networks would take too long)
            var bytes = a.Address.GetAddressBytes();
            for (var i = 1; i < 255; i++)
            {
                var ip = new IPAddress(new byte[] { bytes[0], bytes[1], bytes[2], (byte)i });
                if (!ip.Equals(a.Address)) targets.Add(ip);
            }
        }
        targets = targets.Distinct().ToList();

        var found = new System.Collections.Concurrent.ConcurrentBag<IPAddress>();
        var done = 0;
        using var gate = new SemaphoreSlim(64);
        var tasks = targets.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (await PortOpenAsync(ip, Sdb.TvPort, 400, ct)) found.Add(ip);
            }
            finally
            {
                gate.Release();
                progress?.Report(Interlocked.Increment(ref done) * 100 / Math.Max(1, targets.Count));
            }
        });
        await Task.WhenAll(tasks);
        return found.OrderBy(ip => ip.GetAddressBytes()[3]).ToList();
    }

    private static async Task<bool> PortOpenAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    public static bool LooksLikeIp(string s) =>
        IPAddress.TryParse(s.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && IsLocal(ip);

    /// <summary>Only private, local addresses are allowed – ScreenWave never connects outside your network.</summary>
    public static bool IsLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254);
    }

    // ───────────── Connecting ─────────────

    public static async Task<(bool Ok, string Message)> ConnectAsync(string ip)
    {
        ip = ip.Trim();
        if (!LooksLikeIp(ip)) return (false, "That isn't a local network address (it should look like 192.168.1.50).");

        var serial = $"{ip}:{Sdb.TvPort}";
        var r = await Sdb.RunAsync(["connect", serial], TimeSpan.FromSeconds(20));
        if (!Regex.IsMatch(r.Output, @"connected to", RegexOptions.IgnoreCase) || r.Output.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return (false, r.TimedOut
                ? "The TV didn't answer. Check Developer Mode is on, the Host PC IP is this PC's address, and the TV was restarted."
                : $"Couldn't connect: {FirstLine(r.Output)}");
        }

        // The TV only accepts the PC whose address is set as Host PC IP in Developer Mode
        var devices = await Sdb.RunAsync(["devices"], TimeSpan.FromSeconds(10));
        var line = devices.Output.Split('\n').FirstOrDefault(l => l.StartsWith(serial));
        if (line == null || line.Contains("offline", StringComparison.OrdinalIgnoreCase) || line.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return (false, "The TV answered but didn't let this PC in. In Developer Mode, set Host PC IP to this PC's address and restart the TV.");

        var cap = await Sdb.OnTvAsync(serial, "capability");
        Details = Summarise(cap.Output, line);
        Serial = serial;
        Log.Write($"Connected to the TV at {ip}");
        ConnectionChanged?.Invoke();
        return (true, "Connected");
    }

    public static async Task DisconnectAsync()
    {
        if (Serial == null) return;
        await Sdb.RunAsync(["disconnect", Serial], TimeSpan.FromSeconds(10));
        Log.Write("Disconnected from the TV");
        Serial = null;
        Details = "";
        ConnectionChanged?.Invoke();
    }

    private static string Summarise(string capability, string deviceLine)
    {
        var info = capability.Split('\n')
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        string Get(string k) => info.TryGetValue(k, out var v) ? v : "";

        var parts = new List<string>();
        var model = deviceLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(model) && model != "device") parts.Add(model);
        if (Get("platform_version").Length > 0) parts.Add($"Tizen {Get("platform_version")}");
        if (Get("profile_name").Length > 0) parts.Add(Get("profile_name"));
        return string.Join("  ·  ", parts);
    }

    private static string FirstLine(string s) => s.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "no answer";
}
