using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenWave.Services;

/// <summary>
/// "Do it for me": button presses for a setting, replayed through the TV's remote.
///
/// Built-in recordings ship inside ScreenWave (Assets/routines-builtin.json), keyed by Tizen version –
/// Samsung keeps the same settings menus across a year's TVs, so "tizen-6.0" covers 2021 models.
/// Recordings made with Teach mode are saved on this PC (%LocalAppData%\ScreenWave\routines.json)
/// under both the TV's Tizen version and its model range, and override the built-in ones.
/// Export writes them to a file so they can be built into the next release.
/// </summary>
public static class Routines
{
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1100); // menus animate, so don't rush

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "routines.json");

    // key ("tizen-6.0" or a model range like "AU8000") → setting → keys
    private static Dictionary<string, Dictionary<string, List<string>>>? _builtIn;

    private static Dictionary<string, Dictionary<string, List<string>>> BuiltIn => _builtIn ??= LoadBuiltIn();

    private static Dictionary<string, Dictionary<string, List<string>>> LoadBuiltIn()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("routines-builtin.json", StringComparison.OrdinalIgnoreCase));
            if (name == null) return [];
            using var stream = asm.GetManifestResourceStream(name)!;
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(stream) ?? [];
        }
        catch (Exception ex)
        {
            Log.Write($"Built-in recordings couldn't be read: {ex.Message}");
            return [];
        }
    }

    private static Dictionary<string, Dictionary<string, List<string>>> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch { return []; }
    }

    private static void Save(Dictionary<string, Dictionary<string, List<string>>> all)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Write($"Couldn't save recordings: {ex.Message}"); }
    }

    /// <summary>UE50AU8000KXXU → AU8000, QE55Q60AAUXXU → Q60A. Screen sizes in a range share menus.</summary>
    public static string Range(string model)
    {
        var m = Regex.Match(model ?? "", @"^[A-Z]{2}\d{2}([A-Z]+\d+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : (string.IsNullOrWhiteSpace(model) ? "unknown" : model.ToUpperInvariant());
    }

    public static string YearKey(string platformVersion) => $"tizen-{(string.IsNullOrWhiteSpace(platformVersion) ? "unknown" : platformVersion.Trim())}";

    private static IEnumerable<string> Keys(string model, string platform) => [Range(model), YearKey(platform)];

    /// <summary>The recording to use: this PC's own first, then the built-in one for this TV's year.</summary>
    public static List<string>? Get(string model, string platform, string setting)
    {
        var mine = Load();
        foreach (var k in Keys(model, platform))
            if (mine.TryGetValue(k, out var r) && r.TryGetValue(setting, out var keys) && keys.Count > 0) return keys;
        foreach (var k in Keys(model, platform))
            if (BuiltIn.TryGetValue(k, out var r) && r.TryGetValue(setting, out var keys) && keys.Count > 0) return keys;
        return null;
    }

    public static bool IsBuiltIn(string model, string platform, string setting)
    {
        var mine = Load();
        if (Keys(model, platform).Any(k => mine.TryGetValue(k, out var r) && r.ContainsKey(setting))) return false;
        return Keys(model, platform).Any(k => BuiltIn.TryGetValue(k, out var r) && r.ContainsKey(setting));
    }

    public static void Set(string model, string platform, string setting, List<string> keys)
    {
        var all = Load();
        foreach (var k in Keys(model, platform))
        {
            if (!all.TryGetValue(k, out var r)) all[k] = r = [];
            r[setting] = keys;
        }
        Save(all);
        Log.Write($"Saved {keys.Count} button presses for '{setting}' ({Range(model)}, {YearKey(platform)})");
    }

    public static void Export(string file) => File.Copy(FilePath, file, overwrite: true);

    public static int Import(string file)
    {
        var incoming = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(File.ReadAllText(file)) ?? [];
        var allowed = TvRemote.Keys.Select(k => k.Key).ToHashSet();
        var all = Load();
        var n = 0;
        foreach (var (range, settings) in incoming)
        {
            if (!all.TryGetValue(range, out var r)) all[range] = r = [];
            foreach (var (setting, keys) in settings)
            {
                // Only plain remote buttons are accepted from a file
                if (keys.Count == 0 || keys.Count > 60 || keys.Any(k => !allowed.Contains(k))) continue;
                r[setting] = keys;
                n++;
            }
        }
        Save(all);
        return n;
    }

    public static bool HasAny => File.Exists(FilePath) && Load().Values.Any(v => v.Count > 0);

    /// <summary>Replays a recording. Starts by pressing Exit twice so the TV is on a normal picture, like when it was taught.</summary>
    public static async Task PlayAsync(List<string> keys, Action<int, string>? onStep, CancellationToken ct)
    {
        await TvRemote.PressAsync("KEY_EXIT");
        await Task.Delay(Gap, ct);
        await TvRemote.PressAsync("KEY_EXIT");
        await Task.Delay(Gap, ct);
        for (var i = 0; i < keys.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            onStep?.Invoke(i, keys[i]);
            await TvRemote.PressAsync(keys[i]);
            await Task.Delay(Gap, ct);
        }
    }
}
