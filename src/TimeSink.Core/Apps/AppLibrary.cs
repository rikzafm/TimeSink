using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Apps;

public sealed record NewApp(string ExePath, string DisplayName, string? PackageFamily);

public sealed record LibraryApp(long Id, string ExePath, string? PackageFamily, string DisplayName, AppState State, long? RetiredUtc, bool Background = false, AppKind Kind = AppKind.Other);

public sealed record RetiredApp(long Id, string ExePath, string DisplayName, long RetiredUtc);

public sealed record MergedApp(long Id, string ExePath, string DisplayName, long Seconds);

/// <summary>A tracked app as the Tracked tab shows it: merged aliases listed under it, <c>Seconds</c> including them.</summary>
public sealed record TrackedEntry(long Id, string ExePath, string DisplayName, long AddedUtc, long Seconds, IReadOnlyList<MergedApp> Merged, bool Background = false, AppKind Kind = AppKind.Other);

public sealed record MergedAlias(string ExePath, string? PackageFamily, long CanonicalId);

public enum AddOutcome { Added, AlreadyTracked, Restored }

public sealed record AddResult(long AppId, AddOutcome Outcome);

/// <summary>
/// UI-side writes to <c>apps</c>, <c>icons</c> and <c>ignored_suggestions</c> (implementation.md section 5: the UI may
/// write these). Opens its own short connection and never migrates; the Agent keeps the schema. The Agent picks new
/// apps up through its per-tick reload of tracked apps (decision 39).
/// </summary>
public sealed class AppLibrary : IDisposable
{
    private readonly SqliteConnection _c;

    private AppLibrary(SqliteConnection c) => _c = c;

    public static AppLibrary Open(string dbPath)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        p.ExecuteNonQuery();
        return new AppLibrary(c);
    }

    /// <summary>Starts tracking an app now. An already tracked app is left alone; a retired one is restored (counting from now).</summary>
    public AddResult AddTracked(NewApp app, long nowUtc, byte[]? iconPng)
    {
        using var tx = _c.BeginTransaction();
        AddResult result;

        // a merged alias is the app it counts under: tracked with it, restored with it
        using (var find = Cmd("SELECT COALESCE(canonical_id, id), COALESCE((SELECT k.state FROM apps k WHERE k.id = apps.canonical_id), state) FROM apps WHERE exe_path = $p", tx))
        {
            find.Parameters.AddWithValue("$p", app.ExePath);
            using var r = find.ExecuteReader();
            if (r.Read())
            {
                var id = r.GetInt64(0);
                var state = AppStates.Parse(r.GetString(1));
                r.Close();
                if (state == AppState.Tracked)
                {
                    tx.Commit();
                    return new AddResult(id, AddOutcome.AlreadyTracked);
                }
                using var up = Cmd("UPDATE apps SET state='tracked', retired_utc=NULL, added_utc=$t, package_family=COALESCE($f, package_family) WHERE id=$id OR canonical_id=$id", tx);
                up.Parameters.AddWithValue("$t", nowUtc);
                up.Parameters.AddWithValue("$f", (object?)app.PackageFamily ?? DBNull.Value);
                up.Parameters.AddWithValue("$id", id);
                up.ExecuteNonQuery();
                result = new AddResult(id, AddOutcome.Restored);
            }
            else
            {
                r.Close();
                using var ins = Cmd("INSERT INTO apps (exe_path, package_family, display_name, added_utc) VALUES ($p,$f,$n,$t) RETURNING id", tx);
                ins.Parameters.AddWithValue("$p", app.ExePath);
                ins.Parameters.AddWithValue("$f", (object?)app.PackageFamily ?? DBNull.Value);
                ins.Parameters.AddWithValue("$n", app.DisplayName);
                ins.Parameters.AddWithValue("$t", nowUtc);
                result = new AddResult((long)ins.ExecuteScalar()!, AddOutcome.Added);
            }
        }

        if (iconPng is { Length: > 0 }) PutIcon(result.AppId, iconPng, tx);
        tx.Commit();
        return result;
    }

    /// <summary>
    /// Stops tracking an app: history stays, totals and tracking stop. The Agent's per-tick reload closes any open
    /// session as 'removed' (engine behaviour, decision 39).
    /// </summary>
    public void Retire(long appId, long nowUtc) => SetState(appId, AppState.Retired, nowUtc);

    /// <summary>Tracks a retired app again, counting from <paramref name="nowUtc"/>.</summary>
    public void Restore(long appId, long nowUtc) => SetState(appId, AppState.Tracked, nowUtc);

    // an app's merged aliases follow it, so stopping or restoring a merged app never leaves orphans behind
    private void SetState(long appId, AppState state, long nowUtc)
    {
        using var cmd = Cmd(state == AppState.Retired
            ? "UPDATE apps SET state='retired', retired_utc=$t WHERE id=$id OR canonical_id=$id"
            : "UPDATE apps SET state='tracked', retired_utc=NULL, added_utc=$t WHERE id=$id OR canonical_id=$id");
        cmd.Parameters.AddWithValue("$t", nowUtc);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Tracked apps with their merged aliases and lifetime seconds (aliases counted under the app that keeps the name), by name.</summary>
    public IReadOnlyList<TrackedEntry> Tracked()
    {
        var seconds = new Dictionary<long, long>();
        using (var cmd = Cmd("SELECT app_id, SUM(seconds) FROM daily_usage GROUP BY app_id"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) seconds[r.GetInt64(0)] = r.GetInt64(1);

        var groups = AppGroups.Load(_c);
        return groups.Values.Where(a => a.Group == a.Id)
            .Select(a =>
            {
                var merged = groups.Members(a.Id).Where(m => m.Id != a.Id)
                    .Select(m => new MergedApp(m.Id, m.ExePath, m.Name, seconds.GetValueOrDefault(m.Id)))
                    .OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
                return new TrackedEntry(a.Id, a.ExePath, a.Name, a.AddedUtc, seconds.GetValueOrDefault(a.Id) + merged.Sum(m => m.Seconds), merged, a.Background, a.Kind);
            })
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Marks an app as a Background app (it runs all day, like Steam): still tracked and counted, but ranked after the
    /// others. A merged group is flagged on its canonical app, so an alias id flags the app it is merged into.
    /// </summary>
    public void SetBackground(long appId, bool background)
    {
        using var cmd = Cmd("UPDATE apps SET is_background=$b WHERE id = COALESCE((SELECT canonical_id FROM apps WHERE id=$id), $id)");
        cmd.Parameters.AddWithValue("$b", background ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Sets what an app is for the Wrapped game pages. Always stored, so a choice of "Other" sticks over detection. A
    /// merged group is set on its canonical app, like <see cref="SetBackground"/>.
    /// </summary>
    public void SetKind(long appId, AppKind kind)
    {
        using var cmd = Cmd("UPDATE apps SET kind=$k WHERE id = COALESCE((SELECT canonical_id FROM apps WHERE id=$id), $id)");
        cmd.Parameters.AddWithValue("$k", (int)kind);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Changes the name shown everywhere; history and totals are untouched. A blank name is ignored.</summary>
    public bool Rename(long appId, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        using var cmd = Cmd("UPDATE apps SET display_name=$n WHERE id=$id");
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$id", appId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Counts <paramref name="sourceId"/> (and anything already merged into it) under <paramref name="targetId"/>.
    /// Both must be tracked and the target must not itself be merged into something else.
    /// </summary>
    public bool Merge(long sourceId, long targetId)
    {
        if (sourceId == targetId) return false;
        using var tx = _c.BeginTransaction();
        using (var check = Cmd("SELECT COUNT(*) FROM apps WHERE state='tracked' AND canonical_id IS NULL AND id IN ($s,$t)", tx))
        {
            check.Parameters.AddWithValue("$s", sourceId);
            check.Parameters.AddWithValue("$t", targetId);
            if ((long)check.ExecuteScalar()! != 2) return false;
        }
        using (var up = Cmd("UPDATE apps SET canonical_id=$t WHERE id=$s OR canonical_id=$s", tx))
        {
            up.Parameters.AddWithValue("$s", sourceId);
            up.Parameters.AddWithValue("$t", targetId);
            up.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    /// <summary>
    /// Makes a merged app the main app of its group: the old main (and every other alias) is merged under it. Type, Background
    /// flag, goals and the earliest added date move to the new main; history stays on the rows that recorded it.
    /// </summary>
    public bool MakeMain(long aliasId)
    {
        using var tx = _c.BeginTransaction();
        long oldMain;
        using (var find = Cmd("SELECT canonical_id FROM apps WHERE id=$id AND state='tracked'", tx))
        {
            find.Parameters.AddWithValue("$id", aliasId);
            if (find.ExecuteScalar() is not long main) return false;
            oldMain = main;
        }
        using (var move = Cmd(@"UPDATE apps SET
                is_background = (SELECT is_background FROM apps WHERE id=$old),
                kind = (SELECT kind FROM apps WHERE id=$old),
                added_utc = MIN(added_utc, (SELECT added_utc FROM apps WHERE id=$old))
            WHERE id=$new", tx))
        {
            move.Parameters.AddWithValue("$old", oldMain);
            move.Parameters.AddWithValue("$new", aliasId);
            move.ExecuteNonQuery();
        }
        using (var re = Cmd("UPDATE apps SET canonical_id=$new WHERE id=$old OR canonical_id=$old", tx))
        {
            re.Parameters.AddWithValue("$old", oldMain);
            re.Parameters.AddWithValue("$new", aliasId);
            re.ExecuteNonQuery();
        }
        using (var top = Cmd("UPDATE apps SET canonical_id=NULL WHERE id=$new", tx))
        {
            top.Parameters.AddWithValue("$new", aliasId);
            top.ExecuteNonQuery();
        }
        using (var goals = Cmd("UPDATE OR IGNORE goals SET app_id=$new WHERE app_id=$old", tx))
        {
            goals.Parameters.AddWithValue("$old", oldMain);
            goals.Parameters.AddWithValue("$new", aliasId);
            goals.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    /// <summary>Makes a merged app a separate tracked app again.</summary>
    public void Unmerge(long aliasId)
    {
        using var cmd = Cmd("UPDATE apps SET canonical_id=NULL WHERE id=$id");
        cmd.Parameters.AddWithValue("$id", aliasId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes the recorded time of an app and of everything merged into it. A session that is still open is kept, so a
    /// running app keeps counting from now.
    /// </summary>
    public void ClearHistory(long appId)
    {
        using var tx = _c.BeginTransaction();
        const string ids = "SELECT id FROM apps WHERE id=$id OR canonical_id=$id";
        using (var s = Cmd($"DELETE FROM sessions WHERE end_utc IS NOT NULL AND app_id IN ({ids})", tx))
        {
            s.Parameters.AddWithValue("$id", appId);
            s.ExecuteNonQuery();
        }
        using (var d = Cmd($"DELETE FROM daily_usage WHERE app_id IN ({ids})", tx))
        {
            d.Parameters.AddWithValue("$id", appId);
            d.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Every known app (tracked and retired); merged aliases are not listed.</summary>
    public IReadOnlyList<LibraryApp> Library()
    {
        var list = new List<LibraryApp>();
        using var cmd = Cmd("SELECT id, exe_path, package_family, display_name, state, retired_utc, is_background, kind FROM apps WHERE canonical_id IS NULL ORDER BY display_name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LibraryApp(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), AppStates.Parse(r.GetString(4)),
                r.IsDBNull(5) ? null : r.GetInt64(5), r.GetInt64(6) != 0,
                AppKinds.Resolve(r.IsDBNull(7) ? null : r.GetInt64(7), r.GetString(1), r.GetString(3))));
        return list;
    }

    /// <summary>Exe path and package family of every merged alias with the app it counts under, so a merged app is recognised as tracked (or stopped) with its keeper.</summary>
    public IReadOnlyList<MergedAlias> MergedAliases()
    {
        var list = new List<MergedAlias>();
        using var cmd = Cmd("SELECT exe_path, package_family, canonical_id FROM apps WHERE canonical_id IS NOT NULL");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MergedAlias(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2)));
        return list;
    }

    /// <summary>A retired app with less lifetime time than this is not worth listing (it is hidden, its data kept).</summary>
    public const long MinRetiredSeconds = 3600;

    /// <summary>Ids of retired apps under <see cref="MinRetiredSeconds"/> of lifetime time (merged aliases counted with their canonical app).</summary>
    public HashSet<long> TrivialRetired()
    {
        var ids = new HashSet<long>();
        using var cmd = Cmd(@"SELECT a.id FROM apps a WHERE a.state = 'retired' AND a.canonical_id IS NULL AND
            COALESCE((SELECT SUM(d.seconds) FROM daily_usage d JOIN apps m ON m.id = d.app_id WHERE m.id = a.id OR m.canonical_id = a.id), 0) < $min");
        cmd.Parameters.AddWithValue("$min", MinRetiredSeconds);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    /// <summary>Retired apps, most recently retired first.</summary>
    public IReadOnlyList<RetiredApp> Retired() =>
        Library().Where(a => a.State == AppState.Retired)
            .OrderByDescending(a => a.RetiredUtc)
            .Select(a => new RetiredApp(a.Id, a.ExePath, a.DisplayName, a.RetiredUtc ?? 0)).ToList();

    /// <summary>Adds a path to the ignore list ("Not this one"); repeating it is harmless.</summary>
    public void Ignore(string exePath, long nowUtc)
    {
        using var cmd = Cmd("INSERT OR IGNORE INTO ignored_suggestions (exe_path, utc) VALUES ($p, $t)");
        cmd.Parameters.AddWithValue("$p", exePath);
        cmd.Parameters.AddWithValue("$t", nowUtc);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Paths and package families the Running tab must not offer: tracked apps plus the ignore list.</summary>
    public ExcludedApps ExcludedFromRunning()
    {
        var paths = new List<string>();
        var families = new List<string>();
        using (var cmd = Cmd("SELECT exe_path, package_family FROM apps WHERE state = 'tracked'"))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                paths.Add(r.GetString(0));
                if (!r.IsDBNull(1)) families.Add(r.GetString(1));
            }
        using (var cmd = Cmd("SELECT exe_path FROM ignored_suggestions"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) paths.Add(r.GetString(0));
        return new ExcludedApps(paths, families);
    }

    public void PutIcon(long appId, byte[] png) => PutIcon(appId, png, null);

    /// <summary>Stores an icon the user picked: the hash is marked, so tile-size upgrades leave it alone (decision 152).</summary>
    public void PutChosenIcon(long appId, byte[] png) => PutIcon(appId, png, null, chosen: true);

    /// <summary>The apps whose icon the user picked.</summary>
    public IReadOnlySet<long> ChosenIcons()
    {
        var ids = new HashSet<long>();
        using var cmd = Cmd("SELECT app_id FROM icons WHERE hash LIKE 'chosen:%'");
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    private void PutIcon(long appId, byte[] png, SqliteTransaction? tx, bool chosen = false)
    {
        using var cmd = Cmd("INSERT INTO icons (app_id, png, hash) VALUES ($a,$b,$h) ON CONFLICT(app_id) DO UPDATE SET png=excluded.png, hash=excluded.hash", tx);
        cmd.Parameters.AddWithValue("$a", appId);
        cmd.Parameters.AddWithValue("$b", png);
        cmd.Parameters.AddWithValue("$h", (chosen ? "chosen:" : "") + Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    public byte[]? GetIcon(long appId)
    {
        using var cmd = Cmd("SELECT png FROM icons WHERE app_id = $a");
        cmd.Parameters.AddWithValue("$a", appId);
        return cmd.ExecuteScalar() as byte[];
    }

    /// <summary>The stored icons of the given apps in one query; an app with no stored icon is left out.</summary>
    public IReadOnlyDictionary<long, byte[]> GetIcons(IEnumerable<long> appIds)
    {
        var icons = new Dictionary<long, byte[]>();
        var ids = appIds.Distinct().ToList();
        if (ids.Count == 0) return icons;
        using var cmd = Cmd("SELECT app_id, png FROM icons WHERE app_id IN (SELECT value FROM json_each($ids))");
        cmd.Parameters.AddWithValue("$ids", "[" + string.Join(",", ids) + "]");
        using var r = cmd.ExecuteReader();
        while (r.Read()) icons[r.GetInt64(0)] = (byte[])r[1];
        return icons;
    }

    public void Dispose() => _c.Dispose();

    private SqliteCommand Cmd(string sql, SqliteTransaction? tx = null)
    {
        var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }
}

/// <summary>Fallback icon: a coloured tile with the app's first letter (implementation.md 8.5).</summary>
public static class LetterTile
{
    public static string Letter(string name)
    {
        foreach (var ch in name)
            if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    public static int PaletteIndex(long key, int paletteSize) => TimeSink.Core.Theming.AppPalette.Slot(key, paletteSize);

    /// <summary>A stable key for an app that has no id yet (FNV-1a over the lower-cased path).</summary>
    public static long KeyFor(string exePath)
    {
        ulong h = 14695981039346656037;
        foreach (var ch in exePath.ToLowerInvariant()) { h ^= ch; h *= 1099511628211; }
        return (long)(h & 0x7FFFFFFFFFFFFFFF);
    }
}
