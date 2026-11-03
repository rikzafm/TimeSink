using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Settings;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Views;

/// <summary>
/// Year tab host: the Pace | Wrapped | Matrix segmented switch (decision 9) and year navigator (< 2026 >) over one body at a time.
/// Sub-views are built on first selection only, and the selection is remembered in <c>ui.lastYearView</c>. Only the default (Pace) body is
/// built with the tab; its data loads in <see cref="Start"/>, after first paint.
/// </summary>
public sealed class YearView : UserControl, ILiveView
{
    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly Dictionary<YearSubView, UIElement> _bodies = new();
    private readonly ContentPresenter _body = new();
    private readonly Grid _strip;
    private YearSubView _current;
    private bool _started;

    private int _selectedYear = DateTime.Today.Year;
    private DateOnly? _firstTracked;
    private readonly Button _prev = NavArrow("◂");
    private readonly Button _next = NavArrow("▸");
    private readonly TextBlock _yearLabel = new();
    private readonly Action<long, AppDetailRange, DateOnly?>? _showAppDetail;

    public YearView(string dbPath, ThemeService themes, LiveState live, Action<long, AppDetailRange, DateOnly?>? showAppDetail = null)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _showAppDetail = showAppDetail;
        _current = AppSettings.ForFile(dbPath).Get(Setting.LastYearView);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(16, 8, 16, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var strip = new StackPanel { Orientation = Orientation.Horizontal };
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
        Grid.SetColumn(strip, 0);
        header.Children.Add(strip);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _prev.Click += (_, _) => NavigateYear(-1);
        _next.Click += (_, _) => NavigateYear(1);

        _yearLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _yearLabel.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _yearLabel.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _yearLabel.FontWeight = FontWeights.SemiBold;
        _yearLabel.VerticalAlignment = VerticalAlignment.Center;
        _yearLabel.Margin = new Thickness(6, 0, 6, 0);

        nav.Children.Add(_prev);
        nav.Children.Add(_yearLabel);
        nav.Children.Add(_next);
        Grid.SetColumn(nav, 2);
        header.Children.Add(nav);

        _strip = header;
        UpdateNav();

        // The switch scrolls away with the body: hidden once the body is scrolled (only when it stays scrollable without the
        // strip, so hiding can't clamp the offset back to 0 and flip-flop), shown again at the top.
        _body.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not ScrollViewer sv || _current == YearSubView.Pace) return; // Pace pins its top, switch included
            if (sv.VerticalOffset <= 0) _strip.Visibility = Visibility.Visible;
            else if (_strip.Visibility == Visibility.Visible && sv.ScrollableHeight > _strip.ActualHeight + _strip.Margin.Top + 24)
                _strip.Visibility = Visibility.Collapsed;
        }));
        Grid.SetRow(_body, 1);
        root.Children.Add(header);
        root.Children.Add(_body);
        Content = root;
        Select(_current, persist: false);
        Unloaded += (_, _) => Stop();
    }

    private static Button NavArrow(string glyph)
    {
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var box = new FrameworkElementFactory(typeof(Border));
        box.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        box.AppendChild(text);
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Brush.Accent")));
        var off = new Trigger { Property = IsEnabledProperty, Value = false };
        off.Setters.Add(new Setter(OpacityProperty, new DynamicResourceExtension("Double.Opacity.Disabled")));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = box };
        template.Triggers.Add(hover);
        template.Triggers.Add(off);
        var b = new Button { Content = glyph, Template = template, Width = 22, Height = 22, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(ForegroundProperty, "Brush.TextMuted");
        b.SetResourceReference(FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(FontSizeProperty, "Double.FontSize.Body");
        return b;
    }

    private void SetTrackingStart(DateOnly? start)
    {
        if (start is not null && (_firstTracked is null || start < _firstTracked))
        {
            _firstTracked = start;
            UpdateNav();
        }
    }

    public void NavigateYear(int delta)
    {
        var target = _selectedYear + delta;
        var todayYear = DateTime.Today.Year;
        if (target > todayYear) return;
        if (delta < 0 && _firstTracked is { } f && target < f.Year) return;
        _selectedYear = target;
        UpdateNav();
        foreach (var b in _bodies.Values)
        {
            (b as IYearSubView)?.SetYear(_selectedYear);
        }
    }

    private void UpdateNav()
    {
        _yearLabel.Text = _selectedYear.ToString();
        var todayYear = DateTime.Today.Year;
        _prev.IsEnabled = _firstTracked is not null && _selectedYear > _firstTracked.Value.Year;
        _next.IsEnabled = _selectedYear < todayYear;
    }

    /// <summary>The one place a new sub-view is wired in.</summary>
    private UIElement CreateSubView(YearSubView sub)
    {
        IYearSubView view = sub switch
        {
            YearSubView.Wrapped => new YearWrappedView(_dbPath, _themes, _live, SetTrackingStart),
            YearSubView.Matrix => new YearMatrixView(_dbPath, _themes, _live, SetTrackingStart, _showAppDetail),
            _ => new YearPaceView(_dbPath, _themes, _live, SetTrackingStart, _showAppDetail),
        };
        view.SetYear(_selectedYear);
        return (UIElement)view;
    }

    private void Select(YearSubView sub, bool persist)
    {
        if (_started && _bodies.TryGetValue(_current, out var old)) (old as IYearSubView)?.Stop();
        if (!_bodies.TryGetValue(sub, out var body)) _bodies[sub] = body = CreateSubView(sub);
        _current = sub;
        _strip.Visibility = Visibility.Visible;
        _body.Content = body;
        if (_started) (body as IYearSubView)?.Start();
        if (!persist) return;
        var path = _dbPath;
        Task.Run(() => AppSettings.ForFile(path).Set(Setting.LastYearView, sub));
    }

    /// <summary>Begin loading the visible sub-view (called once the window has rendered).</summary>
    public void Start()
    {
        _started = true;
        var todayYear = DateTime.Today.Year;
        if (_selectedYear != todayYear)
        {
            _selectedYear = todayYear;
            foreach (var b in _bodies.Values)
            {
                (b as IYearSubView)?.SetYear(_selectedYear);
            }
            UpdateNav();
        }
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
