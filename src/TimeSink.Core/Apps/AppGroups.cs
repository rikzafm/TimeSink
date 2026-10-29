using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Apps;

public enum AppState { Tracked, Retired }

/// <summary>The <c>apps.state</c> column's text, kept at the SQL edge.</summary>
internal static class AppStates
{
    public static AppState Parse(string state) => state == "retired" ? AppState.Retired : AppState.Tracked;

    public static string Sql(AppState state) => state == AppState.Retired ? "retired" : "tracked";
}

/// <summary>An app as the readers of <c>apps</c> see it. <c>Group</c> is the Main app id its time counts under (a Main app is its own group).</summary>
internal sealed record GroupedApp(long Id, long Group, string Name, long AddedUtc, bool Background = false, AppKind Kind = AppKind.Other,
    AppState State = AppState.Tracked, long RetiredUtc = 0, string ExePath = "")
{
    public bool Retired => State == AppState.Retired;
}

/// <summary>
/// The apps and the Main app each one counts under, read once. Merged apps share their Main app's group, except a tracked
/// Merged app whose Main app is retired or gone: it names its own group (decision 56). Retired apps are only present
/// when the caller asked for them.
/// </summary>
internal sealed class AppGroups
{
    private readonly Dictionary<long, GroupedApp> _apps;

    private AppGroups(Dictionary<long, GroupedApp> apps) => _apps = apps;

    public int Count => _apps.Count;

    public IEnumerable<GroupedApp> Values => _apps.Values;

    public GroupedApp this[long id] => _apps[id];

    public bool TryGetValue(long id, out GroupedApp app) => _apps.TryGetValue(id, out app!);

    public bool ContainsKey(long id) => _apps.ContainsKey(id);

    /// <summary>The group an app counts under; an unknown id is its own group.</summary>
    public long GroupOf(long id) => _apps.TryGetValue(id, out var a) ? a.Group : id;

    /// <summary>The Main app of <paramref name="group"/> (when present) followed by its Merged apps, by id.</summary>
    public IReadOnlyList<GroupedApp> Members(long group) =>
        _apps.Values.Where(a => a.Group == group).OrderBy(a => a.Id != group).ThenBy(a => a.Id).ToList();

    /// <summary>Builds the groups from rows as read, applying the rule for a Merged app whose Main app is retired or gone.</summary>
    public static AppGroups From(IEnumerable<GroupedApp> rows)
    {
        var d = new Dictionary<long, GroupedApp>();
        foreach (var a in rows) d[a.Id] = a;
        foreach (var a in d.Values.ToList())
            if (a.Group != a.Id && (!d.TryGetValue(a.Group, out var main) || (main.Retired && !a.Retired))) d[a.Id] = a with { Group = a.Id };
        return new AppGroups(d);
    }

    /// <param name="gamesOnly">Keeps only the apps whose group is a Game or Gacha game (the group's Main app decides; the Games filter).</param>
    public static AppGroups Load(SqliteConnection c, bool includeRetired = false, bool gamesOnly = false)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, COALESCE(canonical_id, id), display_name, added_utc, is_background, exe_path, kind, state, retired_utc FROM apps"
            + (includeRetired ? "" : " WHERE state = 'tracked'");
        using var r = cmd.ExecuteReader();
        var rows = new List<GroupedApp>();
        while (r.Read()) rows.Add(new GroupedApp(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4) != 0,
            AppKinds.Resolve(r.IsDBNull(6) ? null : r.GetInt64(6), r.GetString(5), r.GetString(2)),
            AppStates.Parse(r.GetString(7)), r.IsDBNull(8) ? 0 : r.GetInt64(8), r.GetString(5)));
        var groups = From(rows);
        return gamesOnly ? groups.OnlyGames() : groups;
    }

    /// <summary>The groups whose Main app is a Game or Gacha game; a Merged app follows its Main app.</summary>
    internal AppGroups OnlyGames() =>
        new(_apps.Where(p => (_apps.TryGetValue(p.Value.Group, out var main) ? main : p.Value).Kind != AppKind.Other).ToDictionary(p => p.Key, p => p.Value));
}
