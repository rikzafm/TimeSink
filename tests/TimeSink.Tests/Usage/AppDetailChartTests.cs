using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class AppDetailChartTests
{
    private static readonly DateOnly Today = new(2026, 3, 11); // a Wednesday
    private static readonly DateOnly First = new(2025, 11, 20);

    private static readonly Dictionary<DateOnly, long> Days = new()
    {
        [new(2025, 11, 20)] = 3600,
        [new(2026, 1, 5)] = 7200,
        [new(2026, 3, 2)] = 600,
        [new(2026, 3, 11)] = 1800,
    };

    private static readonly AppDetailOpen[] Opens =
    [
        new(new(2025, 11, 20), 21), new(new(2026, 1, 5), 21), new(new(2026, 3, 2), 9), new(new(2026, 3, 11), 21),
    ];

    private static AppDetailChartData Build(AppDetailRange range, DateOnly? anchor = null) =>
        AppDetailChart.Build(range, anchor ?? Today, Today, First, DayOfWeek.Monday, Days, Opens);

    [Fact]
    public void All_is_a_bar_per_year_from_the_first_tracked_year()
    {
        var d = Build(AppDetailRange.All);
        d.Bars.Select(b => (b.Label, b.Seconds)).Should().Equal(("2025", 3600L), ("2026", 9600L));
        d.TotalSeconds.Should().Be(13200);
        d.CanPrev.Should().BeFalse();
        d.CanNext.Should().BeFalse();
        d.OpensByHour[21].Should().Be(3);
        d.PeakHour.Should().Be(21);
    }

    [Fact]
    public void Year_is_a_bar_per_month_and_only_counts_that_years_openings()
    {
        var d = Build(AppDetailRange.Year);
        d.Bars.Should().HaveCount(12);
        d.Bars[0].Seconds.Should().Be(7200);
        d.Bars[2].Seconds.Should().Be(2400);
        d.OpensByHour[21].Should().Be(2);
        d.CanPrev.Should().BeTrue();
        d.CanNext.Should().BeFalse();
    }

    [Fact]
    public void Month_is_a_bar_per_day()
    {
        var d = Build(AppDetailRange.Month);
        d.Bars.Should().HaveCount(31);
        d.Bars[1].Seconds.Should().Be(600);
        d.Bars[10].Seconds.Should().Be(1800);
        d.PeakHour.Should().Be(9); // 9 and 21 tie at one opening each: the earlier hour wins
    }

    [Fact]
    public void Week_runs_from_the_week_start_to_six_days_later()
    {
        var d = Build(AppDetailRange.Week);
        d.From.Should().Be(new DateOnly(2026, 3, 9));
        d.To.Should().Be(new DateOnly(2026, 3, 15));
        d.Bars.Select(b => b.Label).Should().Equal("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun");
        d.Bars[2].Seconds.Should().Be(1800);
        d.PeakHour.Should().Be(21);
    }

    [Fact]
    public void A_period_without_openings_has_no_peak_hour()
    {
        var d = Build(AppDetailRange.Month, new DateOnly(2026, 2, 10));
        d.PeakHour.Should().BeNull();
        d.TotalSeconds.Should().Be(0);
        d.CanNext.Should().BeTrue();
    }

    [Fact]
    public void The_anchor_is_clamped_between_first_tracked_and_today()
    {
        Build(AppDetailRange.Month, new DateOnly(2020, 1, 1)).From.Should().Be(new DateOnly(2025, 11, 1));
        Build(AppDetailRange.Month, new DateOnly(2030, 1, 1)).From.Should().Be(new DateOnly(2026, 3, 1));
        Build(AppDetailRange.Month, new DateOnly(2025, 11, 25)).CanPrev.Should().BeFalse();
    }

    [Theory]
    [InlineData(AppDetailRange.Year, -1, "2025-03-11")]
    [InlineData(AppDetailRange.Month, 1, "2026-04-11")]
    [InlineData(AppDetailRange.Week, -1, "2026-03-04")]
    [InlineData(AppDetailRange.All, 1, "2026-03-11")]
    public void Step_moves_one_period(AppDetailRange range, int dir, string expected) =>
        AppDetailChart.Step(range, Today, dir).Should().Be(DateOnly.Parse(expected));
}
