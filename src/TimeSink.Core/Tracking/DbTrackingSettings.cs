using TimeSink.Core.Data;

namespace TimeSink.Core.Tracking;

/// <summary>Reads the tracking.* settings (implementation.md 5.1) from the database on every access.</summary>
public sealed class DbTrackingSettings(Db db) : ITrackingSettings
{
    public bool PauseOnSleep => Bool("tracking.pauseOnSleep", true);
    public bool CountWhenLocked => Bool("tracking.countWhenLocked", true);
    public int MinSessionSeconds =>
        int.TryParse(db.GetSetting("tracking.minSessionSeconds"), out var v) && v >= 0 ? v : 15;

    private bool Bool(string key, bool fallback) =>
        bool.TryParse(db.GetSetting(key), out var v) ? v : fallback;
}
