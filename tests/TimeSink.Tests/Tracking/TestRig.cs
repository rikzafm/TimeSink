using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Tracking;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}

public sealed class FakeProcesses : IProcessProvider
{
    public List<ProcInfo> Running { get; } = new();
    public IReadOnlyList<ProcInfo> Snapshot() => Running.ToList();
}

public sealed class FakePower : IPowerEvents
{
    public event Action<DateTimeOffset>? Suspend, Resume, Lock, Unlock, Shutdown;
    public void RaiseSuspend(DateTimeOffset t) => Suspend?.Invoke(t);
    public void RaiseResume(DateTimeOffset t) => Resume?.Invoke(t);
    public void RaiseLock(DateTimeOffset t) => Lock?.Invoke(t);
    public void RaiseUnlock(DateTimeOffset t) => Unlock?.Invoke(t);
    public void RaiseShutdown(DateTimeOffset t) => Shutdown?.Invoke(t);
}

/// <summary>Engine + temp SQLite + fakes. Time is UTC; the zone defaults to UTC unless given.</summary>
public sealed class Rig : IDisposable
{
    public static readonly DateTimeOffset Base = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    public const string NotepadPath = @"c:\windows\notepad.exe";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    public Db Db { get; }
    public SqliteSessionStore Store { get; }
    public FakeClock Clock { get; } = new() { UtcNow = Base };
    public FakeProcesses Procs { get; } = new();
    public FakePower Power { get; } = new();
    public TrackingSettings Settings { get; } = new() { MinSessionSeconds = 15 };
    public TimeZoneInfo Zone { get; }
    public TrackingEngine Engine { get; private set; }
    public UsageAccumulator Usage { get; }
    public long NotepadId { get; }

    public Rig(TimeZoneInfo? zone = null, long addedUtc = 0)
    {
        Zone = zone ?? TimeZoneInfo.Utc;
        Directory.CreateDirectory(_dir);
        Db = Db.Open(Path.Combine(_dir, "t.db"));
        Store = new SqliteSessionStore(Db);
        Usage = new UsageAccumulator(Store, Zone);
        NotepadId = Db.InsertApp(NotepadPath, "Notepad", addedUtc);
        Engine = NewEngine();
    }

    public TrackingEngine NewEngine()
    {
        Engine = new TrackingEngine(Store, Procs, Clock, Settings, Zone);
        Engine.Attach(Power);
        return Engine;
    }

    public static DateTimeOffset At(int seconds) => Base.AddSeconds(seconds);

    public ProcInfo Notepad(int pid, int startSeconds = -100000) =>
        new(pid, "notepad.exe", NotepadPath, At(startSeconds));

    public void Tick(int seconds) { Clock.UtcNow = At(seconds); Engine.OnTick(At(seconds)); }

    /// <summary>Tick every 5 s from..to inclusive.</summary>
    public void TickEvery5(int from, int to) { for (var s = from; s <= to; s += 5) Tick(s); }

    public IReadOnlyList<SessionRecord> Sessions => Store.GetAllSessions();
    public Dictionary<string, long> Daily(long? appId = null) =>
        Store.GetDailyUsage().Where(r => appId is null || r.AppId == appId).ToDictionary(r => r.Day, r => r.Seconds);

    public void Dispose()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
