using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Uses <see cref="MonthFixture"/> (zone UTC, now = 2026-03-11 14:20Z). Hand-computed lifetime numbers (seconds):
///   Chrome 98400 (Feb 61200 + Mar 37200), days Feb 2,11,12,28 + Mar 2,3,4,11 = 8, avg 12300, longest 36000 (Feb 12), best Feb, first 2026-02-02
///   Code   63000 (Feb 7200 + Mar 55800), days Feb 5 + Mar 1,2,3,10 = 5, avg 12600, longest 28800, best Mar, first 2026-02-05
///   Disc   16200 (Feb 7200 + Mar 9000), days Feb 3 + Mar 2,10 = 3, avg 5400, longest 7200, best Mar, first 2026-02-03
///   Zed 1800 (1 day), Mus 600 (1 day, first 2026-03-11)
///   Tracked app time 180000 over 13 days with any tracked time (Feb 2,3,5,11,12,28; Mar 1,2,3,4,5,10,11);
///   the Totals row is PC uptime instead: Feb 75600 + Mar 166800 = 242400, avg 18646.15
///   Old (retired 2026-03-01): 82800 over 1 day, not part of the total.
/// </summary>
public sealed class AllQueryTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private AllResult Run(IReadOnlyCollection<long>? running = null) => AllQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, running);

    [Fact]
    public void Per_app_rows_carry_hours_days_average_and_share()
    {
        var r = Run();
        var chrome = r.Apps.Single(a => a.Name == "Chrome");
        chrome.AppId.Should().Be(_f.Chrome);
        chrome.Seconds.Should().Be(98400);
        chrome.DaysUsed.Should().Be(8);
        chrome.AverageSecondsPerDay.Should().Be(12300);
        chrome.Share.Should().BeApproximately(98400 / 180000.0, 1e-9);

        var code = r.Apps.Single(a => a.Name == "Code");
        (code.Seconds, code.DaysUsed, code.AverageSecondsPerDay).Should().Be((63000, 5, 12600));
        var disc = r.Apps.Single(a => a.Name == "Disc");
        (disc.Seconds, disc.DaysUsed, disc.AverageSecondsPerDay).Should().Be((16200, 3, 5400));
        r.Apps.Select(a => a.Name).Should().BeEquivalentTo(["Chrome", "Code", "Disc", ZedName, "Mus"]);
    }

    [Fact]
    public void Detail_lines_have_first_tracked_best_month_and_longest_session()
    {
        var r = Run();
        var chrome = r.Apps.Single(a => a.Name == "Chrome");
        chrome.FirstTracked.Should().Be(new DateOnly(2026, 2, 2));
        chrome.BestMonth.Should().Be(new AllBestMonth(2026, 2, 61200));
        chrome.LongestSessionSeconds.Should().Be(36000);

        var code = r.Apps.Single(a => a.Name == "Code");
        (code.FirstTracked, code.BestMonth, code.LongestSessionSeconds)
            .Should().Be((new DateOnly(2026, 2, 5), new AllBestMonth(2026, 3, 55800), 28800L));
        var disc = r.Apps.Single(a => a.Name == "Disc");
        (disc.BestMonth, disc.LongestSessionSeconds).Should().Be((new AllBestMonth(2026, 3, 9000), 7200L));
        r.Apps.Single(a => a.Name == "Mus").FirstTracked.Should().Be(new DateOnly(2026, 3, 11));
    }

    [Fact]
    public void Totals_row_excludes_retired_apps()
    {
        var r = Run();
        r.Totals.Seconds.Should().Be(242400);
        r.Totals.DaysUsed.Should().Be(13);
        r.Totals.AverageSecondsPerDay.Should().BeApproximately(242400 / 13.0, 1e-9);
        r.Apps.Sum(a => a.Share).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Retired_rows_are_listed_with_their_own_numbers_and_stopped_date()
    {
        var r = Run();
        var old = r.Retired.Should().ContainSingle().Subject;
        old.Name.Should().Be("Old");
        (old.Seconds, old.DaysUsed, old.AverageSecondsPerDay).Should().Be((82800, 1, 82800));
        old.StoppedDate.Should().Be(new DateOnly(2026, 3, 1));
        r.Apps.Should().NotContain(a => a.Name == "Old");
        (r.Apps.Count, r.Retired.Count).Should().Be((5, 1));
    }

    [Fact]
    public void A_tracked_alias_of_a_retired_main_app_is_its_own_row_not_part_of_the_retired_one()
    {
        _f.Exec("INSERT INTO apps(id, canonical_id, exe_path, display_name, added_utc) VALUES (901, (SELECT id FROM apps WHERE display_name = 'Old'), 'old2.exe', 'Old Beta', 0)");
        _f.Session(901, U(2026, 3, 11, 9), U(2026, 3, 11, 10));
        var r = Run();
        r.Apps.Single(a => a.Name == "Old Beta").Seconds.Should().Be(3600);
        r.Retired.Single().Seconds.Should().Be(82800);
    }

    [Fact]
    public void Merged_apps_are_one_row_under_the_canonical_app()
    {
        _f.Exec("INSERT INTO apps(id, canonical_id, exe_path, display_name, added_utc) VALUES (900, " + _f.Chrome + ", 'c:\\chrome-beta.exe', 'Chrome Beta', 0)");
        _f.Session(900, U(2026, 3, 9, 8), U(2026, 3, 9, 9));
        var r = Run();
        r.Apps.Should().NotContain(a => a.Name == "Chrome Beta");
        var chrome = r.Apps.Single(a => a.Name == "Chrome");
        (chrome.Seconds, chrome.DaysUsed).Should().Be((102000, 9));
        r.Totals.Seconds.Should().Be(246000);
        r.Totals.DaysUsed.Should().Be(14);
    }

    [Fact]
    public void An_open_session_counts_to_now_only_while_the_app_is_running()
    {
        Run(running: [_f.Chrome]).Apps.Single(a => a.Name == "Chrome").Seconds.Should().Be(98400);
        // Not running: capped at the heartbeat (14:19 instead of 14:20) = 60 s less.
        Run(running: []).Apps.Single(a => a.Name == "Chrome").Seconds.Should().Be(98340);
    }

    [Fact]
    public void Nothing_tracked_gives_zero_rows_and_zero_totals()
    {
        _f.Exec("DELETE FROM sessions");
        var r = Run();
        r.Totals.Should().Be(new AllTotals(0, 0, 0));
        r.Apps.Should().HaveCount(5).And.OnlyContain(a => a.Seconds == 0 && a.Share == 0 && a.FirstTracked == null && a.BestMonth == null);
    }
}
