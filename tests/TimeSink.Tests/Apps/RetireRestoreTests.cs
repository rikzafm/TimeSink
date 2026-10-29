using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;
using TimeSink.Tests.Tracking;
using TimeSink.Tests.Usage;

namespace TimeSink.Tests.Apps;

public sealed class RetireRestoreTests
{
    [Fact]
    public void Retiring_keeps_history_but_removes_the_app_from_totals_and_tracking_and_lists_it_as_retired()
    {
        using var f = new WeekFixture();
        var a = f.AddApp("Alpha");
        var b = f.AddApp("Beta");
        f.Session(a, WeekFixture.Unix(2026, 3, 11, 9), WeekFixture.Unix(2026, 3, 11, 10));
        f.Session(b, WeekFixture.Unix(2026, 3, 11, 11), WeekFixture.Unix(2026, 3, 11, 12));

        using (var lib = AppLibrary.Open(f.DbPath))
            lib.Retire(a, 1_000);

        Q.Today(f.DbPath, WeekFixture.Now, TimeZoneInfo.Utc).Apps.Select(r => r.Name).Should().Equal("Beta");
        using var db = Db.Open(f.DbPath);
        new SqliteSessionStore(db).GetTrackedApps().Select(t => t.Id).Should().Equal(b);
        using var lib2 = AppLibrary.Open(f.DbPath);
        lib2.Retired().Should().ContainSingle().Which.Should().Be(new RetiredApp(a, @"c:\alpha.exe", "Alpha", 1_000));
        lib2.Library().Select(x => (x.DisplayName, x.State)).Should().BeEquivalentTo([("Alpha", AppState.Retired), ("Beta", AppState.Tracked)]);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sessions WHERE app_id = " + a;
        ((long)cmd.ExecuteScalar()!).Should().Be(1); // history kept
    }

    [Fact]
    public void Restoring_returns_the_app_to_totals_and_tracking_counting_from_now()
    {
        using var f = new WeekFixture();
        var a = f.AddApp("Alpha");
        f.Session(a, WeekFixture.Unix(2026, 3, 11, 9), WeekFixture.Unix(2026, 3, 11, 10));
        using var lib = AppLibrary.Open(f.DbPath);
        lib.Retire(a, 1_000);
        lib.Restore(a, 2_000);

        lib.Retired().Should().BeEmpty();
        Q.Today(f.DbPath, WeekFixture.Now, TimeZoneInfo.Utc).Apps.Select(r => (r.Name, r.Seconds)).Should().Equal(("Alpha", 3600L));
        using var db = Db.Open(f.DbPath);
        new SqliteSessionStore(db).GetTrackedApps().Single().AddedUtc.Should().Be(2_000);
    }

    [Fact]
    public void Retiring_a_running_app_closes_its_open_session_as_removed_when_the_engine_reloads()
    {
        using var r = new Rig();
        r.Procs.Running.Add(r.Notepad(7));
        r.TickEvery5(0, 40);
        r.Sessions.Should().ContainSingle().Which.EndUtc.Should().BeNull();

        using (var lib = AppLibrary.Open(r.DbPath)) lib.Retire(r.NotepadId, Rig.At(41).ToUnixTimeSeconds());
        r.Clock.UtcNow = Rig.At(42);
        r.Engine.ReloadTrackedApps();

        r.Sessions.Single().EndReason.Should().Be("removed");
    }
}
