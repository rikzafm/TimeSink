using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

/// <summary>Uses <see cref="MonthFixture"/> (now = 2026-03-11 14:20Z; Chrome has an open session since 11:00, last seen 14:19; Old is retired).</summary>
public sealed class UsageLoaderTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private static readonly long NowUtc = Now.ToUnixTimeSeconds();
    private static readonly long Day = U(2026, 3, 11, 0);

    private AppGroups Apps(bool includeRetired = false)
    {
        using var c = UsageDb.OpenReadOnly(_f.DbPath);
        return AppGroups.Load(c, includeRetired);
    }

    private List<UsageSeg> Segments(IReadOnlyCollection<long>? running, AppGroups apps)
    {
        using var c = UsageDb.OpenReadOnly(_f.DbPath);
        return UsageLoader.LoadSegments(c, Day, NowUtc, apps, running);
    }

    [Fact]
    public void Retired_apps_are_left_out_unless_asked_for()
    {
        Apps().Values.Should().NotContain(a => a.Name == "Old");

        var old = Apps(includeRetired: true).Values.Single(a => a.Name == "Old");
        old.Retired.Should().BeTrue();
        old.RetiredUtc.Should().Be(U(2026, 3, 1, 0));
        Apps(includeRetired: true)[_f.Chrome].Retired.Should().BeFalse();
    }

    [Theory]
    [InlineData("sleep", true)]
    [InlineData("shutdown", true)]
    [InlineData("exit", false)]
    public void Sleep_and_shutdown_closes_are_flagged_as_the_pc_being_off(string reason, bool off)
    {
        _f.Exec($"INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES ({_f.Chrome}, {Day + 100}, {Day + 200}, {Day + 200}, '{reason}')");
        Segments(null, Apps()).Single(s => s.Start == Day + 100).Sleep.Should().Be(off);
    }

    [Fact]
    public void An_open_session_counts_to_now_when_no_running_list_is_given()
    {
        var seg = Segments(null, Apps()).Single(s => s.AppId == _f.Chrome);
        (seg.End, seg.Open).Should().Be((NowUtc, true));
    }

    [Fact]
    public void An_open_session_counts_to_now_when_its_app_is_running()
    {
        var seg = Segments([_f.Chrome], Apps()).Single(s => s.AppId == _f.Chrome);
        (seg.End, seg.Open).Should().Be((NowUtc, true));
    }

    [Fact]
    public void An_open_session_of_an_app_that_is_not_running_ends_at_its_last_heartbeat()
    {
        var seg = Segments([], Apps()).Single(s => s.AppId == _f.Chrome);
        (seg.End, seg.Open).Should().Be((U(2026, 3, 11, 14, 19), false));
    }

    [Fact]
    public void A_session_that_ended_by_sleep_is_marked()
    {
        _f.Exec($"INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES({_f.Mus}, {U(2026, 3, 11, 9)}, {U(2026, 3, 11, 10)}, {U(2026, 3, 11, 10)}, 'sleep')");
        Segments(null, Apps()).Should().ContainSingle(s => s.AppId == _f.Mus && s.Sleep);
    }

    [Fact]
    public void Sessions_of_retired_apps_come_back_only_when_the_apps_were_loaded_with_them()
    {
        _f.Session(_f.Chrome, U(2026, 3, 11, 1), U(2026, 3, 11, 2));
        var old = Apps(includeRetired: true).Values.Single(a => a.Name == "Old");
        _f.Session(old.Id, U(2026, 3, 11, 3), U(2026, 3, 11, 4));

        Segments(null, Apps()).Should().NotContain(s => s.AppId == old.Id);
        Segments(null, Apps(includeRetired: true)).Should().Contain(s => s.AppId == old.Id);
    }
}
