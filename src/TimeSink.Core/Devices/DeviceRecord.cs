namespace TimeSink.Core.Devices;

/// <summary>
/// A registered device (Windows PC or macOS Mac).
/// </summary>
public sealed record DeviceRecord(
    string DeviceId,
    string Name,
    string Platform,
    bool IsLocal,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset? LastImportUtc = null,
    DateTimeOffset? LastFileUtc = null,
    string State = "active"
);
