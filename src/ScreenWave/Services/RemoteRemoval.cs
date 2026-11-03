namespace ScreenWave.Services;

/// <summary>
/// Removes apps through Samsung's own Apps › Settings › Delete screen, by pressing remote buttons –
/// for TVs (Tizen 6 and newer) that refuse removal over Developer Mode.
///
/// It's taught once in three parts, which are saved like any other recording:
///   remove-open    – from a normal picture to Apps › Settings with the first app highlighted
///   remove-delete  – from a highlighted app to it being deleted (OK › Delete › confirm)
///   remove-layout  – notes: "cols=N" (apps per row) and "order=app_index" once confirmed
/// Moving to the right app in between is worked out from the app's place in the TV's own list.
/// Every deletion is checked against the TV's app list, and it stops if anything unexpected happens.
/// </summary>
public static class RemoteRemoval
{
    public const string OpenKey = "remove-open";
    public const string DeleteKey = "remove-delete";
    public const string LayoutKey = "remove-layout";

    /// <summary>Order: "app_index" (every removable app) or "store_index" (only store-installed apps), as confirmed when taught.</summary>
    public sealed record Layout(int Columns, string Order);

    public static readonly string[] Orders = ["app_index", "store_index"];

    /// <summary>Only used once the grid order has been confirmed by teaching – otherwise it could delete the wrong app.</summary>
    public static bool IsTaught =>
        Routines.Get(Tv.Model, Tv.PlatformVersion, OpenKey) != null &&
        Routines.Get(Tv.Model, Tv.PlatformVersion, DeleteKey) != null &&
        GetLayout() is { } l && Orders.Contains(l.Order);

    public static Layout? GetLayout()
    {
        var notes = Routines.Get(Tv.Model, Tv.PlatformVersion, LayoutKey);
        if (notes == null) return null;
        var d = notes.Select(n => n.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        return d.TryGetValue("cols", out var c) && int.TryParse(c, out var cols) && cols > 0
            ? new Layout(cols, d.TryGetValue("order", out var o) ? o : "unconfirmed")
            : null;
    }

    /// <summary>The apps in the order Samsung's Apps settings grid shows them, following the TV's own app_index.</summary>
    public static List<TvApp> GridOrder(IEnumerable<TvApp> apps, string order) =>
        apps.Where(a => a.Kind == AppKind.Removable)
            .Where(a => order != "store_index" || (a.Fields.TryGetValue("installed_source_type", out var t) && t == "1"))
            .OrderBy(a => a.Fields.TryGetValue("app_index", out var i) && int.TryParse(i, out var n) ? n : int.MaxValue)
            .ToList();

    /// <summary>Arrow presses from the first app to the app at a position in the grid.</summary>
    public static List<string> MovesTo(int position, int columns)
    {
        var moves = new List<string>();
        for (var r = 0; r < position / columns; r++) moves.Add("KEY_DOWN");
        for (var c = 0; c < position % columns; c++) moves.Add("KEY_RIGHT");
        return moves;
    }

    /// <summary>Where a set of arrow presses ends up (row × columns + column), or null if it left the grid.</summary>
    public static int? PositionOf(IEnumerable<string> moves, int columns)
    {
        int row = 0, col = 0;
        foreach (var k in moves)
        {
            switch (k)
            {
                case "KEY_DOWN": row++; break;
                case "KEY_UP": row--; break;
                case "KEY_RIGHT": col++; break;
                case "KEY_LEFT": col--; break;
            }
        }
        return row < 0 || col < 0 || col >= columns ? null : row * columns + col;
    }

    public sealed record Result(bool Removed, string Message, string? WrongAppRemoved = null);

    /// <summary>Removes one app with the remote and checks the TV's app list afterwards.</summary>
    public static async Task<Result> RemoveAsync(string serial, TvApp target, Action<string> status, CancellationToken ct)
    {
        var layout = GetLayout() is { } l && Orders.Contains(l.Order) ? l
            : throw new InvalidOperationException("Removing apps with the remote hasn't been taught for this TV yet.");
        var open = Routines.Get(Tv.Model, Tv.PlatformVersion, OpenKey)!;
        var delete = Routines.Get(Tv.Model, Tv.PlatformVersion, DeleteKey)!;

        // Fresh list: positions change every time an app goes
        var (before, _) = await TvApps.ListAsync(serial);
        var grid = GridOrder(before, layout.Order);
        var position = grid.FindIndex(a => a.Id == target.Id);
        if (position < 0) return new Result(true, $"{target.Title} is already gone.");

        status($"Opening the TV's Apps settings…");
        await Routines.PlayAsync(open, null, ct);

        status($"Moving to {target.Title}…");
        foreach (var k in MovesTo(position, layout.Columns))
        {
            ct.ThrowIfCancellationRequested();
            await TvRemote.PressAsync(k);
            await Task.Delay(Routines.Gap, ct);
        }

        status($"Deleting {target.Title}…");
        foreach (var k in delete)
        {
            ct.ThrowIfCancellationRequested();
            await TvRemote.PressAsync(k);
            await Task.Delay(Routines.Gap, ct);
        }

        // Give the TV a moment, then check what actually went
        status($"Checking {target.Title} has gone…");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var (after, _) = await TvApps.ListAsync(serial);
            var afterIds = after.Select(a => a.Id).ToHashSet();
            var gone = before.Where(a => !afterIds.Contains(a.Id)).ToList();

            var wrong = gone.FirstOrDefault(a => a.Id != target.Id);
            if (wrong != null)
                return new Result(false, $"Stopped: {wrong.Title} was removed instead of {target.Title}. You can reinstall it from the TV's Apps store.", wrong.Title);
            if (gone.Any(a => a.Id == target.Id))
                return new Result(true, $"{target.Title} removed.");
        }

        await TvRemote.PressAsync("KEY_EXIT");
        return new Result(false, $"{target.Title} wasn't removed – the TV may have shown something unexpected. Check the TV screen.");
    }

    /// <summary>
    /// After teaching: which app disappeared, and does its place in the grid match the presses that reached it?
    /// Returns the layout notes to save, and a message for the person teaching.
    /// </summary>
    public static (List<string> Notes, string Message, bool Confirmed) CheckTeaching(
        List<TvApp> before, List<TvApp> after, List<string> moves, int columns)
    {
        var afterIds = after.Select(a => a.Id).ToHashSet();
        var gone = before.Where(a => !afterIds.Contains(a.Id)).ToList();
        if (gone.Count == 0)
            return (new List<string>(), "No app was removed, so nothing was learnt. Check the TV and try again.", false);
        if (gone.Count > 1)
            return (new List<string>(), $"More than one app went ({string.Join(", ", gone.Select(g => g.Title))}), so nothing was learnt.", false);

        var removed = gone[0];
        var reached = PositionOf(moves, columns);
        foreach (var order in Orders)
        {
            var pos = GridOrder(before, order).FindIndex(a => a.Id == removed.Id);
            if (reached != null && pos == reached)
                return (new List<string> { $"cols={columns}", $"order={order}" },
                    $"Learnt. You removed {removed.Title}, and its place in the grid matched – ScreenWave can now remove apps by itself on these TVs.", true);
        }

        var appIndexPos = GridOrder(before, "app_index").FindIndex(a => a.Id == removed.Id);
        return (new List<string> { $"cols={columns}", "order=unconfirmed", $"reached={reached?.ToString() ?? "outside"}", $"actual={appIndexPos}" },
            $"You removed {removed.Title}, but its place in the grid didn't match the TV's list (reached {reached?.ToString() ?? "outside the grid"}, expected {appIndexPos}). " +
            "Check the apps-per-row number and try again with another app. Automatic removal stays off until it matches, so it can't delete the wrong app.", false);
    }
}
