namespace TimeSink.Core.Backup;

/// <summary>The "Last backup: N days ago" line under Back up now.</summary>
public static class LastBackupText
{
    public static string Describe(long lastUtc, DateTimeOffset now)
    {
        if (lastUtc <= 0) return "No backup yet";
        var age = now.ToUnixTimeSeconds() - lastUtc;
        if (age < 3600) return "Last backup: just now";
        if (age < 86400) return "Last backup: today";
        var days = age / 86400;
        return days == 1 ? "Last backup: 1 day ago" : $"Last backup: {days} days ago";
    }
}
