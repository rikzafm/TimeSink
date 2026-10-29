using TimeSink.Core.Data;
using TimeSink.Core.Settings;

namespace TimeSink.Core.Tracking;

/// <summary>Reads the tracking.* settings (implementation.md 5.1) from the database on every access.</summary>
public sealed class DbTrackingSettings(Db db) : ITrackingSettings
{
    private readonly AppSettings _settings = AppSettings.ForDb(db);

    public bool PauseOnSleep => _settings.Get(Setting.PauseOnSleep);
    public bool CountWhenLocked => _settings.Get(Setting.CountWhenLocked);
    public int MinSessionSeconds => _settings.Get(Setting.MinSessionSeconds);
}
