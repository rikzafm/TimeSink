using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

public sealed class TodayQueryTests : IDisposable
{
    private readonly TodayFixture _f = new();
    public void Dispose() => _f.Dispose();

    private TodayResult Run(IReadOnlyCollection<long>? running = null) =>
        TodayQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, running);

    [Fact]
    public void Combined_total_sums_tracked_apps_clipped_to_today_with_open_session_up_to_now()
    {
        var r = Run();
        r.Date.Should().Be(new DateOnly(2026, 3, 11));
        r.TotalSeconds.Should().Be(15600 + 5400); // Chrome 1800+1800+12000, Code 3600+1800; retired Old excluded
        r.RunningCount.Should().Be(1);
        r.OpenSessionCount.Should().Be(1);
    }

    [Fact]
    public void An_open_session_the_agent_does_not_report_as_running_counts_only_to_its_heartbeat()
    {
        var r = Run(running: Array.Empty<long>());
        r.RunningCount.Should().Be(0);
        r.TotalSeconds.Should().Be(1800 + 1800 + (U(2026, 3, 11, 14, 19) - U(2026, 3, 11, 11)) + 5400);
    }

    [Fact]
    public void Apps_are_sorted_by_todays_time_with_status_fields()
    {
        var r = Run();
        r.Apps.Select(a => a.Name).Should().Equal("Chrome", "Code");
        var chrome = r.Apps[0];
        chrome.Seconds.Should().Be(15600);
        chrome.IsRunning.Should().BeTrue();
        chrome.RunningSinceUtc.Should().Be(U(2026, 3, 11, 11));
        var code = r.Apps[1];
        code.Seconds.Should().Be(5400);
        code.IsRunning.Should().BeFalse();
        code.LastClosedUtc.Should().Be(U(2026, 3, 11, 13, 30));
    }

    [Fact]
    public void Row_sessions_are_chronological_and_clipped_to_the_local_day()
    {
        var s = Run().Apps[0].Sessions;
        s.Select(x => (x.StartUtc, x.EndUtc, x.IsOpen)).Should().Equal(
            (U(2026, 3, 11, 0), U(2026, 3, 11, 0, 30), false),
            (U(2026, 3, 11, 9), U(2026, 3, 11, 9, 30), false),
            (U(2026, 3, 11, 11), U(2026, 3, 11, 14, 20), true));
    }

    [Fact]
    public void Hourly_buckets_count_app_seconds_per_hour_with_the_current_hour_partial()
    {
        var h = Run().Hours.Select(x => x.AppSeconds).ToArray();
        h.Length.Should().Be(24);
        h.Should().Equal(
            1800, 0, 0, 0, 0, 0, 0, 0, 1800, 3600, 0, 3600,
            3600, 5400, 1200, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void Typical_day_averages_the_same_weekday_over_eight_weeks_ignoring_days_before_tracking()
    {
        // Wednesdays 03-04 and 02-25 count (tracking began 2026-02-20); 02-18 and earlier do not.
        var r = Run();
        r.HasTypical.Should().BeTrue();
        r.Hours[10].TypicalAppSeconds.Should().Be(3600); // (3600 + 3600) / 2
        r.Hours[11].TypicalAppSeconds.Should().Be(1800); // (3600 + 0) / 2
        r.Hours[9].TypicalAppSeconds.Should().Be(0);
    }

    [Fact]
    public void Sleep_band_runs_from_a_sleep_close_to_the_next_session_start()
    {
        var b = Run().SleepBands;
        b.Should().ContainSingle();
        b[0].StartUtc.Should().Be(U(2026, 3, 11, 9, 30));
        b[0].EndUtc.Should().Be(U(2026, 3, 11, 11));
    }

    [Fact]
    public void Tiles_first_app_longest_session_and_vs_yesterday_by_now()
    {
        var r = Run();
        r.FirstApp!.AppName.Should().Be("Code");
        r.FirstApp.StartUtc.Should().Be(U(2026, 3, 11, 8, 30));
        r.Longest!.AppName.Should().Be("Chrome");
        r.Longest.Seconds.Should().Be(12000);
        // yesterday up to 14:20: Chrome 7200 + Code 1200
        r.VsYesterdaySeconds.Should().Be(21000 - 8400);
    }

    [Fact]
    public void Uptime_runs_from_the_first_session_to_now_minus_sleep_when_sign_in_is_unknown()
    {
        var r = Run();
        r.UptimeSeconds.Should().Be(U(2026, 3, 11, 14, 20) - U(2026, 3, 11, 0) - 5400); // sleep 9:30-11:00 excluded
        r.TotalSeconds.Should().NotBe(r.UptimeSeconds); // combined app time is a separate figure
    }

    [Fact]
    public void Uptime_is_per_day_so_a_later_sign_in_does_not_shorten_it()
    {
        // The day's first activity is the 00:00 carry-over, so a mid-day sign-in (restart) or one before midnight changes nothing.
        var expected = U(2026, 3, 11, 14, 20) - U(2026, 3, 11, 0) - 5400;
        TodayQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, null, null, U(2026, 3, 11, 8)).UptimeSeconds.Should().Be(expected);
        TodayQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, null, null, U(2026, 3, 9, 8)).UptimeSeconds.Should().Be(expected);
    }

    [Fact]
    public void Longest_tile_carries_the_app_id_for_its_icon()
    {
        Run().Longest!.AppId.Should().Be(Run().Apps[0].AppId);
    }

    [Fact]
    public void Seven_day_average_uses_only_days_since_the_app_was_added()
    {
        var r = Run();
        // Chrome: prior 7 days 03-04..03-10 all eligible. 03-04 7200 + 03-10 (8:00-10:00 + 23:00-24:00) 10800 = 18000 / 7.
        r.Apps[0].SevenDayDays.Should().Be(7);
        r.Apps[0].SevenDayAverageSeconds.Should().BeApproximately(18000 / 7.0, 1e-9);
        // Code: added 03-08, so 03-08..03-10 = 3 days; 7200 / 3.
        r.Apps[1].SevenDayDays.Should().Be(3);
        r.Apps[1].SevenDayAverageSeconds.Should().BeApproximately(2400, 1e-9);
    }

    [Fact]
    public void Merged_apps_are_one_row_under_the_canonical_app()
    {
        _f.Exec($"UPDATE apps SET canonical_id = {_f.Chrome} WHERE id = {_f.Code}");
        var r = Run();
        r.Apps.Should().ContainSingle();
        r.Apps[0].Name.Should().Be("Chrome");
        r.Apps[0].Seconds.Should().Be(21000);
    }

    [Fact]
    public void An_empty_database_gives_a_zero_result_without_typical_data()
    {
        var empty = Path.Combine(Path.GetTempPath(), "timesink-empty-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (TimeSink.Core.Data.Db.Open(empty)) { }
            var r = TodayQuery.Run(empty, Now, TimeZoneInfo.Utc, null);
            r.TotalSeconds.Should().Be(0);
            r.Apps.Should().BeEmpty();
            r.HasTypical.Should().BeFalse();
            r.FirstApp.Should().BeNull();
            r.Longest.Should().BeNull();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(empty)!, Path.GetFileName(empty) + "*")) try { File.Delete(f); } catch { }
        }
    }
}
