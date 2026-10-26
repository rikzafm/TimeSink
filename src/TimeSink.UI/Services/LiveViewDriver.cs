using System.Windows.Threading;
using TimeSink.Core.Usage;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Services;

/// <summary>
/// What every live tab view does around its query: while started it connects <see cref="LiveState"/>, refreshes on a timer and
/// on live-state changes, and redraws when the theme or an icon changes. Stopping unsubscribes everything, so a stopped view
/// is rebuilt from fresh data when it starts again. It also starts and stops the view's <see cref="UsageSnapshotLoader"/>.
/// </summary>
internal sealed class LiveViewDriver
{
    private readonly LiveState _live;
    private readonly ThemeService _themes;
    private readonly UsageSnapshotLoader _loader;
    private readonly DispatcherTimer _timer;
    private readonly Action _refresh, _redraw, _tick, _liveChanged;
    private readonly Func<bool>? _isIdle;

    /// <param name="refresh">Loads the view's data; called on start, and by default on each tick and live-state change.</param>
    /// <param name="redraw">Repaints from the data already loaded.</param>
    /// <param name="onTick">Replaces <paramref name="refresh"/> as the timer's action (Today, which also ticks its clock).</param>
    /// <param name="onLiveChanged">Replaces <paramref name="refresh"/> as the live-state action (views that only follow the live period).</param>
    /// <param name="isIdle">True while the flyout is on warm standby: timer ticks are skipped.</param>
    public LiveViewDriver(LiveState live, ThemeService themes, UsageSnapshotLoader loader, TimeSpan interval,
        Action refresh, Action redraw, Action? onTick = null, Action? onLiveChanged = null, Func<bool>? isIdle = null)
    {
        _live = live;
        _themes = themes;
        _loader = loader;
        _refresh = refresh;
        _redraw = redraw;
        _tick = onTick ?? refresh;
        _liveChanged = onLiveChanged ?? refresh;
        _isIdle = isIdle;
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => { if (_isIdle?.Invoke() != true) _tick(); };
    }

    public bool Started => _loader.Started;

    public void Start()
    {
        if (_loader.Started) return;
        _loader.Start();
        _live.Changed += OnLiveChanged;
        _themes.Applied += OnRedraw;
        AppIcons.Updated += OnRedraw;
        _live.Start();
        _timer.Start();
        _refresh();
    }

    public void Stop()
    {
        _loader.Stop();
        _timer.Stop();
        _live.Changed -= OnLiveChanged;
        _themes.Applied -= OnRedraw;
        AppIcons.Updated -= OnRedraw;
    }

    private void OnLiveChanged() => _liveChanged();

    private void OnRedraw() => _redraw();
}
