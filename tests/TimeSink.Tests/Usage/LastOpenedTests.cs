using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class LastOpenedTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly long Now = new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static LastOpened At(long ageSeconds, TimeZoneInfo? zone = null) => LastOpened.Of(Now - ageSeconds, Now, zone ?? Utc);

    [Fact]
    public void No_time_recorded_is_never()
    {
        LastOpened.Of(0, Now, Utc).Kind.Should().Be(LastOpenedKind.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    public void Under_a_minute_is_just_now(long age) => At(age).Kind.Should().Be(LastOpenedKind.JustNow);

    [Fact]
    public void A_time_after_now_is_just_now() => LastOpened.Of(Now + 30, Now, Utc).Kind.Should().Be(LastOpenedKind.JustNow);

    [Theory]
    [InlineData(60, 1)]
    [InlineData(3599, 59)]
    public void Under_an_hour_is_minutes(long age, int minutes) => At(age).Should().Be(new LastOpened(LastOpenedKind.MinutesAgo, minutes));

    [Theory]
    [InlineData(3600, 1)]
    [InlineData(7199, 1)]
    [InlineData(15 * 3600, 15)] // 00:00 today, still today
    public void Earlier_today_is_whole_hours(long age, int hours) => At(age).Should().Be(new LastOpened(LastOpenedKind.HoursAgo, hours));

    [Fact]
    public void The_Local_day_decides_not_the_elapsed_time()
    {
        At(15 * 3600 + 1).Kind.Should().Be(LastOpenedKind.Yesterday);  // 23:59:59 the day before, 15 h ago
        At(39 * 3600).Kind.Should().Be(LastOpenedKind.Yesterday);      // 00:00 yesterday
        At(39 * 3600 + 1).Kind.Should().Be(LastOpenedKind.Date);       // 23:59:59 two days ago
    }

    [Fact]
    public void An_older_day_is_a_date_and_says_whether_it_is_this_year()
    {
        var thisYear = At(10 * 86400);
        thisYear.Should().Be(new LastOpened(LastOpenedKind.Date, 0, new DateOnly(2026, 2, 28), true));
        var lastYear = At(100 * 86400);
        lastYear.Kind.Should().Be(LastOpenedKind.Date);
        lastYear.Date.Year.Should().Be(2025);
        lastYear.InCurrentYear.Should().BeFalse();
    }

    [Fact]
    public void The_zone_moves_the_day_boundary()
    {
        var plus13 = TimeZoneInfo.CreateCustomTimeZone("p13", TimeSpan.FromHours(13), "p13", "p13");
        // 15:00Z is 04:00 the next day at +13; 10 h earlier is 05:00Z = 18:00 the same day at +13 -> Yesterday there, hours in UTC.
        At(10 * 3600, Utc).Kind.Should().Be(LastOpenedKind.HoursAgo);
        At(10 * 3600, plus13).Kind.Should().Be(LastOpenedKind.Yesterday);
    }
}
