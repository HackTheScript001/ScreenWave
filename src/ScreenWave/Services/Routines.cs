using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenWave.Services;

/// <summary>
/// "Do it for me": button presses recorded once (taught) for a setting on a TV model range, replayed later.
/// Kept on this PC only, in %LocalAppData%\ScreenWave\routines.json. Can be exported/imported as a file
/// to use the same recordings on another PC.
/// </summary>
public static class Routines
{
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(1100); // menus animate, so don't rush

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "routines.json");

    // range (e.g. "AU8000") → setting key → keys
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

    /// <summary>
    /// The model range a recording applies to: UE50AU8000KXXU → AU8000, QE55Q60AAUXXU → Q60A.
    /// Different screen sizes in the same range share menus.
    /// </summary>
    public static string Range(string model)
    {
        var m = Regex.Match(model ?? "", @"^[A-Z]{2}\d{2}([A-Z]+\d+[A-Z]?)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : (string.IsNullOrWhiteSpace(model) ? "unknown" : model.ToUpperInvariant());
    }

    public static List<string>? Get(string model, string setting) =>
        Load().TryGetValue(Range(model), out var r) && r.TryGetValue(setting, out var keys) && keys.Count > 0 ? keys : null;

    public static void Set(string model, string setting, List<string> keys)
    {
        var all = Load();
        var range = Range(model);
        if (!all.TryGetValue(range, out var r)) all[range] = r = [];
        r[setting] = keys;
        Save(all);
        Log.Write($"Saved {keys.Count} button presses for '{setting}' on {range} TVs");
    }

    public static void Forget(string model, string setting)
    {
        var all = Load();
        if (all.TryGetValue(Range(model), out var r) && r.Remove(setting)) Save(all);
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
