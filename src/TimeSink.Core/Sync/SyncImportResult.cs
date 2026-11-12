namespace TimeSink.Core.Sync;

/// <summary>
/// Status of a snapshot import operation or inspected sync file.
/// </summary>
public enum SyncStatus
{
    Success,
    Pending,
    BadHash,
    NewerFormat,
    ConflictCopy,
    SelfFile,
    ThirdDeviceIgnored,
    DeviceForgotten,
    Failed,
    EncryptionKeyMissing,
    EncryptionKeyWrong,
    RemoteNotEncrypted,
    Unchanged
}

/// <summary>
/// Result of importing a &lt;deviceId&gt;.tssync snapshot.
/// </summary>
public sealed record SyncImportResult(
    SyncStatus Status,
    string? Message,
    int AppsAdded,
    int SessionsAdded,
    int SessionsUpdated,
    int SessionsSkipped);
