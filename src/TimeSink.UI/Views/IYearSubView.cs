namespace TimeSink.UI.Views;

/// <summary>
/// A body hosted by <see cref="YearView"/> (Pace, Wrapped, Matrix). Built lazily on first selection; data must only be
/// loaded from <see cref="Start"/> (called after first paint / on selection), never from the constructor.
/// Tickets 13 and 14 implement this in their own file and add one line to <c>YearView.CreateSubView</c>.
/// </summary>
public interface IYearSubView
{
    /// <summary>Begin (or resume) loading; idempotent.</summary>
    void Start();

    /// <summary>Stop timers/work while hidden.</summary>
    void Stop();

    /// <summary>Re-query now (the flyout was revealed again, data may be stale).</summary>
    void Refresh();
}
