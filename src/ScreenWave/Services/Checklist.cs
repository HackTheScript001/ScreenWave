using System.IO;
using System.Text.Json;

namespace ScreenWave.Services;

public sealed record Tip(string Key, string Title, string Why, string Where);

/// <summary>The TV settings that make the biggest difference to speed. Ticks are saved on this PC only.</summary>
public static class Checklist
{
    public static readonly Tip[] Tips =
    [
        new("autorun-hub", "Turn off Autorun Smart Hub",
            "Stops the home screen and its adverts loading every time the TV turns on.",
            "Settings › General › Smart Features › Autorun Smart Hub › Off"),
        new("autorun-app", "Turn off Autorun Last App",
            "Stops the last app you used starting again in the background at switch-on.",
            "Settings › General › Smart Features › Autorun Last App › Off"),
        new("tv-plus", "Stop Samsung TV Plus starting on power-up",
            "Some models open TV Plus channels at switch-on, which is slow and pulls adverts.",
            "Settings › Broadcasting › Samsung TV Plus (or Expert Settings) › turn off auto start"),
        new("acr", "Turn off Viewing Information Services",
            "This watches what's on screen to target adverts. It runs all the time and slows the TV.",
            "Settings › Support › Terms & Privacy › Viewing Information Services › Off"),
        new("ads", "Turn off Interest-Based Advertisement",
            "Fewer advert services running in the background.",
            "Settings › Support › Terms & Privacy › Interest-Based Advertisement › Off"),
        new("voice", "Turn off voice wake-up",
            "Stops the TV listening for \"Hi Bixby\" / \"Alexa\" all the time.",
            "Settings › General › Voice › Voice Wake-up › Off (or Bixby/Alexa settings)"),
        new("ambient", "Turn off Ambient Mode autostart (if your TV has it)",
            "Ambient Mode keeps running when the TV looks off.",
            "Settings › General › Ambient Mode › Auto Run › Off"),
        new("cache", "Clear app caches",
            "Frees memory and storage that slow everything down.",
            "Settings › Support › Device Care › Manage Storage › pick each big app › Clear Cache (2022+ TVs). Older TVs: Settings › Apps › pick app › Clear cache"),
        new("smarthub-reset", "Reset Smart Hub",
            "The single biggest speed-up for most TVs. Signs you out of apps; your channels are kept. PIN is 0000 unless you changed it.",
            "Settings › Support › Device Care › Self Diagnosis › Reset Smart Hub (older TVs: Support › Self Diagnosis › Reset Smart Hub)"),
        new("firmware", "Update the TV's software",
            "Samsung fixes slowness in updates. With no internet on the TV: download your model's update from samsung.com on a PC, unzip it to a USB stick, then on the TV choose Update Now with the stick plugged in.",
            "Settings › Support › Software Update › Update Now"),
        new("restart", "Restart the TV fully",
            "Clears everything out of memory.",
            "Hold the power button on the remote until the Samsung logo appears"),
    ];

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "checklist.json");

    public static HashSet<string> Done()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<string> done)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(done.ToHashSet())); }
        catch (Exception ex) { Log.Write($"Couldn't save the checklist: {ex.Message}"); }
    }
}
