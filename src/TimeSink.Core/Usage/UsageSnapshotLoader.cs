using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Core.Usage;

/// <summary>
/// What a usage query needs besides the database: the instant it is asked at, the zone, the running apps and, only when
/// a query reads them, the sign-in time and the week start. Those two are looked up on first use, off the UI thread.
/// </summary>
public sealed class UsageContext
{
    private readonly Lazy<long?> _signInUtc;
    private readonly Lazy<WeekStart> _weekStart;

    public UsageContext(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running, Func<long?> signInUtc, Func<WeekStart> weekStart)
    {
        Now = now;
        Zone = zone;
        Running = running;
        _signInUtc = new Lazy<long?>(signInUtc, LazyThreadSafetyMode.None);
        _weekStart = new Lazy<WeekStart>(weekStart, LazyThreadSafetyMode.None);
    }

    public DateTimeOffset Now { get; }
    public TimeZoneInfo Zone { get; }

    /// <summary>The apps running right now, or null before the Agent has said (queries then trust the database).</summary>
    public IReadOnlyCollection<long>? Running { get; }

    /// <summary>When this Windows session started, or null when unknown.</summary>
    public long? SignInUtc => _signInUtc.Value;

    public WeekStart WeekStart => _weekStart.Value;
    public DayOfWeek WeekStartDay => WeekStart.ToDayOfWeek();
}

/// <summary>
/// Runs a usage query off the caller's thread and hands the result back only if it is still the latest one asked for and
/// the loader is still started. A missing or locked database is not an error: the result is simply dropped.
/// The caller's context (the UI thread) resumes after the query, so <c>apply</c> runs where <see cref="Refresh{T}"/> was awaited.
/// </summary>
public sealed class UsageSnapshotLoader
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;
    private readonly Func<IReadOnlyCollection<long>?> _running;
    private readonly Func<long?> _signInUtc;
    private readonly Func<WeekStart> _weekStart;
    private int _seq;
    private volatile bool _started;

    public UsageSnapshotLoader(Func<DateTimeOffset> clock, TimeZoneInfo zone, Func<IReadOnlyCollection<long>?> running,
        Func<long?> signInUtc, Func<WeekStart> weekStart)
    {
        _clock = clock;
        _zone = zone;
        _running = running;
        _signInUtc = signInUtc;
        _weekStart = weekStart;
    }

    /// <summary>A loader on the real clock and local zone that reads the week start from the settings table on demand.</summary>
    public static UsageSnapshotLoader ForDb(string dbPath, Func<IReadOnlyCollection<long>?> running, Func<long?> signInUtc) =>
        new(() => DateTimeOffset.UtcNow, TimeZoneInfo.Local, running, signInUtc,
            () => WeekStarts.Parse(SettingsStore.Get(dbPath, WeekStarts.SettingKey)));

    public bool Started => _started;

    public void Start() => _started = true;

    public void Stop() => _started = false;

    /// <summary>
    /// Asks <paramref name="query"/> on a pool thread with a context captured now. True when <paramref name="apply"/> ran;
    /// false when the loader is stopped, the database is missing or locked, or a newer refresh (or a stop) overtook this one.
    /// </summary>
    public async Task<bool> Refresh<T>(Func<UsageContext, T> query, Action<T> apply)
    {
        if (!_started) return false;
        var seq = Interlocked.Increment(ref _seq);
        var context = new UsageContext(_clock(), _zone, _running(), _signInUtc, _weekStart);
        T result;
        try { result = await Task.Run(() => query(context)); }
        catch (Exception ex) when (ex is SqliteException or IOException) { return false; } // no DB yet
        if (seq != Volatile.Read(ref _seq) || !_started) return false;
        apply(result);
        return true;
    }
}
