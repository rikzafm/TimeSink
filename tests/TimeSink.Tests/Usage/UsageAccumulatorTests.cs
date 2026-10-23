using TimeSink.Tests.Tracking;

namespace TimeSink.Tests.Usage;

public sealed class UsageAccumulatorTests : IDisposable
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private readonly Rig _rig = new(NewYork);
    public void Dispose() => _rig.Dispose();

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void A_range_inside_one_local_day_is_added_to_that_day()
    {
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 3, 10, 15), Utc(2026, 3, 10, 16));
        _rig.Daily().Should().BeEquivalentTo(new Dictionary<string, long> { ["2026-03-10"] = 3600 });
    }

    [Fact]
    public void A_range_crossing_local_midnight_splits_in_two_days()
    {
        // June: EDT (UTC-4). 03:30Z = 23:30 on the 9th, 04:45Z = 00:45 on the 10th.
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 6, 10, 3, 30), Utc(2026, 6, 10, 4, 45));
        _rig.Daily().Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["2026-06-09"] = 30 * 60,
            ["2026-06-10"] = 45 * 60,
        });
    }

    [Fact]
    public void Spring_forward_day_is_23_hours_long()
    {
        // 2026-03-08 in New York: local day runs 05:00Z .. 04:00Z next day (23 h).
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 3, 8, 0), Utc(2026, 3, 10, 0));
        var d = _rig.Daily();
        d["2026-03-07"].Should().Be(5 * 3600);                // 00:00Z .. 05:00Z
        d["2026-03-08"].Should().Be(23 * 3600);
        d["2026-03-09"].Should().Be(20 * 3600);               // 04:00Z .. 00:00Z
    }

    [Fact]
    public void Fall_back_day_is_25_hours_long()
    {
        // 2026-11-01: local day runs 04:00Z .. 05:00Z next day (25 h).
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 11, 1, 0), Utc(2026, 11, 3, 0));
        var d = _rig.Daily();
        d["2026-10-31"].Should().Be(4 * 3600);
        d["2026-11-01"].Should().Be(25 * 3600);
        d["2026-11-02"].Should().Be(19 * 3600);               // 05:00Z .. 00:00Z
    }

    [Fact]
    public void A_reversed_range_subtracts_and_zero_rows_disappear()
    {
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 6, 10, 15), Utc(2026, 6, 10, 16));
        _rig.Usage.Add(_rig.NotepadId, Utc(2026, 6, 10, 16), Utc(2026, 6, 10, 15));
        _rig.Daily().Should().BeEmpty();
    }
}
