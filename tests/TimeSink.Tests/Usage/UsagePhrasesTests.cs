using TimeSink.Core.Theming;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 164: which string a result is told with, and with what, is decided in Core; the views only look it up.</summary>
public sealed class UsagePhrasesTests
{
    private static readonly DateOnly Monday = new(2026, 3, 9);

    private static WeekResult Week(long total, long prevWeek, params long[] daySeconds)
    {
        var days = daySeconds.Select((s, i) => new WeekDay(Monday.AddDays(i), s, false, false)).ToList();
        return new WeekResult(Monday.AddDays(6), Monday, 0, total, days, [], 0, [], [], prevWeek, Monday);
    }

    [Fact]
    public void A_week_up_on_last_week_says_so_with_the_gain_in_hours_and_minutes()
    {
        var phrases = UsagePhrases.WeekSummary(Week(total: 10800, prevWeek: 7200, 10800));

        phrases[0].Should().Be(new Phrase(StringKeys.WeekVsLastUp, UsageText.HoursMinutes(3600)));
    }

    [Fact]
    public void A_week_down_on_last_week_says_so_with_the_size_of_the_drop()
    {
        var phrases = UsagePhrases.WeekSummary(Week(total: 3600, prevWeek: 7200, 3600));

        phrases[0].Should().Be(new Phrase(StringKeys.WeekVsLastDown, UsageText.HoursMinutes(3600)));
    }

    [Fact]
    public void A_week_level_with_last_week_reads_as_up()
    {
        var phrases = UsagePhrases.WeekSummary(Week(total: 7200, prevWeek: 7200, 7200));

        phrases[0].Key.Should().Be(StringKeys.WeekVsLastUp);
    }

    [Fact]
    public void Without_a_previous_week_there_is_no_change_line_only_the_average()
    {
        var phrases = UsagePhrases.WeekSummary(Week(total: 7200, prevWeek: 0, 7200));

        phrases.Should().Equal(new Phrase(StringKeys.WeekAvg, UsageText.HoursMinutes(7200)));
    }

    [Fact]
    public void A_week_with_nothing_to_say_has_no_phrases() =>
        UsagePhrases.WeekSummary(Week(total: 0, prevWeek: 0, 0, 0)).Should().BeEmpty();

    private static string Clock(long utc) => $"c{utc}";

    private static PanelContent Panel(long total = 7200, long? spanStart = 100, long? spanEnd = 900, PanelFocus? focus = null,
        int sessions = 0, params PanelComparison[] comparisons) =>
        new(total, sessions, spanStart, spanEnd, focus, [], [], comparisons);

    [Fact]
    public void A_day_subtitle_is_the_total_and_the_active_span()
    {
        UsagePhrases.DaySubtitle(Panel(), Clock).Should().Be(
            new Phrase(PanelStringKeys.DayTotal, UsageText.HoursMinutes(7200), new Phrase(PanelStringKeys.DaySpan, "c100", "c900")));
    }

    [Fact]
    public void A_day_with_no_activity_span_has_an_empty_span()
    {
        UsagePhrases.DaySubtitle(Panel(total: 0, spanStart: null, spanEnd: null), Clock).Should().Be(
            new Phrase(PanelStringKeys.DayTotal, UsageText.HoursMinutes(0), ""));
    }

    [Fact]
    public void A_focus_app_leads_the_subtitle_with_its_own_time()
    {
        var focus = new PanelFocus(2, "Chrome", 3600);

        UsagePhrases.DaySubtitle(Panel(focus: focus), Clock).Should().Be(new Phrase(PanelStringKeys.DayFocus, "Chrome",
            UsageText.HoursMinutes(3600), UsageText.HoursMinutes(7200), new Phrase(PanelStringKeys.DaySpan, "c100", "c900")));
    }

    [Fact]
    public void A_focus_app_that_was_not_used_says_so()
    {
        var focus = new PanelFocus(9, "Steam", 0);

        UsagePhrases.DaySubtitle(Panel(focus: focus, spanStart: null, spanEnd: null), Clock).Should().Be(new Phrase(PanelStringKeys.DayFocus, "Steam",
            new Phrase(PanelStringKeys.DayNotUsed), UsageText.HoursMinutes(7200), ""));
    }

    [Fact]
    public void An_hour_subtitle_counts_app_time_and_sessions_or_says_the_hour_was_empty()
    {
        UsagePhrases.HourSubtitle(Panel(total: 1800, sessions: 3)).Should().Be(
            new Phrase(PanelStringKeys.HourSummary, UsageText.HoursMinutes(1800), 3));
        UsagePhrases.HourSubtitle(Panel(total: 0)).Should().Be(new Phrase(PanelStringKeys.HourEmpty));
    }

    [Fact]
    public void The_day_comparisons_are_labelled_by_kind_and_signed()
    {
        var lines = UsagePhrases.PanelLines(Panel(comparisons:
        [
            new PanelComparison(PanelComparisonKind.VsWeekAverage, 1800),
            new PanelComparison(PanelComparisonKind.VsSameDayLastWeek, -600),
        ]));

        lines.Should().Equal(
            new PanelLine(new Phrase(PanelStringKeys.DayVsAverage), new Phrase(PanelStringKeys.Delta, UsageText.SignedHoursMinutes(1800)), true),
            new PanelLine(new Phrase(PanelStringKeys.DayVsLastWeek), new Phrase(PanelStringKeys.Delta, UsageText.SignedHoursMinutes(-600)), false));
    }

    private static YearResult Year(DateOnly? started, int? bestMonth = null) =>
        new(2026, 0, started, [], 0, null, bestMonth, 0, null, 0);

    private static YearMonth Month(int month, YearMonthKind kind, long seconds = 36000, long projected = 72000, double? deltaPercent = null) =>
        new(month, kind, seconds, projected, deltaPercent is { } p ? Change.Between(100 + p, 100) : null, null, []);

    private static Phrase H(long seconds) => new(YearStringKeys.Hours, UsageText.Hours(seconds));

    [Fact]
    public void A_month_before_tracking_says_when_tracking_began_if_it_did()
    {
        var m = Month(1, YearMonthKind.BeforeTracking, 0, 0);

        UsagePhrases.YearMonthNote(Year(null), m).Should().Equal(new Phrase(YearStringKeys.NoteBefore));
        UsagePhrases.YearMonthNote(Year(new DateOnly(2026, 3, 9)), m).Should().Equal(new Phrase(YearStringKeys.NoteBeforeOn, "9 Mar"));
    }

    [Fact]
    public void A_future_month_is_a_projection()
    {
        UsagePhrases.YearMonthNote(Year(null), Month(9, YearMonthKind.Future)).Should().Equal(new Phrase(YearStringKeys.NoteProjected));
    }

    [Fact]
    public void The_current_month_shows_the_pace_with_its_change_when_there_is_one()
    {
        UsagePhrases.YearMonthNote(Year(null), Month(5, YearMonthKind.Current)).Should().Equal(
            new Phrase(YearStringKeys.NoteCurrent, H(36000), H(72000)));

        UsagePhrases.YearMonthNote(Year(null), Month(5, YearMonthKind.Current, deltaPercent: 12)).Should().Equal(
            new Phrase(YearStringKeys.NoteCurrentVs, H(36000), H(72000), "+12%", "Apr"));
    }

    [Fact]
    public void A_finished_month_compares_with_the_month_before_and_january_with_december()
    {
        UsagePhrases.YearMonthNote(Year(null), Month(4, YearMonthKind.Tracked, deltaPercent: -8)).Should().Equal(
            new Phrase(YearStringKeys.NoteVs, "-8%", "Mar"));
        UsagePhrases.YearMonthNote(Year(null), Month(1, YearMonthKind.Tracked, deltaPercent: 5)).Should().Equal(
            new Phrase(YearStringKeys.NoteVs, "+5%", "Dec"));
    }

    [Fact]
    public void The_first_tracked_month_says_so_only_when_it_has_no_change_to_show()
    {
        var started = new DateOnly(2026, 3, 9);

        UsagePhrases.YearMonthNote(Year(started), Month(3, YearMonthKind.Tracked)).Should().Equal(new Phrase(YearStringKeys.NoteFirst, "9 Mar"));
        UsagePhrases.YearMonthNote(Year(started), Month(4, YearMonthKind.Tracked)).Should().BeEmpty();
        UsagePhrases.YearMonthNote(Year(new DateOnly(2025, 3, 9)), Month(3, YearMonthKind.Tracked)).Should().BeEmpty();
    }

    [Fact]
    public void The_best_month_adds_its_note_after_the_rest_but_only_for_a_finished_month()
    {
        var year = Year(null, bestMonth: 4);

        UsagePhrases.YearMonthNote(year, Month(4, YearMonthKind.Tracked, deltaPercent: 20)).Should().Equal(
            new Phrase(YearStringKeys.NoteVs, "+20%", "Mar"), new Phrase(YearStringKeys.NoteBest));
        UsagePhrases.YearMonthNote(year, Month(4, YearMonthKind.Tracked)).Should().Equal(new Phrase(YearStringKeys.NoteBest));
        UsagePhrases.YearMonthNote(year, Month(4, YearMonthKind.Current)).Should().Equal(
            new Phrase(YearStringKeys.NoteCurrent, H(36000), H(72000)));
    }

    [Fact]
    public void The_typical_hour_comparison_shows_the_typical_amount_and_the_difference()
    {
        var lines = UsagePhrases.PanelLines(Panel(comparisons: [new PanelComparison(PanelComparisonKind.TypicalForHour, -300, 2400)]));

        lines.Should().Equal(new PanelLine(new Phrase(PanelStringKeys.HourTypical),
            new Phrase(PanelStringKeys.HourTypicalValue, UsageText.HoursMinutes(2400), UsageText.SignedHoursMinutes(-300)), false));
    }
}
