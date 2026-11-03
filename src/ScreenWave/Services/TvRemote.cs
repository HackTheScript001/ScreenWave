using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ScreenWave.Services;

/// <summary>
/// Presses buttons on the TV's remote from this PC, the same way phone remote apps do: a local
/// connection to the TV on port 8002 (or 8001 on older models). Nothing goes through the internet.
/// The first time, the TV asks "Allow this device?"; after that it remembers ScreenWave with a token
/// that's kept on this PC only.
/// </summary>
public static class TvRemote
{
    private const string AppName = "ScreenWave";
    private static ClientWebSocket? _socket;
    private static string _ip = "";

    public static bool Connected => _socket?.State == WebSocketState.Open;
    public static event Action? ConnectionChanged;

    // The buttons ScreenWave uses, with friendly names
    public static readonly (string Key, string Label)[] Keys =
    [
        ("KEY_UP", "Up"), ("KEY_DOWN", "Down"), ("KEY_LEFT", "Left"), ("KEY_RIGHT", "Right"),
        ("KEY_ENTER", "OK"), ("KEY_RETURN", "Back"), ("KEY_HOME", "Home"), ("KEY_MENU", "Settings"), ("KEY_EXIT", "Exit"),
    ];

    public static string Label(string key) => Keys.FirstOrDefault(k => k.Key == key).Label ?? key;

    // ───────────── Tokens (local only) ─────────────

    private static string TokenFile => Path.Combine(AppInfo.DataFolder, "remote-tokens.json");

    private static Dictionary<string, string> Tokens()
    {
        try { return File.Exists(TokenFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TokenFile)) ?? [] : []; }
        catch { return []; }
    }

    private static void SaveToken(string ip, string token)
    {
        var all = Tokens();
        all[ip] = token;
        try { File.WriteAllText(TokenFile, JsonSerializer.Serialize(all)); } catch { }
    }

    // ───────────── Connecting ─────────────

    /// <summary>Connects to the TV's remote control. The first time, the TV shows an Allow prompt.</summary>
    public static async Task<(bool Ok, string Message)> ConnectAsync(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr) || !Tv.IsLocal(addr))
            return (false, "Connect to the TV on page 1 first.");

        await DisconnectAsync();
        var name = Convert.ToBase64String(Encoding.UTF8.GetBytes(AppName));
        Tokens().TryGetValue(ip, out var token);

        // Newer TVs use a secure connection on 8002 with a token; older ones a plain one on 8001
        var attempts = new[]
        {
            new Uri($"wss://{ip}:8002/api/v2/channels/samsung.remote.control?name={name}" + (token != null ? $"&token={token}" : "")),
            new Uri($"ws://{ip}:8001/api/v2/channels/samsung.remote.control?name={name}"),
        };

        var lastError = "";
        foreach (var uri in attempts)
        {
            var ws = new ClientWebSocket();
            // The TV uses its own self-signed certificate. Only ever accepted for a local address.
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => Tv.IsLocal(addr);
            try
            {
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await ws.ConnectAsync(uri, connectTimeout.Token);

                // The first time, this waits while the TV asks "Allow?" – give the person time to press it
                using var answerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var reply = await ReceiveAsync(ws, answerTimeout.Token);
                using var doc = JsonDocument.Parse(reply);
                var ev = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : "";

                if (ev == "ms.channel.connect")
                {
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                        data.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } newToken)
                        SaveToken(ip, newToken);

                    _socket = ws;
                    _ip = ip;
                    Log.Write($"Remote control connected ({uri.Scheme}:{uri.Port})");
                    _ = DrainAsync(ws);
                    ConnectionChanged?.Invoke();
                    return (true, "Remote connected");
                }

                lastError = ev == "ms.channel.unauthorized"
                    ? "The TV said no. On the TV, allow ScreenWave when it asks (or check Settings › General › External Device Manager › Device Connection Manager)."
                    : $"The TV answered: {ev}";
                ws.Dispose();
                if (ev == "ms.channel.unauthorized") break;
            }
            catch (OperationCanceledException)
            {
                lastError = "The TV didn't answer. If it showed an Allow prompt, press Allow and try again.";
                ws.Dispose();
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                ws.Dispose();
            }
        }
        Log.Write($"Remote control: {lastError}");
        return (false, lastError);
    }

    public static async Task DisconnectAsync()
    {
        var ws = _socket;
        _socket = null;
        if (ws == null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
        }
        catch { }
        ws.Dispose();
        ConnectionChanged?.Invoke();
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, ct);
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Reads (and ignores) anything the TV sends, so the connection stays healthy.</summary>
    private static async Task DrainAsync(ClientWebSocket ws)
    {
        try
        {
            while (ws.State == WebSocketState.Open)
                await ReceiveAsync(ws, CancellationToken.None);
        }
        catch { }
        if (ReferenceEquals(_socket, ws))
        {
            _socket = null;
            Log.Write("Remote control disconnected");
            ConnectionChanged?.Invoke();
        }
    }

    // ───────────── Pressing buttons ─────────────

    public static async Task PressAsync(string key)
    {
        var ws = _socket ?? throw new InvalidOperationException("The remote isn't connected.");
        var json = JsonSerializer.Serialize(new
        {
            method = "ms.remote.control",
            @params = new { Cmd = "Click", DataOfCmd = key, Option = "false", TypeOfRemote = "SendRemoteKey" },
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        Log.Write($"Remote: {Label(key)}");
    }
}
