namespace TimeSink.Core.Usage;

public enum LastOpenedKind { Never, JustNow, MinutesAgo, HoursAgo, Yesterday, Date }

/// <summary>
/// When an app was last opened, without wording: <see cref="Amount"/> is the minutes or hours for those kinds, and
/// <see cref="Date"/> with <see cref="InCurrentYear"/> is the Local day for <see cref="LastOpenedKind.Date"/>.
/// </summary>
public readonly record struct LastOpened(LastOpenedKind Kind, int Amount = 0, DateOnly Date = default, bool InCurrentYear = true)
{
    /// <summary>
    /// Never for no time; under a minute is JustNow and under an hour is minutes. Beyond that the Local day decides, not the
    /// elapsed time: earlier today is hours, the day before is Yesterday, anything older is a date.
    /// </summary>
    public static LastOpened Of(long lastUtc, long nowUtc, TimeZoneInfo zone)
    {
        if (lastUtc <= 0) return new LastOpened(LastOpenedKind.Never);
        var age = nowUtc - lastUtc;
        if (age < 60) return new LastOpened(LastOpenedKind.JustNow);
        if (age < 3600) return new LastOpened(LastOpenedKind.MinutesAgo, (int)(age / 60));
        var bucket = UsageText.DayOf(lastUtc, nowUtc, zone);
        return bucket.Kind switch
        {
            DayKind.Today => new LastOpened(LastOpenedKind.HoursAgo, (int)(age / 3600)),
            DayKind.Yesterday => new LastOpened(LastOpenedKind.Yesterday),
            _ => new LastOpened(LastOpenedKind.Date, 0, bucket.Date, bucket.InCurrentYear),
        };
    }
}
