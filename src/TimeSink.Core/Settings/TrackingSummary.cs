using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Core.Settings;

/// <summary>About: "Tracking since" date and tracked-app count, read best effort (read-only, no migration).</summary>
public sealed record TrackingSummary(DateTimeOffset? FirstAddedUtc, int AppCount)
{
    public static TrackingSummary Read(string dbPath)
    {
        if (!File.Exists(dbPath)) return new(null, 0);
        try
        {
            using var c = Db.CreateConnection(dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT MIN(added_utc), COUNT(*) FROM apps WHERE state = 'tracked'";
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.IsDBNull(0)) return new(null, 0);
            return new(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), (int)r.GetInt64(1));
        }
        catch (SqliteException) { return new(null, 0); }
    }

    public string Describe()
        => FirstAddedUtc is null
            ? "Nothing tracked yet"
            : string.Create(CultureInfo.InvariantCulture,
                $"Since {FirstAddedUtc.Value.ToLocalTime():d MMM yyyy} · {AppCount} app{(AppCount == 1 ? "" : "s")} tracked");
}
