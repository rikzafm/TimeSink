using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Tracking;

public sealed class TrackingEngineTests : IDisposable
{
    private readonly Rig _r = new();
    public void Dispose() => _r.Dispose();

    private static long U(int seconds) => Rig.At(seconds).ToUnixTimeSeconds();
    private const string Today = "2026-03-10";

    // ---- basic lifecycle -------------------------------------------------

    [Fact]
    public void A_session_opens_when_the_process_appears_and_closes_with_the_exact_duration_when_it_exits()
    {
        _r.Procs.Running.Add(_r.Notepad(10, startSeconds: 20));
        _r.TickEvery5(0, 60);
        _r.Procs.Running.Clear();
        _r.Tick(65);

        var s = _r.Sessions.Should().ContainSingle().Subject;
        s.AppId.Should().Be(_r.NotepadId);
        s.StartUtc.Should().Be(U(20));
        s.EndUtc.Should().Be(U(65));
        s.EndReason.Should().Be("exit");
        s.Pid.Should().Be(10);
        s.ProcStartUtc.Should().Be(U(20));
        _r.Daily().Should().Equal(new Dictionary<string, long> { [Today] = 45 });
    }

    [Fact]
    public void Several_processes_of_one_app_are_one_session_that_outlives_the_first_process()
    {
        _r.Procs.Running.Add(_r.Notepad(1, startSeconds: 0));
        _r.Procs.Running.Add(_r.Notepad(2, startSeconds: 10));
        _r.Procs.Running.Add(_r.Notepad(3, startSeconds: 15));
        _r.TickEvery5(0, 30);
        _r.Procs.Running.RemoveAll(p => p.Pid == 1);
        _r.TickEvery5(35, 60);
        _r.Procs.Running.Clear();
        _r.Tick(65);

        var s = _r.Sessions.Should().ContainSingle().Subject;
        (s.StartUtc, s.EndUtc).Should().Be((U(0), (long?)U(65)));
        _r.Daily()[Today].Should().Be(65);
    }

    [Fact]
    public void Time_before_the_engine_started_is_not_back_filled()
    {
        _r.Procs.Running.Add(_r.Notepad(1)); // started long ago
        _r.TickEvery5(0, 40);
        _r.Sessions.Single().StartUtc.Should().Be(U(0));
    }

    [Fact]
    public void Time_before_the_app_was_added_is_not_counted()
    {
        using var r = new Rig(addedUtc: U(30));
        r.Procs.Running.Add(r.Notepad(1));
        r.TickEvery5(0, 60);
        r.Sessions.Single().StartUtc.Should().Be(U(30));
    }

    [Fact]
    public void Other_programs_and_other_paths_are_ignored_but_path_case_and_pathless_unique_names_match()
    {
        _r.Settings.MinSessionSeconds = 5;
        _r.Procs.Running.Add(new ProcInfo(1, "notepad.exe", @"c:\other\notepad.exe", Rig.At(0)));
        _r.Procs.Running.Add(new ProcInfo(2, "calc.exe", @"c:\windows\calc.exe", Rig.At(0)));
        _r.Tick(0);
        _r.Sessions.Should().BeEmpty();

        _r.Procs.Running.Add(new ProcInfo(3, "Notepad.EXE", @"C:\Windows\Notepad.exe", Rig.At(0)));
        _r.Tick(5);
        _r.Sessions.Should().ContainSingle();
        _r.Procs.Running.Clear();
        _r.Tick(10);

        _r.Procs.Running.Add(new ProcInfo(4, "notepad.exe", null, Rig.At(10)));
        _r.Tick(15);
        _r.Sessions.Should().HaveCount(2);
    }

    [Fact]
    public void Heartbeat_every_30_seconds_updates_last_seen_and_accumulates_usage()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 25);
        _r.Sessions.Single().LastSeenUtc.Should().Be(U(0));
        _r.Daily().Should().BeEmpty();

        _r.TickEvery5(30, 55);
        _r.Sessions.Single().LastSeenUtc.Should().Be(U(30));
        _r.Daily()[Today].Should().Be(30);
    }

    // ---- midnight / DST --------------------------------------------------

    [Fact]
    public void A_session_crossing_local_midnight_splits_into_two_days()
    {
        _r.Procs.Running.Add(_r.Notepad(1, startSeconds: 43190));
        _r.TickEvery5(43190, 43210);
        _r.Procs.Running.Clear();
        _r.Tick(43215); // Base + 12 h = 2026-03-11 00:00Z

        _r.Daily().Should().Equal(new Dictionary<string, long> { ["2026-03-10"] = 10, ["2026-03-11"] = 15 });
    }

    [Fact]
    public void Midnight_split_uses_the_local_zone_across_a_DST_change()
    {
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        using var r = new Rig(ny);
        // 2026-03-09 00:00 EDT = 04:00Z. Base is 2026-03-10 12:00Z, so that is Base - 32 h.
        const int midnight = -32 * 3600;
        r.Clock.UtcNow = Rig.At(midnight - 20);
        r.NewEngine();
        r.Procs.Running.Add(r.Notepad(1, midnight - 20));
        r.TickEvery5(midnight - 20, midnight + 10);
        r.Procs.Running.Clear();
        r.Tick(midnight + 15);

        r.Daily().Should().Equal(new Dictionary<string, long> { ["2026-03-08"] = 20, ["2026-03-09"] = 15 });
    }

    // ---- sleep / lock / shutdown -----------------------------------------

    [Fact]
    public void Suspend_closes_at_the_suspend_time_and_resume_opens_a_new_session()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Power.RaiseSuspend(Rig.At(42));
        _r.Sessions.Single().Should().Match<SessionRecord>(s => s.EndUtc == U(42) && s.EndReason == "sleep");

        _r.Clock.UtcNow = Rig.At(300);
        _r.Power.RaiseResume(Rig.At(300));
        _r.TickEvery5(300, 340);

        _r.Sessions.Should().HaveCount(2);
        var second = _r.Sessions[1];
        (second.StartUtc, second.EndUtc).Should().Be((U(300), (long?)null));
        _r.Daily()[Today].Should().Be(42 + 30); // 0..42 and the heartbeat of the second session 300..330
    }

    [Fact]
    public void A_tick_gap_over_20_seconds_behaves_like_a_missed_sleep()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Tick(300); // no suspend event was delivered

        var first = _r.Sessions[0];
        (first.EndUtc, first.EndReason).Should().Be((U(40), "sleep"));
        _r.Sessions[1].StartUtc.Should().Be(U(300));
        _r.Daily()[Today].Should().Be(40);
    }

    [Fact]
    public void With_pause_on_sleep_off_sleep_does_not_split_the_session()
    {
        _r.Settings.PauseOnSleep = false;
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Power.RaiseSuspend(Rig.At(42));
        _r.Power.RaiseResume(Rig.At(300));
        _r.Tick(300);
        _r.Sessions.Should().ContainSingle().Which.EndUtc.Should().BeNull();
    }

    [Fact]
    public void Lock_closes_and_unlock_reopens_when_not_counting_while_locked()
    {
        _r.Settings.CountWhenLocked = false;
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Power.RaiseLock(Rig.At(42));
        _r.TickEvery5(45, 95);
        _r.Sessions.Should().ContainSingle().Which.EndReason.Should().Be("sleep");

        _r.Clock.UtcNow = Rig.At(100);
        _r.Power.RaiseUnlock(Rig.At(100));
        _r.Sessions.Should().HaveCount(2);
        _r.Sessions[1].StartUtc.Should().Be(U(100));
    }

    [Fact]
    public void Lock_is_ignored_when_counting_while_locked()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Power.RaiseLock(Rig.At(42));
        _r.TickEvery5(45, 80);
        _r.Power.RaiseUnlock(Rig.At(82));
        _r.Sessions.Should().ContainSingle().Which.EndUtc.Should().BeNull();
    }

    [Fact]
    public void Shutdown_closes_everything_with_the_shutdown_reason()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Power.RaiseShutdown(Rig.At(44));
        _r.Sessions.Single().Should().Match<SessionRecord>(s => s.EndUtc == U(44) && s.EndReason == "shutdown");
        _r.Daily()[Today].Should().Be(44);
    }

    // ---- crash recovery --------------------------------------------------

    private void CrashAndRestartAt(int nowSeconds)
    {
        _r.Procs.Running.Add(_r.Notepad(7, startSeconds: -50));
        _r.TickEvery5(0, 35); // last heartbeat at 30
        _r.Clock.UtcNow = Rig.At(nowSeconds);
        _r.NewEngine(); // the old engine is gone
    }

    [Fact]
    public void Recovery_continues_a_session_whose_process_is_alive_and_heartbeat_is_fresh()
    {
        CrashAndRestartAt(150); // exactly 120 s after last_seen
        _r.Engine.RecoverFromCrash(Rig.At(150));
        _r.TickEvery5(150, 180);
        _r.Procs.Running.Clear();
        _r.Tick(185);

        var s = _r.Sessions.Should().ContainSingle().Subject;
        (s.StartUtc, s.EndUtc, s.EndReason).Should().Be((U(0), (long?)U(185), "exit"));
        _r.Daily()[Today].Should().Be(185);
    }

    [Fact]
    public void Recovery_closes_at_last_seen_when_the_heartbeat_is_stale()
    {
        CrashAndRestartAt(151);
        _r.Engine.RecoverFromCrash(Rig.At(151));
        var s = _r.Sessions.Single();
        (s.EndUtc, s.EndReason).Should().Be((U(30), "crash"));
        _r.Daily()[Today].Should().Be(30);
    }

    [Fact]
    public void Recovery_closes_at_last_seen_when_the_process_is_gone_or_the_pid_was_reused()
    {
        CrashAndRestartAt(60);
        _r.Procs.Running.Clear();
        _r.Procs.Running.Add(_r.Notepad(7, startSeconds: 40)); // same pid, different creation time
        _r.Engine.RecoverFromCrash(Rig.At(60));
        var s = _r.Sessions.Single();
        (s.EndUtc, s.EndReason).Should().Be((U(30), "crash"));
    }

    [Fact]
    public void A_recovered_session_that_was_too_short_is_removed()
    {
        _r.Settings.MinSessionSeconds = 30;
        _r.Procs.Running.Add(_r.Notepad(7, startSeconds: 10));
        _r.TickEvery5(0, 30); // session 10.., heartbeat 30 -> 20 s counted
        _r.NewEngine();
        _r.Procs.Running.Clear();
        _r.Engine.RecoverFromCrash(Rig.At(60));
        _r.Sessions.Should().BeEmpty();
        _r.Daily().Should().BeEmpty();
    }

    // ---- minimum session, clock, removal ---------------------------------

    [Fact]
    public void Short_sessions_are_removed_and_their_usage_subtracted()
    {
        _r.Settings.MinSessionSeconds = 30;
        _r.Procs.Running.Add(_r.Notepad(1, startSeconds: 10));
        _r.TickEvery5(0, 30);                 // heartbeat accumulated 10..30
        _r.Daily()[Today].Should().Be(20);
        _r.Procs.Running.Clear();
        _r.Tick(35);                          // 25 s < 30 s

        _r.Sessions.Should().BeEmpty();
        _r.Daily().Should().BeEmpty();
    }

    [Fact]
    public void A_session_of_exactly_the_minimum_is_kept()
    {
        _r.Procs.Running.Add(_r.Notepad(1, startSeconds: 10));
        _r.TickEvery5(0, 20);
        _r.Procs.Running.Clear();
        _r.Tick(25);
        _r.Sessions.Should().ContainSingle();
        _r.Daily()[Today].Should().Be(15);
    }

    [Fact]
    public void The_clock_moving_backwards_never_produces_negative_durations()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Tick(20); // clock set back
        _r.TickEvery5(25, 45);
        _r.Procs.Running.Clear();
        _r.Tick(50);

        _r.Sessions.Should().HaveCount(2);
        _r.Sessions.Should().OnlyContain(s => s.EndUtc >= s.StartUtc && s.LastSeenUtc >= s.StartUtc);
        _r.Sessions[0].EndUtc.Should().Be(U(40));
        _r.Daily().Values.Should().OnlyContain(v => v > 0);
        _r.Daily()[Today].Should().Be(40 + 30);
    }

    [Fact]
    public void Retiring_a_running_app_closes_its_session_as_removed()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.Db.SetAppState(_r.NotepadId, "retired", U(41));
        _r.Clock.UtcNow = Rig.At(42);
        _r.Engine.ReloadTrackedApps();

        _r.Sessions.Single().Should().Match<SessionRecord>(s => s.EndUtc == U(42) && s.EndReason == "removed");
        _r.TickEvery5(45, 80);
        _r.Sessions.Should().ContainSingle();
    }

    [Fact]
    public void An_app_added_while_running_is_picked_up_after_reload()
    {
        var calc = _r.Db.InsertApp(@"c:\windows\calc.exe", "Calc", U(0));
        _r.Procs.Running.Add(new ProcInfo(9, "calc.exe", @"c:\windows\calc.exe", Rig.At(-10)));
        _r.Tick(0);
        _r.Sessions.Should().BeEmpty();
        _r.Engine.ReloadTrackedApps();
        _r.Tick(5);
        _r.Sessions.Single().AppId.Should().Be(calc);
    }
}
