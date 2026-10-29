using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 148: the Month day bar and its Mostly / Spread line.</summary>
public sealed class DayBarTests
{
    private static MonthAppAmount A(long id, string name, long secs) => new(id, name, secs);

    [Fact]
    public void An_empty_day_has_no_segments_and_the_empty_verdict()
    {
        var bar = DayBars.Of([]);
        bar.Segments.Should().BeEmpty();
        bar.Verdict.Should().Be(DayVerdict.Empty);
        bar.Leader.Should().BeNull();
    }

    [Fact]
    public void One_app_is_mostly_that_app()
    {
        var bar = DayBars.Of([A(1, "Chrome", 600)]);
        bar.Verdict.Should().Be(DayVerdict.Mostly);
        bar.Leader!.Name.Should().Be("Chrome");
    }

    [Fact]
    public void Mostly_needs_one_and_a_half_times_the_runner_up()
    {
        DayBars.Of([A(1, "A", 300), A(2, "B", 200)]).Verdict.Should().Be(DayVerdict.Mostly); // exactly 1.5x
        var spread = DayBars.Of([A(1, "A", 299), A(2, "B", 200)]);
        spread.Verdict.Should().Be(DayVerdict.Spread);
        spread.Leader.Should().BeNull();
        spread.AppCount.Should().Be(2);
    }

    [Fact]
    public void Segments_run_by_time_even_when_the_input_ranks_a_Background_app_last()
    {
        // Ranking puts the Background app (Steam, 9000) last; the bar still leads with it.
        var bar = DayBars.Of([A(1, "Code", 600), A(2, "Steam", 9000)]);
        bar.Segments.Select(s => s.Name).Should().Equal("Steam", "Code");
        bar.Verdict.Should().Be(DayVerdict.Mostly);
        bar.Leader!.Name.Should().Be("Steam");
    }

    [Fact]
    public void Apps_beyond_the_seventh_fold_into_Other()
    {
        var apps = Enumerable.Range(1, 9).Select(i => A(i, "App" + i, 1000 - i)).ToList();
        var bar = DayBars.Of(apps);
        bar.Segments.Should().HaveCount(DayBars.MaxSegments + 1);
        var other = bar.Segments[^1];
        other.AppId.Should().BeNull();
        other.Name.Should().Be("Other");
        other.Seconds.Should().Be((1000 - 8) + (1000 - 9));
        bar.AppCount.Should().Be(9);
    }
}
