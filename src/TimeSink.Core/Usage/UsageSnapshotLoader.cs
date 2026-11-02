using Microsoft.Data.Sqlite;
using TimeSink.Core.Devices;
using TimeSink.Core.Settings;

namespace TimeSink.Core.Usage;

/// <summary>
/// What a usage query needs besides the database: the instant it is asked at, the zone, the running apps and, only when
/// a query reads them, the sign-in time and the week start. Those two are looked up on first use, off the UI thread.
/// </summary>
public sealed class UsageContext
{
    private readonly Lazy<long?> _signInUtc;
    private readonly Lazy<WeekStart> _weekStart;

    public UsageContext(DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running, Func<long?> signInUtc, Func<WeekStart> weekStart, bool gamesOnly = false, string? dbPath = null, DeviceScope deviceScope = DeviceScope.This, string? localDeviceId = null)
    {
        DbPath = dbPath;
        Now = now;
        GamesOnly = gamesOnly;
        DeviceScope = deviceScope;
        LocalDeviceId = localDeviceId;
        Zone = zone;
        Running = running;
        _signInUtc = new Lazy<long?>(signInUtc, LazyThreadSafetyMode.None);
        _weekStart = new Lazy<WeekStart>(weekStart, LazyThreadSafetyMode.None);
    }

    /// <summary>The database the queries read, or null for a context built only to feed an in-memory source.</summary>
    public string? DbPath { get; }

    public DateTimeOffset Now { get; }

    /// <summary>True while the Games filter is on: queries then see only Game and Gacha apps.</summary>
    public bool GamesOnly { get; }

    /// <summary>The scope filter: This device only, or All synced devices.</summary>
    public DeviceScope DeviceScope { get; }

    /// <summary>The local device ID, or null when unknown/not set.</summary>
    public string? LocalDeviceId { get; }

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
    private readonly Func<bool> _gamesOnly;
    private readonly Func<DeviceScope> _deviceScope;
    private readonly Func<string?> _localDeviceId;
    private readonly string? _dbPath;
    private int _seq;
    private volatile bool _started;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UsageSnapshotLoader(Func<DateTimeOffset> clock, TimeZoneInfo zone, Func<IReadOnlyCollection<long>?> running,
        Func<long?> signInUtc, Func<WeekStart> weekStart, Func<bool>? gamesOnly = null, string? dbPath = null, Func<DeviceScope>? deviceScope = null, Func<string?>? localDeviceId = null)
    {
        _dbPath = dbPath;
        _gamesOnly = gamesOnly ?? (() => false);
        _deviceScope = deviceScope ?? (() => dbPath != null ? AppSettings.ForFile(dbPath).Get(Setting.DeviceScope) : DeviceScope.This);
        _localDeviceId = localDeviceId ?? (() => dbPath != null ? AppSettings.ForFile(dbPath).Get(Setting.DeviceId) : null);
        _clock = clock;
        _zone = zone;
        _running = running;
        _signInUtc = signInUtc;
        _weekStart = weekStart;
    }

    /// <summary>A loader on the real clock and local zone that reads the week start from the settings table on demand.</summary>
    public static UsageSnapshotLoader ForDb(string dbPath, Func<IReadOnlyCollection<long>?> running, Func<long?> signInUtc, Func<bool>? gamesOnly = null, Func<DeviceScope>? deviceScope = null, Func<string?>? localDeviceId = null) =>
        new(() => DateTimeOffset.UtcNow, TimeZoneInfo.Local, running, signInUtc,
            () => AppSettings.ForFile(dbPath).Get(Setting.WeekStartsOn), gamesOnly, dbPath, deviceScope, localDeviceId);

    /// <summary>The instant the next refresh will be asked at, on this loader's clock.</summary>
    public DateTimeOffset Now => _clock();

    /// <summary>The zone this loader cuts days in.</summary>
    public TimeZoneInfo Zone => _zone;

    /// <summary>The Local day of <see cref="Now"/> in <see cref="Zone"/>, for a view that needs it before its first result.</summary>
    public DateOnly Today => LocalDays.DateOf(_clock().ToUnixTimeSeconds(), _zone);

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
        await _gate.WaitAsync();
        try
        {
            if (!_started || seq != Volatile.Read(ref _seq)) return false;
            var context = new UsageContext(_clock(), _zone, _running(), _signInUtc, _weekStart, _gamesOnly(), _dbPath, _deviceScope(), _localDeviceId());
            T result;
            try { result = await Task.Run(() => query(context)); }
            catch (Exception ex) when (ex is SqliteException or IOException) { return false; } // no DB yet
            if (seq != Volatile.Read(ref _seq) || !_started) return false;
            apply(result);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
