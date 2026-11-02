using TimeSink.Core.Data;
using TimeSink.Core.Settings;

namespace TimeSink.Core.Sync;

/// <summary>
/// Background synchronization host: writes snapshots periodically (default 5 minutes) when sessions change,
/// watches the sync folder for incoming snapshots, enforces the 10-minute discovery timeout,
/// and handles 'Sync now' and clean exit snapshots (shared/sync-format.md section 2).
/// </summary>
public sealed class SyncHost : IDisposable
{
    private static readonly TimeSpan DefaultExportInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(30);

    private readonly string _dbPath;
    private readonly TimeZoneInfo _zone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private readonly TimeSpan _exportInterval;

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private string? _watchedFolder;
    private Timer? _timer;
    private Timer? _debounceTimer;

    private DateTimeOffset _lastExportUtc = DateTimeOffset.MinValue;
    private long _lastSessionCount = -1;
    private long _lastMaxLastSeen = -1;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
    private bool _disposed;
    private int _executing;

    public SyncHost(
        string dbPath,
        TimeZoneInfo? zone = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        TimeSpan? exportInterval = null)
    {
        _dbPath = dbPath;
        _zone = zone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
        _exportInterval = exportInterval ?? DefaultExportInterval;
    }

    public void Start(TimeSpan? checkInterval = null)
    {
        var interval = checkInterval ?? TimeSpan.FromSeconds(15);
        _timer = new Timer(_ => CheckSafe(forceExport: false), null, TimeSpan.Zero, interval);
        _debounceTimer = new Timer(_ => CheckSafe(forceExport: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void SyncNow()
    {
        lock (_gate)
        {
            _backoffUntil = DateTimeOffset.MinValue;
        }
        CheckSafe(forceExport: true);
    }

    public void TriggerFolderChange()
    {
        lock (_gate)
        {
            if (!_disposed)
                _debounceTimer?.Change(DefaultDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Check(bool forceExport = false)
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _executing, 1) == 1) return;

        try
        {
            var now = _clock();

            lock (_gate)
            {
                if (now < _backoffUntil && !forceExport) return;
            }

            var settings = AppSettings.ForFile(_dbPath);
            var enabled = settings.Get(Setting.SyncEnabled);
            var folder = settings.Get(Setting.SyncFolder);

            if (!enabled || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                UpdateWatcher(null);
                return;
            }

            UpdateWatcher(folder);

            using var db = Db.Open(_dbPath);
            var localDeviceId = db.GetSetting(Setting.DeviceId.Key);
            if (string.IsNullOrEmpty(localDeviceId))
            {
                localDeviceId = Guid.NewGuid().ToString();
                db.SetSetting(Setting.DeviceId.Key, localDeviceId);
            }

            // 1. Export local snapshot if due or forced
            var sessionsChanged = HasSessionsChanged(db, localDeviceId, out var count, out var maxLastSeen);
            var exportDue = forceExport || _lastExportUtc == DateTimeOffset.MinValue || (now - _lastExportUtc >= _exportInterval && sessionsChanged);

            if (exportDue)
            {
                try
                {
                    SyncWriter.Write(db.Connection, folder, now);
                    _lastExportUtc = now;
                    _lastSessionCount = count;
                    _lastMaxLastSeen = maxLastSeen;
                    db.SetSetting(Setting.SyncLastExportUtc.Key, now.ToUnixTimeSeconds().ToString());
                    _log?.Invoke($"sync: exported local snapshot ({count} sessions)");
                }
                catch (Exception ex)
                {
                    HandleError(ex, "export");
                    return;
                }
            }

            // 2. Scan folder and import snapshots from other devices
            bool anyRemoteFound = false;
            bool placeholderDetected = false;

            try
            {
                var files = Directory.GetFiles(folder, "*" + SyncWriter.Extension);
                foreach (var file in files)
                {
                    if (SyncReader.IsConflictCopy(file)) continue;

                    if (SyncReader.IsPlaceholder(file))
                    {
                        placeholderDetected = true;
                        continue;
                    }

                    var result = SyncReader.Import(file, _dbPath, _zone);
                    if (result.Status == SyncStatus.Success)
                    {
                        anyRemoteFound = true;
                        db.SetSetting(Setting.SyncLastImportUtc.Key, now.ToUnixTimeSeconds().ToString());
                        _log?.Invoke($"sync: imported snapshot {Path.GetFileName(file)} (+{result.AppsAdded} apps, +{result.SessionsAdded} sessions, {result.SessionsUpdated} updated)");
                    }
                    else if (result.Status == SyncStatus.Pending)
                    {
                        placeholderDetected = true;
                    }
                    else if (result.Status == SyncStatus.ThirdDeviceIgnored)
                    {
                        _log?.Invoke($"sync: third device snapshot ignored: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError(ex, "import");
                return;
            }

            if (anyRemoteFound)
            {
                try
                {
                    DeviceIdentity.MergeMatchingCrossDeviceApps(db);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"sync: auto-merge matching apps error: {ex.Message}");
                }
            }

            // 3. Update sync state and check 10-minute discovery window
            UpdateSyncState(db, now, anyRemoteFound, placeholderDetected);
        }
        finally
        {
            Interlocked.Exchange(ref _executing, 0);
        }
    }

    private void CheckSafe(bool forceExport)
    {
        try { Check(forceExport); }
        catch (Exception ex) { HandleError(ex, "check"); }
    }

    private bool HasSessionsChanged(Db db, string localDeviceId, out long count, out long maxLastSeen)
    {
        count = 0;
        maxLastSeen = 0;
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), COALESCE(MAX(s.last_seen_utc), 0)
            FROM sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE a.device_id = $devId;
            """;
        cmd.Parameters.AddWithValue("$devId", localDeviceId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            count = r.GetInt64(0);
            maxLastSeen = r.GetInt64(1);
        }

        return count != _lastSessionCount || maxLastSeen != _lastMaxLastSeen;
    }

    private void UpdateSyncState(Db db, DateTimeOffset now, bool anyRemoteFound, bool placeholderDetected)
    {
        long activeRemoteDevices = 0;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE is_local = 0 AND state = 'active';";
            activeRemoteDevices = (long)(cmd.ExecuteScalar() ?? 0L);
        }

        if (activeRemoteDevices > 0 || anyRemoteFound)
        {
            db.SetSetting(Setting.SyncState.Key, "synced");
            return;
        }

        if (placeholderDetected)
        {
            db.SetSetting(Setting.SyncState.Key, "pending");
            return;
        }

        // Discovery window: 10 minutes
        var enabledUtcStr = db.GetSetting(Setting.SyncEnabledUtc.Key);
        long enabledUtc;
        if (string.IsNullOrEmpty(enabledUtcStr) || !long.TryParse(enabledUtcStr, out enabledUtc) || enabledUtc <= 0)
        {
            enabledUtc = now.ToUnixTimeSeconds();
            db.SetSetting(Setting.SyncEnabledUtc.Key, enabledUtc.ToString());
        }

        var elapsedSinceEnabled = now - DateTimeOffset.FromUnixTimeSeconds(enabledUtc);
        if (elapsedSinceEnabled >= DiscoveryTimeout)
        {
            db.SetSetting(Setting.SyncState.Key, "no_other_device");
        }
        else
        {
            db.SetSetting(Setting.SyncState.Key, "waiting_for_device");
        }
    }

    private void HandleError(Exception ex, string context)
    {
        lock (_gate)
        {
            _backoffUntil = _clock().Add(ErrorBackoff);
        }
        _log?.Invoke($"sync error ({context}): {ex.GetType().Name}: {ex.Message}");
    }

    private void UpdateWatcher(string? folder)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (string.Equals(_watchedFolder, folder, StringComparison.OrdinalIgnoreCase)) return;

            _watcher?.Dispose();
            _watcher = null;
            _watchedFolder = null;

            if (folder is not null && Directory.Exists(folder))
            {
                try
                {
                    var w = new FileSystemWatcher(folder, "*" + SyncWriter.Extension)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    FileSystemEventHandler changed = (_, _) => TriggerFolderChange();
                    w.Created += changed;
                    w.Changed += changed;
                    w.Renamed += (_, _) => TriggerFolderChange();

                    _watcher = w;
                    _watchedFolder = folder;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"sync watcher error: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            _timer?.Dispose();
            _debounceTimer?.Dispose();
            _watcher?.Dispose();
            _watcher = null;
        }

        // On quit: write clean final snapshot if sync is enabled
        try
        {
            var settings = AppSettings.ForFile(_dbPath);
            if (settings.Get(Setting.SyncEnabled))
            {
                var folder = settings.Get(Setting.SyncFolder);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    SyncWriter.Write(_dbPath, folder, _clock());
                    _log?.Invoke("sync: clean final snapshot written on exit");
                }
            }
        }
        catch { }
    }
}
