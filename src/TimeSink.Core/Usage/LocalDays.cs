namespace TimeSink.Core.Usage;

/// <summary>Local-calendar-day helpers shared by the usage queries (DST-aware).</summary>
internal static class LocalDays
{
    public static DateOnly DateOf(long utcSeconds, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utcSeconds), zone).DateTime);

    /// <summary>UTC unix seconds of local midnight (a skipped midnight moves to the first valid hour; an ambiguous one takes the later offset).</summary>
    public static long StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUnixTimeSeconds();
    }
}
