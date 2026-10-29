using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

public sealed class TodayPastDayTests : IDisposable
{
    private readonly TodayFixture _f = new();
    public void Dispose() => _f.Dispose();

    private TodayResult Run(DateOnly? day) => Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc, null, day);

    [Fact]
    public void A_past_day_sums_that_day_only_and_is_not_live()
    {
        var r = Run(new DateOnly(2026, 3, 10));
        r.Date.Should().Be(new DateOnly(2026, 3, 10));
        r.TotalSeconds.Should().Be(3600 + 7200 + 7200); // Chrome 23-24 and 8-10, Code 14-16
        r.RunningCount.Should().Be(0);
        r.OpenSessionCount.Should().Be(0);
        r.Apps.Should().OnlyContain(a => !a.IsRunning);
    }

    [Fact]
    public void A_future_or_missing_day_means_today()
    {
        Run(null).Date.Should().Be(new DateOnly(2026, 3, 11));
        Run(new DateOnly(2026, 3, 20)).Date.Should().Be(new DateOnly(2026, 3, 11));
    }

    [Fact]
    public void A_past_day_compares_with_the_whole_day_before()
    {
        // 03-10 has 18000 s; 03-09 has nothing.
        Run(new DateOnly(2026, 3, 10)).VsYesterdaySeconds.Should().Be(18000);
    }

    [Fact]
    public void Hour_detail_splits_the_hour_by_app_and_session()
    {
        var r = Run(null);
        var d = BarDetail.ForHour(r, 9, TimeZoneInfo.Utc);
        d.AppSeconds.Should().Be(3600); // Chrome 9:00-9:30 and Code 8:30-9:30 clipped to 9:00-9:30...
        d.Apps.Select(a => a.Name).Should().BeEquivalentTo("Chrome", "Code");
        d.Apps.Sum(a => a.Seconds).Should().Be(d.AppSeconds);
        d.Sessions.Should().HaveCount(2);
        d.Sessions.Should().OnlyContain(s => s.StartUtc >= d.StartUtc && s.EndUtc <= d.EndUtc);
    }

    [Fact]
    public void First_date_is_the_day_tracking_began()
    {
        Q.Today(_f.DbPath, TodayFixture.Now, TimeZoneInfo.Utc).FirstTracked.Should().Be(new DateOnly(2026, 2, 20));
    }
}
