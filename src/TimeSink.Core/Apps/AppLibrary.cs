using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Apps;

public sealed record NewApp(string ExePath, string DisplayName, string? PackageFamily);

public sealed record LibraryApp(long Id, string ExePath, string? PackageFamily, string DisplayName, string State, long? RetiredUtc);

public sealed record RetiredApp(long Id, string ExePath, string DisplayName, long RetiredUtc);

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

        using (var find = Cmd("SELECT id, state FROM apps WHERE exe_path = $p", tx))
        {
            find.Parameters.AddWithValue("$p", app.ExePath);
            using var r = find.ExecuteReader();
            if (r.Read())
            {
                var id = r.GetInt64(0);
                var state = r.GetString(1);
                r.Close();
                if (state == "tracked")
                {
                    tx.Commit();
                    return new AddResult(id, AddOutcome.AlreadyTracked);
                }
                using var up = Cmd("UPDATE apps SET state='tracked', retired_utc=NULL, added_utc=$t, package_family=COALESCE($f, package_family) WHERE id=$id", tx);
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
    public void Retire(long appId, long nowUtc) => SetState(appId, "retired", nowUtc);

    /// <summary>Tracks a retired app again, counting from <paramref name="nowUtc"/>.</summary>
    public void Restore(long appId, long nowUtc) => SetState(appId, "tracked", nowUtc);

    private void SetState(long appId, string state, long nowUtc)
    {
        using var cmd = Cmd(state == "retired"
            ? "UPDATE apps SET state='retired', retired_utc=$t WHERE id=$id"
            : "UPDATE apps SET state='tracked', retired_utc=NULL, added_utc=$t WHERE id=$id");
        cmd.Parameters.AddWithValue("$t", nowUtc);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Every known app (tracked and retired); merged aliases are not listed.</summary>
    public IReadOnlyList<LibraryApp> Library()
    {
        var list = new List<LibraryApp>();
        using var cmd = Cmd("SELECT id, exe_path, package_family, display_name, state, retired_utc FROM apps WHERE canonical_id IS NULL ORDER BY display_name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LibraryApp(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetInt64(5)));
        return list;
    }

    /// <summary>Retired apps, most recently retired first.</summary>
    public IReadOnlyList<RetiredApp> Retired() =>
        Library().Where(a => a.State == "retired")
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

    private void PutIcon(long appId, byte[] png, SqliteTransaction? tx)
    {
        using var cmd = Cmd("INSERT INTO icons (app_id, png, hash) VALUES ($a,$b,$h) ON CONFLICT(app_id) DO UPDATE SET png=excluded.png, hash=excluded.hash", tx);
        cmd.Parameters.AddWithValue("$a", appId);
        cmd.Parameters.AddWithValue("$b", png);
        cmd.Parameters.AddWithValue("$h", Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    public byte[]? GetIcon(long appId)
    {
        using var cmd = Cmd("SELECT png FROM icons WHERE app_id = $a");
        cmd.Parameters.AddWithValue("$a", appId);
        return cmd.ExecuteScalar() as byte[];
    }

    public string? GetIconHash(long appId)
    {
        using var cmd = Cmd("SELECT hash FROM icons WHERE app_id = $a");
        cmd.Parameters.AddWithValue("$a", appId);
        return cmd.ExecuteScalar() as string;
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

    public static int PaletteIndex(long key, int paletteSize) => (int)(((key % paletteSize) + paletteSize) % paletteSize);

    /// <summary>A stable key for an app that has no id yet (FNV-1a over the lower-cased path).</summary>
    public static long KeyFor(string exePath)
    {
        ulong h = 14695981039346656037;
        foreach (var ch in exePath.ToLowerInvariant()) { h ^= ch; h *= 1099511628211; }
        return (long)(h & 0x7FFFFFFFFFFFFFFF);
    }
}
