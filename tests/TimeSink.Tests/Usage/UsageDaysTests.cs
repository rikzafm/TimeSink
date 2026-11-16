using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Sessions cut at Local-day boundaries, in America/New_York for the DST cases:
/// 2026-03-08 spring forward (23 h, local midnights at 05:00Z and 04:00Z next day), 2026-11-01 fall back (25 h, 04:00Z to 05:00Z next day).
/// </summary>
public sealed class UsageDaysTests
{
    private static readonly TimeZoneInfo Ny = FindNewYork();

    private static TimeZoneInfo FindNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("no New York time zone on this machine");
    }

    private static long U(int y, int mo, int d, int h = 0, int mi = 0) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static UsageSeg Seg(long start, long end, long group = 1) => new(group, group, start, end, false, false);

    private static List<UsageSlice> Cut(TimeZoneInfo zone, params UsageSeg[] segs) => UsageDays.Slices(segs, zone).ToList();

    [Fact]
    public void A_session_inside_one_day_is_one_slice()
    {
        var s = Seg(U(2026, 5, 4, 14), U(2026, 5, 4, 15));
        var slices = Cut(TimeZoneInfo.Utc, s);
        var slice = Assert.Single(slices);
        Assert.Equal(new DateOnly(2026, 5, 4), slice.Day);
        Assert.Equal((s.Start, s.End), (slice.Start, slice.End));
        Assert.Equal(s, slice.Seg);
    }

    [Fact]
    public void A_session_across_midnight_is_cut_there_and_each_slice_keeps_the_whole_session()
    {
        var s = Seg(U(2026, 5, 4, 23), U(2026, 5, 5, 2));
        var slices = Cut(TimeZoneInfo.Utc, s);
        Assert.Equal([new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 5)], slices.Select(x => x.Day));
        Assert.Equal([3600L, 7200L], slices.Select(x => x.End - x.Start));
        Assert.All(slices, x => Assert.Equal(s, x.Seg));
    }

    [Fact]
    public void A_session_ending_exactly_at_midnight_does_not_touch_the_next_day()
    {
        var slices = Cut(TimeZoneInfo.Utc, Seg(U(2026, 5, 4, 23), U(2026, 5, 5)));
        Assert.Equal(new DateOnly(2026, 5, 4), Assert.Single(slices).Day);
    }

    [Fact]
    public void An_empty_or_inverted_session_gives_no_slices()
    {
        Assert.Empty(Cut(TimeZoneInfo.Utc, Seg(100, 100), Seg(200, 100)));
    }

    [Fact]
    public void A_session_spanning_several_days_gives_a_slice_for_each()
    {
        var slices = Cut(TimeZoneInfo.Utc, Seg(U(2026, 5, 4, 12), U(2026, 5, 7, 12)));
        Assert.Equal(4, slices.Count);
        Assert.Equal([43200L, 86400L, 86400L, 43200L], slices.Select(x => x.End - x.Start));
    }

    [Fact]
    public void The_spring_forward_day_is_23_hours()
    {
        // 2026-03-07 05:00Z .. 2026-03-09 04:00Z covers the local days of Mar 7 (24 h), Mar 8 (23 h) and part of Mar 9.
        var slices = Cut(Ny, Seg(U(2026, 3, 7, 5), U(2026, 3, 9, 4)));
        Assert.Equal([new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 8)], slices.Select(x => x.Day));
        Assert.Equal([24 * 3600L, 23 * 3600L], slices.Select(x => x.End - x.Start));
    }

    [Fact]
    public void The_fall_back_day_is_25_hours()
    {
        var slices = Cut(Ny, Seg(U(2026, 10, 31, 4), U(2026, 11, 2, 5)));
        Assert.Equal([new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1)], slices.Select(x => x.Day));
        Assert.Equal([24 * 3600L, 25 * 3600L], slices.Select(x => x.End - x.Start));
    }

    [Fact]
    public void A_session_over_the_skipped_hour_is_cut_at_the_real_midnight()
    {
        // 01:30 EST to 03:30 EDT on Mar 8 is one real hour; it all belongs to Mar 8.
        var slices = Cut(Ny, Seg(U(2026, 3, 8, 6, 30), U(2026, 3, 8, 7, 30)));
        var slice = Assert.Single(slices);
        Assert.Equal(new DateOnly(2026, 3, 8), slice.Day);
        Assert.Equal(3600, slice.End - slice.Start);
    }

    [Fact]
    public void Slices_of_one_session_add_up_to_its_length()
    {
        var s = Seg(U(2026, 3, 7, 20), U(2026, 3, 9, 9));
        Assert.Equal(s.End - s.Start, Cut(Ny, s).Sum(x => x.End - x.Start));
    }

    private static UsageSeg On(string device, long start, long end, long app, long group = 1) => new(group, app, start, end, false, false, device);

    [Fact]
    public void Runs_count_a_groups_overlapping_sessions_on_one_device_once()
    {
        // A launcher and its game, merged: open together 10:00 to 12:00, the game alone until 13:00.
        var runs = UsageDays.Runs([On("pc", U(2026, 5, 4, 10), U(2026, 5, 4, 12), app: 1), On("pc", U(2026, 5, 4, 10), U(2026, 5, 4, 13), app: 2)]);
        var run = Assert.Single(runs);
        Assert.Equal((1L, U(2026, 5, 4, 10), U(2026, 5, 4, 13), "pc"), (run.Group, run.Start, run.End, run.DeviceId));
    }

    [Fact]
    public void Runs_keep_sessions_that_only_touch_apart_and_stay_open_while_a_member_is()
    {
        var runs = UsageDays.Runs([On("pc", U(2026, 5, 4, 10), U(2026, 5, 4, 11), app: 1), On("pc", U(2026, 5, 4, 11), U(2026, 5, 4, 12), app: 1),
            On("pc", U(2026, 5, 4, 14), U(2026, 5, 4, 16), app: 1), On("pc", U(2026, 5, 4, 15), U(2026, 5, 4, 15, 30), app: 2) with { Open = true }]);
        Assert.Equal([3600L, 3600L, 7200L], runs.Select(r => r.End - r.Start));
        Assert.Equal([false, false, true], runs.Select(r => r.Open));
    }

    [Fact]
    public void Runs_do_not_join_other_groups_or_other_devices()
    {
        var runs = UsageDays.Runs([On("pc", U(2026, 5, 4, 10), U(2026, 5, 4, 12), app: 1), On("mac", U(2026, 5, 4, 11), U(2026, 5, 4, 13), app: 2),
            On("pc", U(2026, 5, 4, 10), U(2026, 5, 4, 12), app: 3, group: 3)]);
        Assert.Equal(3, runs.Count);
        Assert.Equal(3 * 7200L, runs.Sum(r => r.End - r.Start));
    }

    [Fact]
    public void Per_day_group_seconds_count_overlapping_merged_sessions_once()
    {
        var days = UsageDays.PerDayGroup([On("pc", U(2026, 5, 4, 23), U(2026, 5, 5, 1), app: 1), On("pc", U(2026, 5, 4, 23), U(2026, 5, 5, 2), app: 2)], TimeZoneInfo.Utc);
        Assert.Equal(3600L, days[new DateOnly(2026, 5, 4)][1]);
        Assert.Equal(7200L, days[new DateOnly(2026, 5, 5)][1]);
    }
}
