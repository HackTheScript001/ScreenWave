using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenWave.Services;

public enum AppKind
{
    Removable,   // the TV says it can be removed (store and preloaded partner apps)
    Core,        // part of Samsung's system – never touched
}

public sealed record TvApp(string Id, string Title, AppKind Kind, bool Suggested, IReadOnlyDictionary<string, string> Fields);

public sealed record RemovedApp(string Title, string Id, DateTime When, string Tv);

/// <summary>
/// Lists and removes apps through Developer Mode (sdb shell 0 vd_applist / vd_appuninstall).
/// Only apps the TV marks as removable can be removed; Samsung's core packages are always left alone.
/// </summary>
public static class TvApps
{
    // Preloaded apps most people never open. Only ticked if the TV says they're removable.
    private static readonly string[] SuggestedTitles =
    [
        "Rakuten", "Facebook", "Samsung Promotion", "Samsung Health", "Samsung Daily", "Art Store",
        "Alexa", "Samsung Kids", "Samsung TV Plus Kids", "Gallery", "Samsung Food", "SmartThings Find",
    ];

    private static readonly string[] IdKeys = ["app_tizen_id", "app_id", "appid", "tizen_id", "package_id", "pkgid", "id"];
    private static readonly string[] TitleKeys = ["app_title", "title", "app_name", "name", "label"];

    public static async Task<(List<TvApp> Apps, string Raw)> ListAsync(string serial)
    {
        var r = await Sdb.OnTvAsync(serial, "shell", "0", "vd_applist");
        if (!r.Ok && r.Output.Length == 0) throw new InvalidOperationException("The TV didn't answer the app list request.");
        if (r.Output.Trim().Equals("closed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This TV refused the app list (it answered \"closed\"). Its firmware may block it.");
        return (Parse(r.Output), r.Output);
    }

    /// <summary>
    /// vd_applist prints blocks of "key = value" lines, one block per app, usually separated by dashed lines.
    /// The exact keys vary by year, so this accepts either "=" or ":" and several key names.
    /// </summary>
    public static List<TvApp> Parse(string output)
    {
        var records = new List<Dictionary<string, string>>();
        var cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (cur.Count > 0) records.Add(cur);
            cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Regex.IsMatch(line, @"^[-=_*]{5,}$")) { Flush(); continue; }

            var m = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_ ]*?)\s*[=:]\s*(.*)$");
            if (!m.Success) continue;
            var key = m.Groups[1].Value.Trim().Replace(' ', '_');
            var value = m.Groups[2].Value.Trim().Trim('"');
            if (cur.ContainsKey(key) && IdKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) Flush(); // next app started
            cur[key] = value;
        }
        Flush();

        var apps = new List<TvApp>();
        foreach (var rec in records)
        {
            var id = IdKeys.Select(k => rec.TryGetValue(k, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (id == null) continue;
            var title = TitleKeys.Select(k => rec.TryGetValue(k, out var v) ? v : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? id;

            var kind = IsCore(id, rec) ? AppKind.Core : AppKind.Removable;
            var suggested = kind == AppKind.Removable && SuggestedTitles.Any(s => title.Contains(s, StringComparison.OrdinalIgnoreCase));
            apps.Add(new TvApp(id, title, kind, suggested, rec));
        }

        return apps
            .GroupBy(a => a.Id).Select(g => g.First())
            .OrderBy(a => a.Kind).ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCore(string id, Dictionary<string, string> rec)
    {
        if (id.StartsWith("org.tizen", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("com.samsung", StringComparison.OrdinalIgnoreCase)) return true;

        if (rec.TryGetValue("is_removable", out var r) || rec.TryGetValue("removable", out r))
            return !(r == "1" || r.Equals("true", StringComparison.OrdinalIgnoreCase) || r.Equals("yes", StringComparison.OrdinalIgnoreCase));

        // No flag: only store-style IDs (long numbers, e.g. 3201910019365) count as removable
        return !Regex.IsMatch(id, @"^\d{8,}(\.\w+)?$");
    }

    /// <summary>Removes one app. Returns the TV's answer; success is confirmed by listing apps again afterwards.</summary>
    public static async Task<SdbResult> RemoveAsync(string serial, TvApp app)
    {
        if (app.Kind == AppKind.Core) throw new InvalidOperationException($"{app.Title} is part of Samsung's system and isn't removed.");
        return await Sdb.OnTvAsync(serial, "shell", "0", "vd_appuninstall", app.Id);
    }

    // ───────────── Local record of what was removed (on this PC only) ─────────────

    private static string RemovedFile => Path.Combine(AppInfo.DataFolder, "removed-apps.json");

    public static List<RemovedApp> Removed()
    {
        try
        {
            return File.Exists(RemovedFile)
                ? JsonSerializer.Deserialize<List<RemovedApp>>(File.ReadAllText(RemovedFile)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void RecordRemoved(IEnumerable<RemovedApp> apps)
    {
        var all = Removed();
        all.AddRange(apps);
        try
        {
            File.WriteAllText(RemovedFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Write($"Couldn't save the removed-apps list: {ex.Message}"); }
    }
}
