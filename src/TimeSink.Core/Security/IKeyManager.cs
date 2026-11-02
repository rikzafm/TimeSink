namespace TimeSink.Core.Security;

/// <summary>
/// Manages encryption keys for the TimeSink database.
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
}
