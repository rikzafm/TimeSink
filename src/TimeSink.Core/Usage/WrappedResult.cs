namespace TimeSink.Core.Usage;

/// <summary>One app on the Wrapped podium (merged apps are one row under their canonical app).</summary>
public sealed record WrappedApp(long AppId, string Name, long Seconds);

/// <summary>
/// Everything the three Year > Wrapped cards show for the calendar year of <see cref="NowUtc"/> (year to date).
/// <see cref="TotalSeconds"/> is PC uptime; every other time is app-seconds (overlapping apps add). Nullable members are null when there is nothing to say.
/// </summary>
public sealed record WrappedResult(
    int Year,
    long NowUtc,
    DateOnly? TrackingStarted,
    long TotalSeconds,
    long AppSeconds,
    int DaysTracked,
    int DaysWithActivity,
    int AppsTracked,
    IReadOnlyList<WrappedApp> Podium,
    IReadOnlyList<WrappedApp> Others,
    int LongestStreakDays,
    DateOnly? StreakStart,
    DateOnly? StreakEnd,
    int? BusiestMonth,
    long BusiestMonthSeconds,
    double? BusiestMonthDeltaPercent,
    int? PeakHour,
    long PeakHourSeconds,
    int BeforeNoonPercent)
{
    /// <summary>"N days and M hours" the PC was on: whole days of the total.</summary>
    public int EquivalentDays => (int)(TotalSeconds / 86400);

    /// <summary>The whole hours left over after <see cref="EquivalentDays"/>.</summary>
    public int EquivalentHours => (int)(TotalSeconds % 86400 / 3600);

    /// <summary>Total whole hours (rounded down, so it agrees with the days-and-hours line), the big number on the first card.</summary>
    public long TotalHours => TotalSeconds / 3600;
}
