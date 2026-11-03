using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenWave.Services;

/// <summary>
/// Adds the block list to your own AdGuard Home or Pi-hole, over your local network only.
/// The address must be a private local one – ScreenWave refuses anything on the internet.
/// </summary>
public static class Blockers
{
    // Samsung's app-store servers the TV uses to (re)install preloaded apps
    public static readonly string[] Domains =
    [
        "osb-v2.samsungqbe.com",
        "osb-apps.samsungqbe.com",
        "osb-apps-v2.samsungqbe.com",
        "osb-auth-eusvc-v2.samsungqbe.com",
    ];

    public const string Comment = "Added by ScreenWave – stops Samsung reinstalling removed TV apps";

    /// <summary>
    /// Turns "192.168.0.10:3000" or "http://pi.hole/admin" into a base address, and checks it's on the local network.
    /// </summary>
    public static async Task<Uri> LocalBaseAsync(string address, string defaultPath = "")
    {
        address = address.Trim().TrimEnd('/');
        if (address.Length == 0) throw new InvalidOperationException("Enter its address in Settings first.");
        if (!address.Contains("://")) address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new InvalidOperationException("That address doesn't look right. Use something like 192.168.0.10:3000.");

        var ips = IPAddress.TryParse(uri.Host, out var ip) ? new[] { ip } : await Dns.GetHostAddressesAsync(uri.Host);
        var v4 = ips.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        if (v4.Count == 0 || !v4.All(Tv.IsLocal))
            throw new InvalidOperationException("That address isn't on your local network, so ScreenWave won't connect to it.");

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = defaultPath;
        return new Uri($"{uri.Scheme}://{uri.Authority}{path}/");
    }

    private static HttpClient Client(bool allowSelfSigned)
    {
        var handler = new HttpClientHandler();
        // Home servers often use their own certificate on https – fine, because the address is checked to be local
        if (allowSelfSigned) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ───────────── AdGuard Home ─────────────

    /// <summary>Adds the rules to AdGuard Home's custom filtering rules, keeping everything already there.</summary>
    public static async Task<string> AddToAdGuardAsync(string address, string user, string password)
    {
        var baseUri = await LocalBaseAsync(address);
        using var http = Client(baseUri.Scheme == "https");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

        // Saving replaces the whole list, so read the current rules first
        using var statusResp = await http.GetAsync(new Uri(baseUri, "control/filtering/status"));
        if (statusResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("AdGuard Home didn't accept the username or password.");
        statusResp.EnsureSuccessStatusCode();
        var status = JsonNode.Parse(await statusResp.Content.ReadAsStringAsync());
        var rules = status?["user_rules"]?.AsArray().Select(n => n?.GetValue<string>() ?? "").ToList() ?? [];

        var wanted = Domains.Select(d => $"||{d}^").ToList();
        var missing = wanted.Where(w => !rules.Contains(w)).ToList();
        if (missing.Count == 0) return "AdGuard Home already blocks all of them.";

        rules.Add($"! {Comment}");
        rules.AddRange(missing);
        var body = new StringContent(JsonSerializer.Serialize(new { rules }), Encoding.UTF8, "application/json");
        using var setResp = await http.PostAsync(new Uri(baseUri, "control/filtering/set_rules"), body);
        if (!setResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"AdGuard Home refused the change ({(int)setResp.StatusCode}). {await setResp.Content.ReadAsStringAsync()}".Trim());

        Log.Write($"Added {missing.Count} rule(s) to AdGuard Home at {baseUri.Authority}");
        return $"Added {missing.Count} rule(s) to AdGuard Home.";
    }

    public static async Task<Uri> AdGuardPageAsync(string address) => new(await LocalBaseAsync(address), "#custom_rules");

    // ───────────── Pi-hole ─────────────

    /// <summary>Adds each address as an exact block entry. Pi-hole v6 (password); falls back to v5 (API token).</summary>
    public static async Task<string> AddToPiHoleAsync(string address, string password)
    {
        var root = await LocalBaseAsync(address);
        // Accept either the server address or the /admin page address
        var server = new Uri(root.GetLeftPart(UriPartial.Authority) + "/");
        using var http = Client(server.Scheme == "https");

        // Pi-hole v6
        using var authResp = await http.PostAsync(new Uri(server, "api/auth"),
            new StringContent(JsonSerializer.Serialize(new { password }), Encoding.UTF8, "application/json"));
        if (authResp.StatusCode != HttpStatusCode.NotFound)
        {
            var auth = JsonNode.Parse(await authResp.Content.ReadAsStringAsync());
            var valid = auth?["session"]?["valid"]?.GetValue<bool>() == true;
            if (!valid) throw new InvalidOperationException("Pi-hole didn't accept the password.");
            var sid = auth?["session"]?["sid"]?.GetValue<string>();

            try
            {
                var added = 0;
                foreach (var d in Domains)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "api/domains/deny/exact"))
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new { domain = d, comment = Comment, groups = new[] { 0 }, enabled = true }),
                            Encoding.UTF8, "application/json"),
                    };
                    if (!string.IsNullOrEmpty(sid)) req.Headers.Add("sid", sid);
                    using var resp = await http.SendAsync(req);
                    var text = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode) added++;
                    else if (!text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) && !text.Contains("exist", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Pi-hole refused {d} ({(int)resp.StatusCode}).");
                }
                Log.Write($"Added {added} domain(s) to Pi-hole at {server.Authority}");
                return added == 0 ? "Pi-hole already blocks all of them." : $"Added {added} address(es) to Pi-hole.";
            }
            finally
            {
                if (!string.IsNullOrEmpty(sid))
                {
                    using var logout = new HttpRequestMessage(HttpMethod.Delete, new Uri(server, "api/auth"));
                    logout.Headers.Add("sid", sid);
                    try { using var _ = await http.SendAsync(logout); } catch { }
                }
            }
        }

        // Pi-hole v5: the "password" here is the API token from Settings › API
        var count = 0;
        foreach (var d in Domains)
        {
            var url = new Uri(server, $"admin/api.php?list=black&add={Uri.EscapeDataString(d)}&auth={Uri.EscapeDataString(password)}");
            var text = await http.GetStringAsync(url);
            if (text.Contains("Not authorized", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pi-hole didn't accept the API token (Settings › API › Show API token).");
            count++;
        }
        Log.Write($"Added {count} domain(s) to Pi-hole v5 at {server.Authority}");
        return $"Added {count} address(es) to Pi-hole.";
    }

    public static async Task<Uri> PiHolePageAsync(string address)
    {
        var root = await LocalBaseAsync(address);
        return new Uri(root.GetLeftPart(UriPartial.Authority) + "/admin/groups/domains");
    }
}
