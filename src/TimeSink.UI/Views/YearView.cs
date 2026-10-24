using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Views;

/// <summary>
/// Year tab host: the Pace | Wrapped | Matrix segmented switch (decision 9) over one body at a time. Sub-views are built
/// on first selection only, and the selection is remembered in <c>ui.lastYearView</c>. Only the default (Pace) body is
/// built with the tab; its data loads in <see cref="Start"/>, after first paint.
/// </summary>
public sealed class YearView : UserControl
{
    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly Dictionary<YearSubView, UIElement> _bodies = new();
    private readonly ContentPresenter _body = new();
    private YearSubView _current;
    private bool _started;

    public YearView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _current = YearSubViews.Parse(SettingsStore.Get(dbPath, YearSubViews.SettingKey));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 8, 16, 0) };
        foreach (var (sub, key) in new[]
        {
            (YearSubView.Pace, "Str.YearSwitchPace"), (YearSubView.Wrapped, "Str.YearSwitchWrapped"), (YearSubView.Matrix, "Str.YearSwitchMatrix"),
        })
        {
            var b = new RadioButton { GroupName = "YearSubView", IsChecked = sub == _current, Tag = sub };
            b.SetResourceReference(StyleProperty, "SegButton");
            b.SetResourceReference(ContentControl.ContentProperty, key);
            b.Checked += (_, _) => Select(sub, persist: true);
            strip.Children.Add(b);
        }
        Grid.SetRow(_body, 1);
        root.Children.Add(strip);
        root.Children.Add(_body);
        Content = root;
        Select(_current, persist: false);
        Unloaded += (_, _) => Stop();
    }

    /// <summary>The one place a new sub-view is wired in.</summary>
    private UIElement CreateSubView(YearSubView sub) => sub switch
    {
        YearSubView.Wrapped => new YearWrappedView(_dbPath, _themes, _live),
        YearSubView.Matrix => new YearMatrixView(_dbPath, _themes, _live),
        _ => new YearPaceView(_dbPath, _themes, _live),
    };

    private void Select(YearSubView sub, bool persist)
    {
        if (_started && _bodies.TryGetValue(_current, out var old)) (old as IYearSubView)?.Stop();
        if (!_bodies.TryGetValue(sub, out var body)) _bodies[sub] = body = CreateSubView(sub);
        _current = sub;
        _body.Content = body;
        if (_started) (body as IYearSubView)?.Start();
        if (!persist) return;
        var path = _dbPath;
        var value = sub.ToString();
        Task.Run(() => SettingsStore.Set(path, YearSubViews.SettingKey, value));
    }

    /// <summary>Begin loading the visible sub-view (called once the window has rendered).</summary>
    public void Start()
    {
        _started = true;
        (_bodies[_current] as IYearSubView)?.Start();
    }

    public void Stop()
    {
        _started = false;
        foreach (var b in _bodies.Values) (b as IYearSubView)?.Stop();
    }

    public void Refresh()
    {
        if (_started) (_bodies[_current] as IYearSubView)?.Refresh();
    }
}
