using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Tracking;

public sealed class TrackingEngineTests : IDisposable
{
    private readonly Rig _r = new();
    public void Dispose() => _r.Dispose();

    private static long U(int seconds) => Rig.At(seconds).ToUnixTimeSeconds();
    private const string Today = "2026-03-10";

    private static void RunFixture(string file, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(file);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    // ---- basic lifecycle -------------------------------------------------

    [Fact]
    public void A_session_opens_when_the_process_appears_and_closes_with_the_exact_duration_when_it_exits() =>
        RunFixture("single-session.json", "single-session-open-and-close");

    [Fact]
    public void Several_processes_of_one_app_are_one_session_that_outlives_the_first_process() =>
        RunFixture("single-session.json", "multi-process-coalescing");

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
    public void A_session_crossing_local_midnight_splits_into_two_days() =>
        RunFixture("midnight-and-dst.json", "midnight-split-utc");

    [Fact]
    public void Midnight_split_uses_the_local_zone_across_a_DST_change() =>
        RunFixture("midnight-and-dst.json", "midnight-split-dst-america-new-york");

    // ---- sleep / lock / shutdown -----------------------------------------

    [Fact]
    public void Suspend_closes_at_the_suspend_time_and_resume_opens_a_new_session() =>
        RunFixture("suspend-resume-and-power.json", "suspend-and-resume");

    [Fact]
    public void A_tick_gap_over_20_seconds_behaves_like_a_missed_sleep() =>
        RunFixture("suspend-resume-and-power.json", "tick-gap-fallback");

    [Fact]
    public void With_pause_on_sleep_off_sleep_does_not_split_the_session() =>
        RunFixture("suspend-resume-and-power.json", "pause-on-sleep-disabled");

    [Fact]
    public void Lock_closes_and_unlock_reopens_when_not_counting_while_locked() =>
        RunFixture("lock-and-unlock.json", "lock-splits-when-not-counting-locked");

    [Fact]
    public void Lock_is_ignored_when_counting_while_locked() =>
        RunFixture("lock-and-unlock.json", "lock-ignored-when-counting-locked");

    [Fact]
    public void Shutdown_closes_everything_with_the_shutdown_reason() =>
        RunFixture("suspend-resume-and-power.json", "shutdown-event");

    // ---- crash recovery --------------------------------------------------

    [Fact]
    public void Recovery_continues_a_session_whose_process_is_alive_and_heartbeat_is_fresh() =>
        RunFixture("crash-recovery.json", "recovery-continues-fresh-heartbeat");

    [Fact]
    public void Recovery_closes_at_last_seen_when_the_heartbeat_is_stale() =>
        RunFixture("crash-recovery.json", "recovery-closes-stale-heartbeat");

    [Fact]
    public void Recovery_closes_at_last_seen_when_the_process_is_gone_or_the_pid_was_reused() =>
        RunFixture("crash-recovery.json", "recovery-closes-on-pid-reuse");

    [Fact]
    public void A_recovered_session_that_was_too_short_is_removed() =>
        RunFixture("crash-recovery.json", "recovery-removes-short-session");

    // ---- minimum session, clock, removal ---------------------------------

    [Fact]
    public void Short_sessions_are_removed_and_their_usage_subtracted() =>
        RunFixture("min-session-and-clock.json", "short-session-removed-and-subtracted");

    [Fact]
    public void A_session_of_exactly_the_minimum_is_kept() =>
        RunFixture("min-session-and-clock.json", "exact-minimum-session-kept");

    [Fact]
    public void The_clock_moving_backwards_never_produces_negative_durations() =>
        RunFixture("min-session-and-clock.json", "clock-moving-backwards-safe");

    [Fact]
    public void Retiring_a_running_app_closes_its_session_as_removed()
    {
        _r.Procs.Running.Add(_r.Notepad(1));
        _r.TickEvery5(0, 40);
        _r.ExternalExec($"UPDATE apps SET state = 'retired', retired_utc = {U(41)} WHERE id = {_r.NotepadId}"); // as the UI does
        _r.Clock.UtcNow = Rig.At(42);
        _r.Engine.ReloadTrackedApps();

        _r.Sessions.Single().Should().Match<SessionRecord>(s => s.EndUtc == U(42) && s.EndReason == "removed");
        _r.TickEvery5(45, 80);
        _r.Sessions.Should().ContainSingle();
    }

    [Fact]
    public void An_app_added_while_running_is_picked_up_after_reload()
    {
        _r.ExternalExec(@"INSERT INTO apps (exe_path, display_name, added_utc) VALUES ('c:\windows\calc.exe', 'Calc', 0)");
        var calc = _r.Db.GetApps().Single(a => a.DisplayName == "Calc").Id;
        _r.Procs.Running.Add(new ProcInfo(9, "calc.exe", @"c:\windows\calc.exe", Rig.At(-10)));
        _r.Tick(0);
        _r.Sessions.Should().BeEmpty();
        _r.Engine.ReloadTrackedApps();
        _r.Tick(5);
        _r.Sessions.Single().AppId.Should().Be(calc);
    }

    [Fact]
    public void Reload_re_reads_the_app_list_only_when_another_connection_committed()
    {
        var store = new CountingStore(_r.Store);
        var engine = new TrackingEngine(store, _r.Procs, _r.Clock, _r.Settings, _r.Zone);
        var baseline = store.AppReads;

        engine.ReloadTrackedApps(); // first call: nothing known yet about the version it was built with
        engine.ReloadTrackedApps();
        engine.ReloadTrackedApps();
        store.AppReads.Should().BeLessThanOrEqualTo(baseline); // the constructor already read at this version

        _r.ExternalExec("UPDATE settings SET value = value WHERE 0"); // no change: no commit
        engine.ReloadTrackedApps();
        store.AppReads.Should().BeLessThanOrEqualTo(baseline);

        _r.ExternalExec("INSERT INTO settings (key, value) VALUES ('x', '1')"); // the UI committed
        engine.ReloadTrackedApps();
        store.AppReads.Should().Be(baseline + 1);
        engine.ReloadTrackedApps();
        store.AppReads.Should().Be(baseline + 1);
    }

    [Fact]
    public void A_store_that_cannot_tell_always_reloads()
    {
        var store = new CountingStore(_r.Store, versioned: false);
        var engine = new TrackingEngine(store, _r.Procs, _r.Clock, _r.Settings, _r.Zone);
        var baseline = store.AppReads;
        engine.ReloadTrackedApps();
        engine.ReloadTrackedApps();
        store.AppReads.Should().Be(baseline + 2);
    }

    private sealed class CountingStore(ISessionStore inner, bool versioned = true) : ISessionStore
    {
        public int AppReads { get; private set; }
        public long? DataVersion => versioned ? inner.DataVersion : null;
        public IReadOnlyList<TrackedApp> GetTrackedApps() { AppReads++; return inner.GetTrackedApps(); }
        public IReadOnlyList<SessionRecord> GetOpenSessions() => inner.GetOpenSessions();
        public IReadOnlyList<SessionRecord> GetAllSessions() => inner.GetAllSessions();
        public SessionRecord OpenSession(long appId, long startUtc, int? pid, long? procStartUtc) => inner.OpenSession(appId, startUtc, pid, procStartUtc);
        public void Heartbeat(long sessionId, long lastSeenUtc) => inner.Heartbeat(sessionId, lastSeenUtc);
        public void CloseSession(long sessionId, long endUtc, string reason) => inner.CloseSession(sessionId, endUtc, reason);
        public void DeleteSession(long sessionId) => inner.DeleteSession(sessionId);
        public void AddDailyUsage(long appId, string day, long deltaSeconds) => inner.AddDailyUsage(appId, day, deltaSeconds);
        public IReadOnlyList<DailyUsageRow> GetDailyUsage() => inner.GetDailyUsage();
        public void ReplaceDailyUsage(IReadOnlyCollection<DailyUsageRow> rows) => inner.ReplaceDailyUsage(rows);
    }
}
