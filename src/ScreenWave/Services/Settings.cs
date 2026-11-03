using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenWave.Services;

/// <summary>
/// ScreenWave's settings, in %LocalAppData%\ScreenWave\settings.json on this PC only.
/// Passwords are only saved if you choose to, and then encrypted with Windows (DPAPI) so only your
/// Windows account on this PC can read them.
/// </summary>
public sealed class Settings
{
    public string AdGuardAddress { get; set; } = "";
    public string AdGuardUser { get; set; } = "";
    public string AdGuardPasswordProtected { get; set; } = "";
    public string PiHoleAddress { get; set; } = "";
    public string PiHolePasswordProtected { get; set; } = "";

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "settings.json");
    private static Settings? _current;
    public static Settings Current => _current ??= Load();

    private static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Write($"Settings couldn't be saved: {ex.Message}"); }
    }

    public static string Protect(string secret) =>
        secret.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string data)
    {
        if (data.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(data), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
