using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Security;
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
    private readonly IKeyManager? _keyManager;

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private string? _watchedFolder;
    private Timer? _timer;
    private Timer? _debounceTimer;

    private DateTimeOffset _lastExportUtc = DateTimeOffset.MinValue;
    private long _lastSessionCount = -1;
    private long _lastMaxLastSeen = -1;
    private long _lastAppsFingerprint = -1;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
    private bool _disposed;
    private int _executing;
    private long _fingerprintCount = -1;
    private readonly Dictionary<string, FileStamp> _unchangedFiles = new(StringComparer.OrdinalIgnoreCase);

    // Encryption failures skip only their own file until its retry fingerprint changes. A file in here was NOT imported,
    // so it never gets a `sync.imported.*` fingerprint and never lands in _unchangedFiles.
    private sealed record RetrySkip(string Fingerprint, SyncStatus Status, string? Message);
    private readonly Dictionary<string, RetrySkip> _retrySkips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _loggedStatus = new(StringComparer.OrdinalIgnoreCase);
    private string? _loggedExportError;

    private sealed record FileStamp(long Length, DateTime LastWriteUtc)
    {
        public static FileStamp? Of(string path)
        {
            try { var i = new FileInfo(path); return new FileStamp(i.Length, i.LastWriteTimeUtc); }
            catch (IOException) { return null; }
        }
    }

    public SyncHost(
        string dbPath,
        TimeZoneInfo? zone = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        TimeSpan? exportInterval = null,
        IKeyManager? keyManager = null)
    {
        _dbPath = dbPath;
        _zone = zone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
        _exportInterval = exportInterval ?? DefaultExportInterval;
        _keyManager = keyManager;
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
            _retrySkips.Clear();
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
            var appsChanged = HasAppsChanged(db, localDeviceId, out var appsFingerprint);
            var exportDue = forceExport || _lastExportUtc == DateTimeOffset.MinValue || (now - _lastExportUtc >= _exportInterval && sessionsChanged) || appsChanged;

            string? exportError = null;
            if (exportDue)
            {
                try
                {
                    SyncWriter.Write(db.Connection, folder, now, keyManager: _keyManager);
                    _lastExportUtc = now;
                    _lastSessionCount = count;
                    _lastMaxLastSeen = maxLastSeen;
                    _lastAppsFingerprint = appsFingerprint;
                    db.SetSetting(Setting.SyncLastExportUtc.Key, now.ToUnixTimeSeconds().ToString());
                    _loggedExportError = null;
                    _log?.Invoke($"sync: exported local snapshot ({count} sessions)");
                }
                catch (Exception ex)
                {
                    // An export failure must not block the import pass below.
                    exportError = ShortError(ex);
                    if (_loggedExportError != exportError)
                    {
                        _loggedExportError = exportError;
                        _log?.Invoke($"sync error (export): {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // 2. Scan folder and import snapshots from other devices
            bool anyRemoteFound = false;
            // Fingerprint rows are cleared by forget/delete/DeleteAll; a changed count invalidates the in-memory skip cache.
            var fingerprintCount = CountFingerprints(db);
            if (fingerprintCount != _fingerprintCount)
            {
                _unchangedFiles.Clear();
                _fingerprintCount = fingerprintCount;
            }
            bool placeholderDetected = false;
            string? encryptionState = null;
            string? importError = null;
            var passHash = PassphraseFingerprint();
            var encryptedSetting = settings.Get(Setting.SyncEncrypted);

            try
            {
                var files = Directory.GetFiles(folder, "*" + SyncWriter.Extension);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    // Our own export: never import it (Import would try the current passphrase on it before noticing it is ours).
                    if (string.Equals(Path.GetFileNameWithoutExtension(file), localDeviceId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (SyncReader.IsConflictCopy(file))
                    {
                        LogStatusOnce(file, "ConflictCopy", $"sync: cloud conflict copy ignored: {name}");
                        continue;
                    }

                    if (SyncReader.IsPlaceholder(file))
                    {
                        placeholderDetected = true;
                        continue;
                    }

                    var stamp = FileStamp.Of(file);
                    if (stamp is not null && _unchangedFiles.TryGetValue(file, out var seen) && seen == stamp)
                        continue;

                    var retryFp = stamp is null ? null : $"{stamp.Length}:{stamp.LastWriteUtc.Ticks}|{passHash}|{encryptedSetting}";
                    if (retryFp is not null && _retrySkips.TryGetValue(file, out var skip))
                    {
                        if (skip.Fingerprint == retryFp)
                        {
                            encryptionState ??= EncryptionStateOf(skip.Status);
                            importError ??= skip.Message;
                            continue;
                        }
                        _retrySkips.Remove(file);
                    }

                    var result = SyncReader.Import(file, _dbPath, _zone, _keyManager);
                    if (result.Status is not (SyncStatus.EncryptionKeyMissing or SyncStatus.EncryptionKeyWrong or SyncStatus.RemoteNotEncrypted))
                        _retrySkips.Remove(file);
                    if (result.Status is SyncStatus.Success or SyncStatus.Unchanged)
                        _loggedStatus.Remove(file);

                    if (result.Status == SyncStatus.Unchanged)
                    {
                        if (stamp is not null) _unchangedFiles[file] = stamp;
                    }
                    else if (result.Status == SyncStatus.Success)
                    {
                        anyRemoteFound = true;
                        db.SetSetting(Setting.SyncLastImportUtc.Key, now.ToUnixTimeSeconds().ToString());
                        _log?.Invoke($"sync: imported snapshot {name} (+{result.AppsAdded} apps, +{result.SessionsAdded} sessions, {result.SessionsUpdated} updated)");
                    }
                    else if (result.Status == SyncStatus.Pending)
                    {
                        placeholderDetected = true;
                    }
                    else if (result.Status == SyncStatus.ThirdDeviceIgnored)
                    {
                        _log?.Invoke($"sync: third device snapshot ignored: {name}");
                    }
                    else if (result.Status is SyncStatus.EncryptionKeyMissing or SyncStatus.EncryptionKeyWrong or SyncStatus.RemoteNotEncrypted)
                    {
                        encryptionState ??= EncryptionStateOf(result.Status);
                        importError ??= result.Message;
                        if (retryFp is not null) _retrySkips[file] = new RetrySkip(retryFp, result.Status, result.Message);
                        var text = result.Status switch
                        {
                            SyncStatus.EncryptionKeyMissing => $"sync: snapshot encrypted but key missing: {name}",
                            SyncStatus.EncryptionKeyWrong => $"sync: passphrase failed decryption: {name}",
                            _ => $"sync: remote file {name} is not encrypted while local expects encryption",
                        };
                        LogStatusOnce(file, result.Status + "|" + retryFp, text);
                    }
                    else if (result.Status != SyncStatus.SelfFile)
                    {
                        // BadHash, NewerFormat, DeviceForgotten, ConflictCopy, Failed: one line per change of status or file.
                        LogStatusOnce(file, result.Status + "|" + (stamp is null ? "" : $"{stamp.Length}:{stamp.LastWriteUtc.Ticks}"),
                            $"sync: {name}: {result.Status}" + (string.IsNullOrEmpty(result.Message) ? "" : $" ({result.Message})"));
                        if (result.Status != SyncStatus.DeviceForgotten) importError ??= result.Message ?? result.Status.ToString();
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

            // 3. Update sync state: encryption trouble outranks a failed export, which outranks the normal states.
            if (encryptionState is not null)
                db.SetSetting(Setting.SyncState.Key, encryptionState);
            else if (exportError is not null)
                db.SetSetting(Setting.SyncState.Key, Setting.SyncStates.ExportFailed);
            else
                UpdateSyncState(db, now, anyRemoteFound, placeholderDetected);

            var lastError = exportError ?? importError;
            if (lastError is not null) db.SetSetting(Setting.SyncLastError.Key, lastError);
            else if (!string.IsNullOrEmpty(db.GetSetting(Setting.SyncLastError.Key))) db.SetSetting(Setting.SyncLastError.Key, "");
        }
        finally
        {
            Interlocked.Exchange(ref _executing, 0);
        }
    }

    private static string EncryptionStateOf(SyncStatus status) => status switch
    {
        SyncStatus.EncryptionKeyMissing => Setting.SyncStates.EncryptionKeyMissing,
        SyncStatus.EncryptionKeyWrong => Setting.SyncStates.EncryptionKeyWrong,
        _ => Setting.SyncStates.RemoteNotEncrypted,
    };

    private static string ShortError(Exception ex) => ex.GetType().Name + ": " + ex.Message.Split('\n')[0].Trim();

    /// <summary>In-memory only: changes when the stored passphrase changes, without exposing it.</summary>
    private string PassphraseFingerprint()
    {
        try
        {
            var p = (_keyManager ?? Db.KeyManager).GetSyncPassphrase();
            return string.IsNullOrEmpty(p) ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p)));
        }
        catch { return "?"; }
    }

    private void LogStatusOnce(string file, string key, string text)
    {
        if (_loggedStatus.TryGetValue(file, out var last) && last == key) return;
        _loggedStatus[file] = key;
        _log?.Invoke(text);
    }

    private static long CountFingerprints(Db db)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM settings WHERE key LIKE 'sync.imported.%';";
        return (long)(cmd.ExecuteScalar() ?? 0L);
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

    private bool HasAppsChanged(Db db, string localDeviceId, out long fingerprint)
    {
        fingerprint = 0;
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(kind), 0), COALESCE(SUM(is_background), 0), COALESCE(SUM(COALESCE(canonical_id, 0)), 0), COALESCE(MAX(added_utc), 0)
            FROM apps
            WHERE device_id = $devId
               OR id IN (SELECT canonical_id FROM apps WHERE device_id = $devId AND canonical_id IS NOT NULL)
               OR canonical_id IN (SELECT id FROM apps WHERE device_id = $devId);
            """;
        cmd.Parameters.AddWithValue("$devId", localDeviceId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            fingerprint = HashCode.Combine(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), HashCode.Combine(r.GetInt64(3), r.GetInt64(4)));
        }

        return _lastAppsFingerprint != -1 && fingerprint != _lastAppsFingerprint;
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
                    SyncWriter.Write(_dbPath, folder, _clock(), keyManager: _keyManager);
                    _log?.Invoke("sync: clean final snapshot written on exit");
                }
            }
        }
        catch { }
    }
}
