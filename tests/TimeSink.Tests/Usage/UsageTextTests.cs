using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public class UsageTextTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(-300, "0")]        // negative clamps
    [InlineData(600, "0.2")]       // 10 min
    [InlineData(1800, "0.5")]
    [InlineData(5400, "1.5")]
    [InlineData(9000, "2.5")]      // kept as a decimal, not rounded to 2 or 3
    [InlineData(35999, "10")]      // 9.9997 h rounds to 10, shown whole
    [InlineData(36000, "10")]
    [InlineData(98400, "27")]      // 27.33 h
    [InlineData(2196000, "610")]
    public void Hours_show_one_decimal_under_ten_hours_and_whole_hours_above(long seconds, string expected) =>
        UsageText.Hours(seconds).Should().Be(expected);

    [Theory]
    [InlineData(12.0, "+12%")]
    [InlineData(-3.0, "-3%")]
    [InlineData(0.0, "0%")]
    [InlineData(0.4, "0%")]
    [InlineData(-0.3, "0%")]       // YearPaceView printed "-0%" here
    [InlineData(0.5, "+1%")]       // half away from zero
    [InlineData(-0.5, "-1%")]
    [InlineData(double.NaN, "")]
    [InlineData(double.PositiveInfinity, "")]
    public void Delta_is_a_signed_whole_percent_and_zero_has_no_sign(double percent, string expected) =>
        UsageText.Delta(percent).Should().Be(expected);

    [Theory]
    [InlineData(12.4, "12")]
    [InlineData(-12.5, "13")]      // magnitude, half away from zero
    [InlineData(-0.2, "0")]
    [InlineData(double.NaN, "")]
    public void DeltaMagnitude_is_the_whole_percent_digits_for_up_and_down_templates(double percent, string expected) =>
        UsageText.DeltaMagnitude(percent).Should().Be(expected);

    [Theory]
    [InlineData(0.08, "+8%")]
    [InlineData(-0.125, "-13%")]
    [InlineData(0.004, "0%")]
    [InlineData(double.NaN, "")]
    public void DeltaOfRatio_reads_a_fraction_as_percent(double fraction, string expected) =>
        UsageText.DeltaOfRatio(fraction).Should().Be(expected);

    [Theory]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    [InlineData(59, "0m")]         // matches MonthView.ShortHours today
    [InlineData(2520, "42m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1.0h")]
    [InlineData(5400, "1.5h")]
    [InlineData(9000, "2.5h")]
    [InlineData(25200, "7.0h")]
    public void MonthCell_is_blank_for_none_minutes_under_an_hour_and_hours_above(long seconds, string expected) =>
        UsageText.MonthCell(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(54 * 60, "54m")]
    [InlineData(3600, "1h 00m")]
    [InlineData(17 * 3600 + 21 * 60 + 32, "17h 21m")]
    public void HoursMinutes_is_Xh_YYm(long seconds, string expected) => UsageText.HoursMinutes(seconds).Should().Be(expected);

    [Theory]
    [InlineData(38 * 60, "+38m")]
    [InlineData(-90 * 60, "-1h 30m")]
    [InlineData(0, "+0m")]
    public void SignedHoursMinutes_carries_a_sign(long seconds, string expected) => UsageText.SignedHoursMinutes(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(5400, "1.5")]
    [InlineData(13846.15, "3.8")]
    [InlineData(82800, "23.0")]
    public void AveragePerDay_is_hours_with_one_decimal(double seconds, string expected) =>
        UsageText.AveragePerDay(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.003333, "<1%")]
    [InlineData(0.01, "1%")]
    [InlineData(0.54666, "55%")]
    [InlineData(1.0, "100%")]
    public void Share_is_a_whole_percent_with_a_floor_for_tiny_shares(double share, string expected) =>
        UsageText.Share(share).Should().Be(expected);

    private static readonly TimeZoneInfo Ny = FindNewYork();

    private static TimeZoneInfo FindNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("no New York time zone on this machine");
    }

    private static long At(int y, int mo, int d, int h, int mi, int offsetHours) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(offsetHours)).ToUnixTimeSeconds();

    // EST = -5, EDT = -4. Spring forward 2026-03-08 (23 h day); fall back 2026-11-01 (25 h day).
    [Fact]
    public void DayOf_same_date_is_Today()
    {
        var b = UsageText.DayOf(At(2026, 6, 10, 8, 0, -4), At(2026, 6, 10, 20, 0, -4), Ny);
        b.Should().Be(new DayBucket(DayKind.Today, new DateOnly(2026, 6, 10), true));
    }

    [Fact]
    public void DayOf_previous_date_is_Yesterday_even_when_less_than_a_day_ago()
    {
        var b = UsageText.DayOf(At(2026, 6, 9, 23, 50, -4), At(2026, 6, 10, 0, 10, -4), Ny);
        b.Kind.Should().Be(DayKind.Yesterday);
        b.Date.Should().Be(new DateOnly(2026, 6, 9));
    }

    [Fact]
    public void DayOf_on_a_25_hour_day_more_than_24_hours_ago_is_still_Today()
    {
        // 00:10 EDT to 23:50 EST on 2026-11-01 is 24 h 40 min apart but the same Local day.
        UsageText.DayOf(At(2026, 11, 1, 0, 10, -4), At(2026, 11, 1, 23, 50, -5), Ny).Kind.Should().Be(DayKind.Today);
    }

    [Fact]
    public void DayOf_across_a_23_hour_day_less_than_24_hours_ago_is_Yesterday()
    {
        // 23:50 EST on 03-07 to 23:40 EDT on 03-08 is 22 h 50 min apart but the previous Local day.
        UsageText.DayOf(At(2026, 3, 7, 23, 50, -5), At(2026, 3, 8, 23, 40, -4), Ny).Kind.Should().Be(DayKind.Yesterday);
    }

    [Fact]
    public void DayOf_older_dates_are_Date_and_know_whether_they_share_the_year_of_now()
    {
        var sameYear = UsageText.DayOf(At(2026, 1, 15, 12, 0, -5), At(2026, 11, 2, 9, 0, -5), Ny);
        sameYear.Should().Be(new DayBucket(DayKind.Date, new DateOnly(2026, 1, 15), true));

        var otherYear = UsageText.DayOf(At(2025, 6, 1, 12, 0, -4), At(2026, 1, 1, 10, 0, -5), Ny);
        otherYear.Should().Be(new DayBucket(DayKind.Date, new DateOnly(2025, 6, 1), false));
    }

    [Fact]
    public void DayOf_judges_the_year_in_the_zone_not_in_UTC()
    {
        // 2025-12-31 23:00 EST is already 2026-01-01 in UTC; Yesterday relative to 2026-01-01 10:00 EST, a 2025 date.
        var b = UsageText.DayOf(At(2025, 12, 31, 23, 0, -5), At(2026, 1, 1, 10, 0, -5), Ny);
        b.Kind.Should().Be(DayKind.Yesterday);
        b.InCurrentYear.Should().BeFalse();
    }

    [Fact]
    public void DayOf_a_time_after_now_on_the_same_date_is_Today()
    {
        UsageText.DayOf(At(2026, 6, 10, 9, 0, -4), At(2026, 6, 10, 8, 0, -4), Ny).Kind.Should().Be(DayKind.Today);
    }
}
