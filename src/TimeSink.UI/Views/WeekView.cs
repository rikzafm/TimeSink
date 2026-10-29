using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Week tab ("A · Ledger" redesign, decision 131): total with its change on last week, seven labelled columns, a 20-week
/// trend strip with the streak, and per-app rows with sparklines. Built lazily on first visit; data loads only after <see cref="Start"/> (so first paint is never delayed)
/// and refreshes every 30 s and on every pipe state change. All colours, fonts and text come from theme resources.
/// </summary>
public sealed class WeekView : UserControl, ILiveView
{
    private const int RefreshEverySeconds = 30;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly UsageSnapshotLoader _loader;
    private readonly UsageSnapshotLoader _panelLoader; // its own sequence, so opening a day never drops a week refresh or the other way round
    private readonly LiveViewDriver _driver;

    private readonly TextBlock _caption = Text("Soft", "Double.FontSize.Body", "Brush.TextMuted");
    private readonly TextBlock _total = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _streak = new() { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _streakSub = Text("Small", "Double.FontSize.Body", "Brush.TextMuted");
    private readonly TextBlock _delta = Text("Small", "Double.FontSize.Body", "Brush.TextMuted");
    private readonly WeekColumns _columns = new() { Height = 96 };
    private readonly TextBlock _gridCaption = Text("Small", "Double.FontSize.Small", "Brush.TextMuted");
    private readonly TextBlock _trendCaption = Text("Small", "Double.FontSize.Small", "Brush.TextMuted");
    private readonly WeekTrend _trend = new() { Height = 30 };
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _empty = Text("Small", "Double.FontSize.Body", "Brush.TextMuted");

    private WeekResult? _result;
    private int _weeksBack;
    private readonly Button _prev = NavArrow("◂");
    private readonly Button _next = NavArrow("▸");

    private readonly DetailPanel _panel = new() { Margin = new Thickness(0, 8, 0, 4) };
    private readonly ToolTip _tip = new();
    private readonly ToolTip _trendTip = new();
    private readonly Action<DateOnly>? _openDay;
    private int _panelSeq;
    private (DateOnly Day, long? AppId)? _pendingDay; // a sparkline click that needed another week: opened after that week loads

    public WeekView(string dbPath, ThemeService themes, LiveState live, Action<DateOnly>? openDay = null)
    {
        _openDay = openDay;
        _dbPath = dbPath;
        _themes = themes;
        _live = live;

        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _streak.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _streak.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        _streak.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        _streakSub.HorizontalAlignment = HorizontalAlignment.Right;
        _delta.Margin = new Thickness(0, 4, 0, 0);
        _trendCaption.HorizontalAlignment = HorizontalAlignment.Right;
        _trendCaption.SetResourceReference(TextBlock.TextProperty, "Str.WeekTrendCaption");
        _empty.Margin = new Thickness(0, 14, 0, 0);
        _empty.SetResourceReference(TextBlock.TextProperty, "Str.WeekEmpty");
        _empty.Visibility = Visibility.Collapsed;
        _streakSub.SetResourceReference(TextBlock.TextProperty, "Str.WeekStreakSub");
        _gridCaption.SetResourceReference(TextBlock.TextProperty, "Str.WeekGridCaption");

        _columns.SetResourceReference(WeekColumns.ColumnBrushProperty, "Brush.Heat2");
        _columns.SetResourceReference(WeekColumns.CurrentBrushProperty, "Brush.Accent");
        _columns.SetResourceReference(WeekColumns.FutureBrushProperty, "Brush.BorderSoft");
        _columns.SetResourceReference(WeekColumns.LabelBrushProperty, "Brush.TextMuted");
        _columns.SetResourceReference(WeekColumns.AverageBrushProperty, "Brush.Heat1");
        _trend.SetResourceReference(WeekTrend.BarBrushProperty, "Brush.Heat1");
        _trend.SetResourceReference(WeekTrend.CurrentBrushProperty, "Brush.Accent");

        var header = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        var left = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_total);
        left.Children.Add(_delta);
        header.Children.Add(left);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_streak);
        right.Children.Add(_streakSub);
        header.Children.Add(right);

        var trendHeader = new Grid { Margin = new Thickness(0, 14, 0, 6) };
        trendHeader.Children.Add(_gridCaption);
        trendHeader.Children.Add(_trendCaption);

        _caption.VerticalAlignment = VerticalAlignment.Center;
        _prev.Margin = new Thickness(0, 0, 4, 0);
        _next.Margin = new Thickness(4, 0, 0, 0);
        _prev.Click += (_, _) => Navigate(1);
        _next.Click += (_, _) => Navigate(-1);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        nav.Children.Add(_prev);
        nav.Children.Add(_caption);
        nav.Children.Add(_next);

        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(nav);
        root.Children.Add(header);
        root.Children.Add(_columns);
        root.Children.Add(_panel);
        root.Children.Add(trendHeader);
        root.Children.Add(_trend);
        root.Children.Add(_rows);
        root.Children.Add(_empty);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = root };

        _columns.SetResourceReference(WeekColumns.SelectedBrushProperty, "Brush.Accent");
        _columns.Cursor = System.Windows.Input.Cursors.Hand;
        StyleTip(_tip);
        _columns.ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(_columns, 80);
        ToolTipService.SetPlacement(_columns, System.Windows.Controls.Primitives.PlacementMode.Top);
        _columns.ColumnHovered += OnColumnHovered;
        _columns.ColumnClicked += OnColumnClicked;
        _trend.Cursor = System.Windows.Input.Cursors.Hand;
        StyleTip(_trendTip);
        _trend.ToolTip = _trendTip;
        ToolTipService.SetInitialShowDelay(_trend, 80);
        ToolTipService.SetPlacement(_trend, System.Windows.Controls.Primitives.PlacementMode.Top);
        _trend.BarHovered += OnTrendHovered;
        _trend.BarClicked += OnTrendClicked;
        _panel.CloseRequested += () => _columns.SelectedIndex = -1;
        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right && _result is { } kr)
            {
                var step = e.Key == System.Windows.Input.Key.Left ? -1 : 1;
                var next = _columns.SelectedIndex >= 0 ? _columns.SelectedIndex + step : kr.Days.ToList().FindIndex(d => d.IsToday) is var t and >= 0 ? t : step < 0 ? kr.Days.Count - 1 : 0;
                while (next >= 0 && next < kr.Days.Count && kr.Days[next].IsFuture) next += step;
                if (next >= 0 && next < kr.Days.Count) _ = ShowDayPanel(kr.Days[next].Date, null);
                e.Handled = true;
                return;
            }
            if (e.Key != System.Windows.Input.Key.Escape || _panel.Visibility != Visibility.Visible) return;
            ClosePanel();
            e.Handled = true;
        };

        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly);
        _panelLoader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw,
            isIdle: () => Window.GetWindow(this) is FlyoutWindow { IsRevealed: false }); // warm standby: stay idle
        Unloaded += (_, _) => Stop();
    }

    private static TextBlock Text(string style, string size, string brush)
    {
        var t = new TextBlock();
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }

    /// <summary>Begin loading and refreshing. Called once the window has rendered.</summary>
    public void Start()
    {
        _driver.Start();
        _panelLoader.Start();
    }

    public void Stop()
    {
        _driver.Stop();
        _panelLoader.Stop();
    }

    public async void Refresh()
    {
        var weeksBack = _weeksBack;
        if (await _loader.Refresh(c => WeekQuery.Run(c, weeksBack), r => _result = r)) Redraw();
    }

    private void Redraw()
    {
        if (_result is not { } r) return;
        _caption.Text = _weeksBack switch
        {
            0 => Str(StringKeys.WeekCaption),
            1 => Str(StringKeys.WeekLastCaption),
            _ => $"{UsageText.ShortDate(r.Days[0].Date)} – {UsageText.ShortDate(r.Days[^1].Date)}",
        };
        var nav = Navigator(r);
        _prev.IsEnabled = nav.CanPrev(r.WeekStartDate);
        _next.IsEnabled = nav.CanNext(r.WeekStartDate);
        _total.Text = UsageText.HoursMinutes(r.TotalSeconds);
        _streak.Text = Str(StringKeys.WeekStreak, r.StreakDays);
        _columns.Days = r.Days;
        _trend.Values = r.WeekTotals;

        var parts = new List<string>();
        if (r.ChangeSeconds is { } change)
            parts.Add(Str(StringKeys.WeekVsLast, (change >= 0 ? "▲ " : "▼ ") + UsageText.HoursMinutes(Math.Abs(change))));
        if (r.AverageSeconds > 0)
            parts.Add(Str(StringKeys.WeekAvg, UsageText.HoursMinutes((long)r.AverageSeconds)));
        _delta.Text = string.Join(" · ", parts);
        _delta.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = r.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppIcons.Load(_dbPath, r.Apps.Select(a => a.AppId));
        _rows.Children.Clear();
        foreach (var app in r.Apps) _rows.Children.Add(BuildRow(app, _themes.AppSlot(app.AppId)));
        if (_pendingDay is { } pending)
        {
            _pendingDay = null;
            _ = ShowDayPanel(pending.Day, r.Apps.FirstOrDefault(a => a.AppId == pending.AppId));
        }
    }

    /// <summary>Week navigation between the first tracked week and this one, for the week the result shows.</summary>
    private static PeriodNavigator Navigator(WeekResult r) =>
        new(PeriodUnit.Week, r.FirstTracked ?? r.Today, r.Today, r.WeekStartDate.DayOfWeek);

    /// <summary>Steps to the week before (<paramref name="delta"/> 1) or after (-1) the shown one; does nothing at either end.</summary>
    private void Navigate(int delta)
    {
        if (_result is not { } r) return;
        var nav = Navigator(r);
        if (delta > 0 ? !nav.CanPrev(r.WeekStartDate) : !nav.CanNext(r.WeekStartDate)) return;
        _weeksBack = nav.WeeksBack(nav.Step(r.WeekStartDate, -delta));
        ClosePanel();
        Refresh();
    }

    /// <summary>A bare triangle button: no box, muted until hovered.</summary>
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

    private void ClosePanel()
    {
        _panelSeq++;
        _columns.SelectedIndex = -1;
        _panel.Hide();
    }

    private void OnColumnHovered(int i, Point _)
    {
        if (i < 0 || _result is not { } r) { _tip.IsOpen = false; return; }
        var d = r.Days[i];
        _tip.Content = $"{UsageText.WeekdayDate(d.Date, full: true)}{Environment.NewLine}{UsageText.HoursMinutes(d.Seconds)}";
    }

    private void OnColumnClicked(int i)
    {
        if (_result is not { } r) return;
        if (_columns.SelectedIndex == i) { ClosePanel(); return; }
        _ = ShowDayPanel(r.Days[i].Date, null);
    }

    private static void StyleTip(ToolTip tip)
    {
        tip.SetResourceReference(Control.BackgroundProperty, "Brush.Panel");
        tip.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        tip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        tip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
    }

    /// <summary>First day of the week behind trend bar <paramref name="i"/>: the strip is anchored to the current week and runs oldest first.</summary>
    private static DateOnly TrendWeekStart(WeekResult r, int i) => Navigator(r).WeekAnchor(r.WeekTotals.Count - 1 - i);

    private void OnTrendHovered(int i, Point _)
    {
        if (i < 0 || _result is not { } r) { _trendTip.IsOpen = false; return; }
        var from = TrendWeekStart(r, i);
        _trendTip.Content = $"{UsageText.ShortDate(from)} – {UsageText.ShortDate(from.AddDays(6))}{Environment.NewLine}{UsageText.HoursMinutes(r.WeekTotals[i])}";
    }

    private void OnTrendClicked(int i)
    {
        if (_result is not { } r) return;
        var nav = Navigator(r);
        var target = nav.WeeksBack(nav.WeekAnchor(r.WeekTotals.Count - 1 - i));
        if (target == _weeksBack) return;
        _weeksBack = target;
        ClosePanel();
        Refresh();
    }

    private static DateOnly SparkDate(WeekResult r, WeekAppRow app, int i) => WeekQuery.SparkDay(r.Today, i, app.Spark.Count);

    private void OnSparkHovered(ToolTip tip, WeekAppRow app, int i)
    {
        if (i < 0 || _result is not { } r) { tip.IsOpen = false; return; }
        tip.Content = $"{app.Name}{Environment.NewLine}{UsageText.WeekdayDate(SparkDate(r, app, i), full: true)} · {UsageText.HoursMinutes(app.Spark[i])}";
    }

    /// <summary>
    /// Opens a day's panel and selects its column. A sparkline day outside the shown week first steps to that week and
    /// opens once it has loaded. A given app leads the list and the subtitle.
    /// </summary>
    private async Task ShowDayPanel(DateOnly day, WeekAppRow? focus)
    {
        if (_result is not { } r) return;
        var index = r.Days.ToList().FindIndex(d => d.Date == day);
        if (index < 0)
        {
            _weeksBack = Navigator(r).WeeksBack(day);
            _pendingDay = (day, focus?.AppId);
            Refresh();
            return;
        }
        _columns.SelectedIndex = index;
        Focus();
        var seq = ++_panelSeq;
        DayDetail? loaded = null;
        var applied = await _panelLoader.Refresh(c => DayDetailQuery.Run(c, day), d => loaded = d);
        if (!applied || seq != _panelSeq || loaded is not { } detail) return; // overtaken, view stopped, no database, or the panel was closed meanwhile

        var content = PanelContents.ForDay(detail, focus is null ? null : (focus.AppId, focus.Name));
        var compares = content.Comparisons.Select(x => (
            Str(x.Kind == PanelComparisonKind.VsWeekAverage ? PanelStringKeys.DayVsAverage : PanelStringKeys.DayVsLastWeek),
            UsageText.SignedHoursMinutes(x.DeltaSeconds),
            x.Good)).ToList();
        var span = content is { SpanStartUtc: { } a, SpanEndUtc: { } b } ? Str(PanelStringKeys.DaySpan, Clock(a), Clock(b)) : "";
        var apps = content.Apps.Select(x => (x.Name, x.Seconds, _themes.AppSlot(x.AppId))).ToList();
        var subtitle = UsageText.HoursMinutes(content.TotalSeconds) + span;
        if (content.Focus is { } f)
            subtitle = Str(PanelStringKeys.DayFocus, f.Name,
                f.Seconds > 0 ? UsageText.HoursMinutes(f.Seconds) : Str(PanelStringKeys.DayNotUsed), UsageText.HoursMinutes(content.TotalSeconds), span);
        _panel.Show(
            UsageText.WeekdayDate(day, full: true),
            subtitle,
            apps,
            [],
            compares,
            Str(PanelStringKeys.DayOpenInToday),
            _openDay is null ? null : () => _openDay(day));
        _panel.BringIntoView();
    }

    private UIElement BuildRow(WeekAppRow app, int paletteIndex)
    {
        var appBrush = "Brush.App" + paletteIndex;

        var tile = AppTile.Tracked(_themes, app.AppId, app.Name, TileStyle.WeekRow);

        var name = new TextBlock { Text = app.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

        var spark = new SparkBars { Values = app.Spark, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        spark.SetResourceReference(SparkBars.BarBrushProperty, appBrush);
        var sparkTip = new ToolTip();
        StyleTip(sparkTip);
        spark.ToolTip = sparkTip;
        spark.Cursor = System.Windows.Input.Cursors.Hand;
        ToolTipService.SetInitialShowDelay(spark, 80);
        ToolTipService.SetPlacement(spark, System.Windows.Controls.Primitives.PlacementMode.Top);
        spark.BarHovered += (i, _) => OnSparkHovered(sparkTip, app, i);
        spark.BarClicked += i => { if (_result is { } r) _ = ShowDayPanel(SparkDate(r, app, i), app); };

        var time = new TextBlock { Text = UsageText.HoursMinutes(app.Seconds), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, MinWidth = 56, TextAlignment = TextAlignment.Right };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        time.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

        var grid = new Grid();
        grid.SetResourceReference(FrameworkElement.MarginProperty, "Thickness.RowList");
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(spark, 2);
        Grid.SetColumn(time, 3);
        grid.Children.Add(tile);
        grid.Children.Add(name);
        grid.Children.Add(spark);
        grid.Children.Add(time);
        return grid;
    }
}
