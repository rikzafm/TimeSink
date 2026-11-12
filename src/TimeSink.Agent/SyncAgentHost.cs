using TimeSink.Agent.Native;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;

namespace TimeSink.Agent;

/// <summary>
/// Connects <see cref="SyncHost"/> into the Agent's Windows message loop and named-event signals.
/// </summary>
internal sealed class SyncAgentHost : IDisposable
{
    public const uint SyncNowMessage = NativeMethods.WM_APP + 5;

    private readonly string _dbPath;
    private readonly IKeyManager _keyManager;
    private readonly SyncHost _sync;
    private readonly nint _hwnd;
    private EventWaitHandle? _syncNowEvent;
    private RegisteredWaitHandle? _syncNowWait;

    public SyncAgentHost(string dbPath, nint hwnd, IKeyManager? keyManager = null)
    {
        _dbPath = dbPath;
        _hwnd = hwnd;
        _keyManager = keyManager ?? Db.KeyManager;
        _sync = new SyncHost(dbPath, TimeZoneInfo.Local, null, Log.Info, keyManager: _keyManager);
    }

    public void OnSyncPassphraseSet(string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        // 1. Persist passphrase via DPAPI
        _keyManager.SetSyncPassphrase(passphrase);

        // 2. Set sync.encrypted = true and reset paused error states
        using (var db = Db.Open(_dbPath))
        {
            db.SetSetting(Setting.SyncEncrypted.Key, "true");
            SyncReader.ClearFingerprints(db);

            var state = db.GetSetting(Setting.SyncState.Key);
            if (state is "encryption_key_missing" or "encryption_key_wrong" or "remote_not_encrypted")
            {
                db.SetSetting(Setting.SyncState.Key, "pending");
            }
        }

        // 3. Trigger immediate sync export and clear retry pause
        _sync.SyncNow();
    }

    public void OnSyncPassphraseCleared()
    {
        _keyManager.ClearSyncPassphrase();
        using (var db = Db.Open(_dbPath))
        {
            db.SetSetting(Setting.SyncEncrypted.Key, "false");
            SyncReader.ClearFingerprints(db);
        }
        _sync.SyncNow();
    }

    public void Start()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _syncNowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AgentSignals.SyncNowEventName);
                _syncNowWait = ThreadPool.RegisterWaitForSingleObject(_syncNowEvent,
                    (_, _) => NativeMethods.PostMessage(_hwnd, SyncNowMessage, 0, 0), null, Timeout.Infinite, executeOnlyOnce: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"sync now signal: {ex.Message}");
        }

        _sync.Start();
    }

    public bool HandleMessage(uint msg)
    {
        if (msg == SyncNowMessage)
        {
            _sync.SyncNow();
            return true;
        }
        return false;
    }

    public void SyncNow() => _sync.SyncNow();

    public void Dispose()
    {
        _syncNowWait?.Unregister(null);
        _syncNowEvent?.Dispose();
        _sync.Dispose();
    }
}
