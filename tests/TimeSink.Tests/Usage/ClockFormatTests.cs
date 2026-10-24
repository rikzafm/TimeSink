using System.Globalization;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public class ClockFormatTests
{
    private static readonly DateTime T = new(2026, 3, 11, 13, 42, 0);

    private static CultureInfo WithPattern(string name, string shortTime)
    {
        var c = new CultureInfo(name, useUserOverride: false);
        c.DateTimeFormat.ShortTimePattern = shortTime; // what a user's Region settings change does
        return c;
    }

    [Fact]
    public void Twelve_hour_pattern_gives_am_pm_clock_and_hour_labels()
    {
        var c = WithPattern("en-US", "h:mm tt");
        ClockFormat.Is24Hour(c).Should().BeFalse();
        ClockFormat.Clock(T, c).Should().Be("1:42 pm");
        ClockFormat.Hour(6, c).Should().Be("6 am");
        ClockFormat.Hour(18, c).Should().Be("6 pm");
        ClockFormat.Hour(0, c).Should().Be("12 am");
    }

    [Fact]
    public void Twenty_four_hour_pattern_gives_24_hour_clock_and_hour_labels()
    {
        var c = WithPattern("en-GB", "HH:mm");
        ClockFormat.Is24Hour(c).Should().BeTrue();
        ClockFormat.Clock(T, c).Should().Be("13:42");
        ClockFormat.Hour(6, c).Should().Be("06:00");
        ClockFormat.Hour(18, c).Should().Be("18:00");
    }

    [Fact]
    public void The_windows_setting_wins_over_the_language_default()
    {
        // The same language flips with the Region setting: nothing is hard-coded per language.
        ClockFormat.Clock(T, WithPattern("en-US", "HH:mm")).Should().Be("13:42");
        ClockFormat.Clock(T, WithPattern("de-DE", "h:mm tt")).Should().EndWith("pm");
    }

    [Fact]
    public void Hour_wraps_into_a_day()
    {
        var c = WithPattern("en-US", "h:mm tt");
        ClockFormat.Hour(24, c).Should().Be("12 am");
    }

    [Fact]
    public void Windows_culture_keeps_user_overrides_on()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB", useUserOverride: false);
            ClockFormat.WindowsCulture().UseUserOverride.Should().BeTrue();
            ClockFormat.WindowsCulture().Name.Should().Be("en-GB");
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
