using TimeSink.Core.Theming;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Theming;

/// <summary>Decision 164: every key Core can choose for a Phrase is defined by the built-in themes, so none shows as "[key]".</summary>
public class PhraseKeysTests
{
    private static readonly DateOnly Monday = new(2026, 3, 9);

    private static IEnumerable<Phrase> Flatten(Phrase p) =>
        new[] { p }.Concat(p.Args.OfType<Phrase>().SelectMany(Flatten));

    private static IEnumerable<Phrase> EveryPhrase()
    {
        var days = new[] { new WeekDay(Monday, 3600, false, false) };
        foreach (var (total, prev) in new[] { (7200L, 3600L), (3600L, 7200L), (3600L, 0L) })
            foreach (var p in UsagePhrases.WeekSummary(new WeekResult(Monday, Monday, 0, total, days, [], 0, [], [], prev, Monday)))
                yield return p;

        var comparisons = new[]
        {
            new PanelComparison(PanelComparisonKind.TypicalForHour, 60, 600),
            new PanelComparison(PanelComparisonKind.VsWeekAverage, 60),
            new PanelComparison(PanelComparisonKind.VsSameDayLastWeek, -60),
        };
        foreach (var focus in new PanelFocus?[] { null, new(1, "A", 0), new(1, "A", 60) })
            foreach (var (start, total) in new (long?, long)[] { (null, 0), (100, 600) })
            {
                var c = new PanelContent(total, 1, start, start, focus, [], [], comparisons);
                yield return UsagePhrases.DaySubtitle(c, t => t.ToString());
                yield return UsagePhrases.HourSubtitle(c);
                foreach (var line in UsagePhrases.PanelLines(c)) { yield return line.Label; yield return line.Value; }
            }

        foreach (var kind in Enum.GetValues<YearMonthKind>())
            foreach (var delta in new[] { null, Change.Between(110, 100) })
                foreach (var started in new DateOnly?[] { null, new(2026, 3, 1) })
                {
                    var result = new YearResult(2026, 0, started, [], 0, null, 3, 0, null, 0);
                    foreach (var p in UsagePhrases.YearMonthNote(result, new YearMonth(3, kind, 60, 120, delta, null, []))) yield return p;
                }
    }

    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Themes_define_every_key_a_phrase_can_carry(string id)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "themes");
        var t = new ThemeLoader(new FolderThemeSource(dir)).Load(id);
        var keys = EveryPhrase().SelectMany(Flatten).Select(p => p.Key).Distinct().ToList();

        keys.Should().NotBeEmpty();
        foreach (var key in keys) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }
}
