namespace TimeSink.Core.Security;

/// <summary>
/// Manages encryption keys for the TimeSink database and cross-device sync.
/// </summary>
public interface IKeyManager
{
    /// <summary>
    /// Retrieves the existing database encryption key, or creates and stores a new 256-bit key.
    /// </summary>
    byte[] GetOrCreateDatabaseKey();

    /// <summary>
    /// Deletes the stored database key (e.g., during a full data wipe).
    /// </summary>
    void DeleteDatabaseKey();

    /// <summary>
    /// Retrieves the stored sync passphrase, or null if no sync passphrase has been configured.
    /// </summary>
    string? GetSyncPassphrase();

    /// <summary>
    /// Securely stores the sync passphrase.
    /// </summary>
    void SetSyncPassphrase(string passphrase);

    /// <summary>
    /// Checks whether a sync passphrase is currently configured.
    /// </summary>
    bool HasSyncPassphrase();

    /// <summary>
    /// Clears and deletes the stored sync passphrase.
    /// </summary>
    void ClearSyncPassphrase();
}
