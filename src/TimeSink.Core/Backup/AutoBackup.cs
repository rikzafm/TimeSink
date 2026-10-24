using System.Globalization;
using TimeSink.Core.Data;

namespace TimeSink.Core.Backup;

/// <summary>Retention for automatic backups: the 7 newest plus the newest of each of the last 12 months.</summary>
public static class BackupRetention
{
    public const int KeepNewest = 7;
    public const int KeepMonths = 12;

    /// <summary>
    /// Returns the file names to delete. Only <c>daily-YYYYMMDD</c> and <c>weekly-YYYYMMDD</c> files are considered;
    /// manual, pre-restore and unrelated files are never returned.
    /// </summary>
    public static IEnumerable<string> FilesToDelete(IEnumerable<string> fileNames, DateTimeOffset now)
    {
        var auto = fileNames.Select(n => (Name: n, Date: ParseDate(n))).Where(x => x.Date is not null)
            .Select(x => (x.Name, Date: x.Date!.Value)).OrderByDescending(x => x.Date).ThenByDescending(x => x.Name, StringComparer.Ordinal).ToList();

        var keep = auto.Take(KeepNewest).Select(x => x.Name).ToHashSet();
        var thisMonth = new DateTime(now.Year, now.Month, 1);
        var oldest = thisMonth.AddMonths(-(KeepMonths - 1));
        foreach (var month in auto.Where(x => x.Date >= oldest).GroupBy(x => (x.Date.Year, x.Date.Month)))
            keep.Add(month.First().Name); // list is newest first
        return auto.Where(x => !keep.Contains(x.Name)).Select(x => x.Name);
    }

    private static DateTime? ParseDate(string name)
    {
        foreach (var prefix in new[] { "daily-", "weekly-" })
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(BackupService.Extension, StringComparison.Ordinal)) continue;
            var stamp = name.Substring(prefix.Length, name.Length - prefix.Length - BackupService.Extension.Length);
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        }
        return null;
    }
}

/// <summary>The scheduled backup (Agent calls this on start and on a timer) and the manual "Back up now".</summary>
public static class AutoBackup
{
    /// <summary>
    /// If <c>backup.mode</c> is Daily/Weekly and the interval has elapsed since <c>backup.lastUtc</c>, writes
    /// <c>daily-YYYYMMDD.timesink</c> / <c>weekly-YYYYMMDD.timesink</c>, prunes by retention and records <c>backup.lastUtc</c>.
    /// Returns the new file, or null when nothing was due. The interval is shortened by an hour so a drifting start time cannot skip a period.
    /// </summary>
    public static string? RunIfDue(string dbPath, DateTimeOffset now, string defaultFolder, string appVersion = "0.0.0")
    {
        var mode = SettingsStore.Get(dbPath, "backup.mode") ?? "Daily";
        var interval = mode switch { "Daily" => TimeSpan.FromHours(23), "Weekly" => TimeSpan.FromDays(7) - TimeSpan.FromHours(1), _ => (TimeSpan?)null };
        if (interval is null) return null;

        var last = long.TryParse(SettingsStore.Get(dbPath, "backup.lastUtc"), out var l) ? l : 0;
        if (now.ToUnixTimeSeconds() - last < interval.Value.TotalSeconds) return null;

        var prefix = mode == "Weekly" ? "weekly" : "daily";
        return Run(dbPath, now, defaultFolder, $"{prefix}-{now.UtcDateTime:yyyyMMdd}{BackupService.Extension}", appVersion);
    }

    /// <summary>"Back up now": an unconditional backup named <c>manual-YYYYMMDD-HHmmss.timesink</c> (not subject to retention).</summary>
    public static string Now(string dbPath, DateTimeOffset now, string defaultFolder, string appVersion = "0.0.0") =>
        Run(dbPath, now, defaultFolder, $"manual-{now.UtcDateTime:yyyyMMdd-HHmmss}{BackupService.Extension}", appVersion);

    /// <summary>The folder from <c>backup.folder</c>, or <paramref name="defaultFolder"/>.</summary>
    public static string Folder(string dbPath, string defaultFolder)
    {
        var f = SettingsStore.Get(dbPath, "backup.folder");
        return string.IsNullOrWhiteSpace(f) ? defaultFolder : f;
    }

    private static string Run(string dbPath, DateTimeOffset now, string defaultFolder, string fileName, string appVersion)
    {
        var folder = Folder(dbPath, defaultFolder);
        var file = Path.Combine(folder, fileName);
        BackupService.Create(dbPath, file, now, appVersion);
        SettingsStore.Set(dbPath, "backup.lastUtc", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        foreach (var name in BackupRetention.FilesToDelete(Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)!), now))
        {
            try { File.Delete(Path.Combine(folder, name)); } catch (IOException) { }
        }
        return file;
    }
}
