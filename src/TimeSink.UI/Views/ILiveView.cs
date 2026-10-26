namespace TimeSink.UI.Views;

/// <summary>A flyout tab body that loads and ticks only while it is the one on screen.</summary>
public interface ILiveView
{
    /// <summary>Begin (or resume) loading; idempotent.</summary>
    void Start();

    /// <summary>Stop timers and subscriptions while hidden.</summary>
    void Stop();
}
