using TimeSink.Core.Backup;

namespace TimeSink.Tests.Tracking;

public sealed class DiscardOpenSessionsTests : IDisposable
{
    private readonly Rig _r = new();
    public void Dispose() => _r.Dispose();

    [Fact]
    public void After_history_is_deleted_underneath_it_the_engine_forgets_open_sessions_and_starts_fresh()
    {
        _r.Procs.Running.Add(_r.Notepad(10, startSeconds: 0));
        _r.TickEvery5(0, 60);
        _r.Clock.UtcNow = Rig.At(62);
        HistoryDeleter.DeleteAll(_r.DbPath, "DELETE");

        _r.Engine.DiscardOpenSessions(Rig.At(62));
        _r.TickEvery5(65, 100);
        _r.Procs.Running.Clear();
        _r.Tick(105);

        var s = _r.Sessions.Should().ContainSingle().Subject;
        s.StartUtc.Should().Be(Rig.At(62).ToUnixTimeSeconds());
        _r.Daily().Values.Sum().Should().Be(105 - 62);
    }
}
