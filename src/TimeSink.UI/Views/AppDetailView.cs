using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// App detail page (board "TimeSink App Detail"): a full-flyout overlay opened by double-clicking a row in the All tab.
/// Shows the app's own icon, three lifetime tiles (last opened, average session, times opened) and a year grid of
/// twelve small month calendars coloured against the app's busiest day. Built in code like <see cref="AllView"/>;
/// colours, fonts and text come from theme resources. Data loads off-thread on open, every 30 s and on pipe state change.
/// </summary>
public sealed class AppDetailView : UserControl
{
    private const int RefreshEverySeconds = 30;
    private static readonly CultureInfo En = CultureInfo.InvariantCulture; // English names in v1 (spec 8.1)

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly long _appId;
    private readonly Action _close;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;

    private TextBlock _readout = Label(); // rebuilt with each redraw (an element has one parent)
    private AppDetailResult? _result;
    private int _year;
    private DayOfWeek _weekStart = DayOfWeek.Monday;
    private bool _bars;
    private AppDetailRange _range = AppDetailRange.Year;
    private DateOnly _anchor;

    public AppDetailView(string dbPath, ThemeService themes, LiveState live, long appId, Action close)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _appId = appId;
        _close = close;
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        Focusable = true;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _close(); e.Handled = true; } };
        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached);
        (_year, _anchor) = (_loader.Today.Year, _loader.Today);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw);
        Loaded += (_, _) =>
        {
            Redraw();
            _driver.Start();
            Focus();
        };
        Unloaded += (_, _) => _driver.Stop();
    }

    private async void Refresh()
    {
        var (id, year) = (_appId, _year);
        AppDetailResult? result = null;
        var applied = await _loader.Refresh(
            c => (Result: AppDetailQuery.Run(c, id, year), WeekStart: c.WeekStartDay),
            r =>
            {
                result = r.Result;
                _weekStart = r.WeekStart;
            });
        if (!applied) return;
        if (result is null) { _close(); return; } // the app was retired or removed meanwhile
        _result = result;
        _year = result.Year;
        Redraw();
    }

    private void SwitchYear(int year)
    {
        _year = year;
        Refresh();
    }

    // ---- drawing ----

    private void Redraw()
    {
        var root = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        for (var i = 0; i < 5; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Content = root;
        var r = _result;
        root.Children.Add(BackRow(r));
        if (r is null) return;

        AppIcons.Load(_dbPath, [r.AppId]);
        var header = Header(r);
        var tiles = Tiles(r);
        var card = _bars ? BarsCard(r) : YearCard(r);
        var note = Label();
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        note.Text = _bars ? ""
            : r.PeakDaySeconds > 0
            ? Str(AppDetailStringKeys.Footnote, UsageText.HoursMinutes(r.PeakDaySeconds))
            : Str(AppDetailStringKeys.FootnoteNone, r.Year.ToString(En));
        header.Margin = new Thickness(0, 10, 0, 0);
        tiles.Margin = new Thickness(0, 10, 0, 0);
        card.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(header, 1);
        Grid.SetRow(tiles, 2);
        Grid.SetRow(card, 3);
        Grid.SetRow(note, 4);
        root.Children.Add(header);
        root.Children.Add(tiles);
        root.Children.Add(card);
        root.Children.Add(note);
    }

    private FrameworkElement BackRow(AppDetailResult? r)
    {
        var row = new Grid { Height = 32 };
        var back = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 0, 0, 0) };
        back.SetResourceReference(StyleProperty, "LinkButton");
        back.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        var text = new TextBlock { Text = Str(AppDetailStringKeys.Back), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        text.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Chevron(left: true, "Brush.Accent", 16));
        content.Children.Add(text);
        back.Content = content;
        back.Click += (_, _) => _close();
        row.Children.Add(back);
        if (r is null) return row;

        Button prev, next;
        string label;
        if (_bars)
        {
            var d = ChartData(r);
            prev = YearButton(left: true, enabled: d.CanPrev, AppDetailStringKeys.PrevPeriod, () => { _anchor = AppDetailChart.Step(_range, d.From, -1); Redraw(); });
            next = YearButton(left: false, enabled: d.CanNext, AppDetailStringKeys.NextPeriod, () => { _anchor = AppDetailChart.Step(_range, d.From, 1); Redraw(); });
            label = _range == AppDetailRange.All ? "" : PeriodLabel(d);
        }
        else
        {
            prev = YearButton(left: true, enabled: r.Year > r.FirstYear, AppDetailStringKeys.PrevYear, () => SwitchYear(r.Year - 1));
            next = YearButton(left: false, enabled: r.Year < r.LastYear, AppDetailStringKeys.NextYear, () => SwitchYear(r.Year + 1));
            label = r.Year.ToString(En);
        }
        if (_bars && _range == AppDetailRange.All) return row; // a single period: nothing to step through
        var year = new TextBlock { Text = label, MinWidth = _bars ? 96 : 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        year.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        year.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        year.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        strip.Children.Add(prev);
        strip.Children.Add(year);
        strip.Children.Add(next);
        var pill = new Border { HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = strip };
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        pill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        row.Children.Add(pill);
        return row;
    }

    private static Button YearButton(bool left, bool enabled, string labelKey, Action click)
    {
        var b = new Button { Width = 28, Height = 28, Content = Chevron(left, enabled ? "Brush.TextSoft" : "Brush.Border", 14), IsEnabled = enabled };
        b.SetResourceReference(StyleProperty, "LinkButton");
        b.Padding = new Thickness(0);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(b, Str(labelKey));
        b.Click += (_, _) => click();
        return b;
    }

    private static Path Chevron(bool left, string brushKey, double size)
    {
        var p = new Path
        {
            Data = Geometry.Parse(left ? "M10,3 L5,8 L10,13" : "M6,3 L11,8 L6,13"),
            Width = size, Height = size, Stretch = Stretch.Uniform, StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        p.SetResourceReference(Shape.StrokeProperty, brushKey);
        return p;
    }

    private FrameworkElement Header(AppDetailResult r)
    {
        // The app's own icon fills the tile; only an app without one keeps the coloured letter tile.
        var tile = AppTile.Tracked(_themes, r.AppId, r.Name, TileStyle.Header);

        var name = new TextBlock { Text = r.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        var meta = Label();
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Text = r.FirstTracked is { } first
            ? Str(AppDetailStringKeys.Meta, r.ExeName, UsageText.ShortDate(first, withYear: first.Year != _loader.Today.Year))
            : r.ExeName;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (r.Background)
        {
            var title = new DockPanel { LastChildFill = true };
            var badge = BackgroundBadge.Create();
            DockPanel.SetDock(badge, Dock.Right);
            title.Children.Add(badge);
            title.Children.Add(name);
            text.Children.Add(title);
        }
        else text.Children.Add(name);
        text.Children.Add(meta);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        row.Children.Add(text);
        return row;
    }

    private FrameworkElement Tiles(AppDetailResult r)
    {
        var (lastValue, lastSub) = LastOpened(r.Last);
        var grid = new Grid();
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tiles = new[]
        {
            Tile(Str(AppDetailStringKeys.LastOpened), lastValue, lastSub),
            Tile(Str(AppDetailStringKeys.AvgSession), r.SessionCount > 0 ? UsageText.HoursMinutes(r.AverageSessionSeconds) : Str(AppDetailStringKeys.Never),
                Str(AppDetailStringKeys.Longest, UsageText.HoursMinutes(r.LongestSessionSeconds))),
            Tile(Str(AppDetailStringKeys.TimesOpened), r.SessionCount.ToString(En),
                Str(AppDetailStringKeys.TotalIn, Str(AllStringKeys.Hours, UsageText.Hours(r.TotalSeconds)))),
        };
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            Grid.SetColumn(tiles[i], i);
            grid.Children.Add(tiles[i]);
        }
        return grid;
    }

    private (string Value, string Sub) LastOpened(AppDetailLast? last)
    {
        if (last is null) return (Str(AppDetailStringKeys.Never), "");
        if (last.Running) return (Str(AppDetailStringKeys.RunningNow), Str(AppDetailStringKeys.LastSubRunning, Clock(last.StartUtc)));
        var bucket = UsageText.DayOf(last.StartUtc, _loader.Now.ToUnixTimeSeconds(), _loader.Zone);
        var when = bucket.Kind switch
        {
            DayKind.Today => Str(AppDetailStringKeys.Today),
            DayKind.Yesterday => Str(AppDetailStringKeys.Yesterday),
            _ => bucket.InCurrentYear ? UsageText.WeekdayDayMonth(bucket.Date) : UsageText.ShortDate(bucket.Date, withYear: true),
        };
        return (when, Str(AppDetailStringKeys.LastSub, Clock(last.StartUtc), UsageText.HoursMinutes(last.Seconds)));
    }

    private static FrameworkElement Tile(string label, string value, string sub)
    {
        var l = Label();
        l.Text = label;
        l.FontSize = 10;
        var v = new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        v.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        v.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        var s = Label();
        s.Text = sub;
        s.Margin = new Thickness(0, 2, 0, 0);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        var stack = new StackPanel();
        stack.Children.Add(l);
        stack.Children.Add(v);
        stack.Children.Add(s);
        var box = new Border { Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), Child = stack };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return box;
    }

    private FrameworkElement YearCard(AppDetailResult r)
    {
        var title = new TextBlock { Text = Str(AppDetailStringKeys.YearCaption, r.Year.ToString(En)), FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var hours = new TextBlock { Text = Str(AppDetailStringKeys.YearHours, UsageText.Hours(r.YearSeconds)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        hours.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        hours.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        hours.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var head = new Grid();
        head.Children.Add(title);
        head.Children.Add(hours);

        var months = Months(r);
        months.Margin = new Thickness(0, 12, 0, 0);

        _readout = Label();
        _readout.Text = Str(AppDetailStringKeys.Hint);
        _readout.TextTrimming = TextTrimming.CharacterEllipsis;
        _readout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _readout.VerticalAlignment = VerticalAlignment.Center;
        var legend = Legend();
        Grid.SetColumn(legend, 1);
        footer.Children.Add(_readout);
        footer.Children.Add(legend);
        var footerBox = new Border { Padding = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Child = footer };
        footerBox.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        return CardFrame(head, Toolbar(), months, footerBox);
    }

    /// <summary>The card shared by both chart views: head, toolbar, a content area that takes the spare height, and the readout footer.</summary>
    private static FrameworkElement CardFrame(FrameworkElement head, FrameworkElement toolbar, FrameworkElement content, FrameworkElement footer)
    {
        toolbar.Margin = new Thickness(0, 8, 0, 0);
        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(content, 2);
        Grid.SetRow(footer, 3);
        inner.Children.Add(head);
        inner.Children.Add(toolbar);
        inner.Children.Add(content);
        inner.Children.Add(footer);

        var card = new Border { Padding = new Thickness(14, 12, 14, 10), BorderThickness = new Thickness(1), Child = inner };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        return card;
    }

    // ---- bar chart view ----

    private FrameworkElement Toolbar()
    {
        var bar = new DockPanel { LastChildFill = false };
        var view = Segmented([Str(AppDetailStringKeys.ViewCalendar), Str(AppDetailStringKeys.ViewBars)], _bars ? 1 : 0, i => { _bars = i == 1; Redraw(); });
        DockPanel.SetDock(view, Dock.Left);
        bar.Children.Add(view);
        if (_bars)
        {
            string[] names = [Str(AppDetailStringKeys.RangeAll), Str(AppDetailStringKeys.RangeYear), Str(AppDetailStringKeys.RangeMonth), Str(AppDetailStringKeys.RangeWeek)];
            var range = CycleButton(names[(int)_range], () => { _range = AppDetailChart.NextRange(_range); _anchor = Today(); Redraw(); });
            DockPanel.SetDock(range, Dock.Right);
            bar.Children.Add(range);
        }
        return bar;
    }

    private DateOnly Today() => _loader.Today;

    private AppDetailChartData ChartData(AppDetailResult r) =>
        AppDetailChart.Build(_range, _anchor, Today(), r.FirstTracked, _weekStart,
            r.DaySeconds ?? new Dictionary<DateOnly, long>(), r.Opens ?? []);

    private string PeriodLabel(AppDetailChartData d) => _range switch
    {
        AppDetailRange.All => Str(AppDetailStringKeys.AllTime),
        AppDetailRange.Year => d.From.Year.ToString(En),
        AppDetailRange.Month => UsageText.MonthYear(d.From),
        _ => $"{UsageText.ShortDate(d.From)} – {UsageText.ShortDate(d.To)}",
    };

    private FrameworkElement BarsCard(AppDetailResult r)
    {
        var d = ChartData(r);
        var title = new TextBlock { Text = PeriodLabel(d), FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var hours = new TextBlock { Text = Str(AppDetailStringKeys.YearHours, UsageText.Hours(d.TotalSeconds)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        hours.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        hours.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        hours.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var head = new Grid();
        head.Children.Add(title);
        head.Children.Add(hours);

        _readout = Label();
        _readout.Text = Str(AppDetailStringKeys.BarsHint);
        _readout.TextTrimming = TextTrimming.CharacterEllipsis;
        _readout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        var footerBox = new Border { Padding = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, Child = _readout };
        footerBox.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        var peak = d.Bars.Max(b => b.Seconds);
        var barStrip = BarStrip(d.Bars.Select(b => peak > 0 ? (double)b.Seconds / peak : 0).ToList(), LabelsFor(d.Bars.Select(b => b.Label).ToList()),
            i => _readout.Text = i is { } k ? Str(AppDetailStringKeys.BarTip, BarName(d.Bars[k]), UsageText.HoursMinutes(d.Bars[k].Seconds)) : Str(AppDetailStringKeys.BarsHint),
            height: 120, maxBarWidth: 36);
        barStrip.Margin = new Thickness(0, 12, 0, 0);

        var tod = TimeOfDay(d);
        tod.Margin = new Thickness(0, 16, 0, 8);
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        content.Children.Add(barStrip);
        content.Children.Add(tod);
        System.Windows.Automation.AutomationProperties.SetName(content, PeriodLabel(d));
        return CardFrame(head, Toolbar(), content, footerBox);
    }

    private string BarName(AppDetailBar b) => _range switch
    {
        AppDetailRange.All => b.From.Year.ToString(En),
        AppDetailRange.Year => UsageText.MonthYear(b.From),
        _ => UsageText.WeekdayDayMonth(b.From),
    };

    /// <summary>Thins the axis labels so a month of day numbers stays readable: every label up to 12 bars, otherwise every fifth.</summary>
    private static List<string?> LabelsFor(IReadOnlyList<string> all) =>
        all.Select((l, i) => all.Count <= 12 || i % 5 == 0 ? l : null).ToList();

    private FrameworkElement TimeOfDay(AppDetailChartData d)
    {
        var caption = Label();
        caption.Text = Str(AppDetailStringKeys.TimeOfDay);
        caption.FontSize = 10;
        var summary = new TextBlock { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
        summary.SetResourceReference(TextBlock.ForegroundProperty, d.PeakHour is null ? "Brush.TextMuted" : "Brush.Accent");
        summary.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        summary.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        summary.Text = d.PeakHour is { } h
            ? Str(AppDetailStringKeys.OpenedAround, ClockFormat.Clock(new DateTime(2000, 1, 1, h, 0, 0), CultureInfo.CurrentCulture))
            : Str(AppDetailStringKeys.OpenedNone);
        var head = new Grid();
        head.Children.Add(caption);
        head.Children.Add(summary);

        var most = d.OpensByHour.Max();
        var labels = Enumerable.Range(0, 24).Select(h => h % 6 == 0 ? h.ToString("00", En) : null).ToList();
        var strip = BarStrip(d.OpensByHour.Select(n => most > 0 ? (double)n / most : 0).ToList(), labels,
            i => summary.Text = i is { } k
                ? Str(AppDetailStringKeys.BarTip, ClockFormat.Clock(new DateTime(2000, 1, 1, k, 0, 0), CultureInfo.CurrentCulture), Opens(d.OpensByHour[k]))
                : d.PeakHour is { } p ? Str(AppDetailStringKeys.OpenedAround, ClockFormat.Clock(new DateTime(2000, 1, 1, p, 0, 0), CultureInfo.CurrentCulture)) : Str(AppDetailStringKeys.OpenedNone),
            accentPeak: d.PeakHour, height: 44);
        strip.Margin = new Thickness(0, 6, 0, 0);

        var box = new StackPanel();
        box.Children.Add(head);
        box.Children.Add(strip);
        return box;
    }

    private static string Opens(int n) => n == 1 ? Str(AppDetailStringKeys.SessionOne) : Str(AppDetailStringKeys.SessionMany, n);

    /// <summary>
    /// A bottom-aligned bar per value (0..1 of the strip height) with a sparse label row. A fixed <paramref name="height"/>
    /// gives a compact strip, otherwise the strip takes the height it is given. Hovering a bar reports its index.
    /// </summary>
    private static FrameworkElement BarStrip(IReadOnlyList<double> values, IReadOnlyList<string?> labels, Action<int?> hover, int? accentPeak = null, double? height = null, double maxBarWidth = double.PositiveInfinity)
    {
        var bars = new UniformGrid { Columns = values.Count, Rows = 1 };
        var names = new UniformGrid { Columns = values.Count, Rows = 1 };
        for (var i = 0; i < values.Count; i++)
        {
            var index = i;
            var cell = new Grid { Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0), MaxWidth = maxBarWidth };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - values[i], GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(values[i], GridUnitType.Star) });
            var fill = new Border { MinHeight = values[i] > 0 ? 2 : 0 };
            fill.SetResourceReference(Border.BackgroundProperty, accentPeak is { } p && p != i ? "Brush.Heat2" : "Brush.Accent");
            fill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            Grid.SetRow(fill, 1);
            cell.Children.Add(fill);
            cell.MouseEnter += (_, _) => hover(index);
            cell.MouseLeave += (_, _) => hover(null);
            bars.Children.Add(cell);

            var name = Label();
            name.Text = labels[i] ?? "";
            name.FontSize = 9;
            name.TextAlignment = TextAlignment.Center;
            name.TextWrapping = TextWrapping.NoWrap;
            names.Children.Add(name);
        }
        var baseline = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bars };
        baseline.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        if (height is { } h) baseline.Height = h;

        var strip = new Grid();
        strip.RowDefinitions.Add(new RowDefinition { Height = height is null ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        strip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        names.Margin = new Thickness(0, 3, 0, 0);
        Grid.SetRow(names, 1);
        strip.Children.Add(baseline);
        strip.Children.Add(names);
        return strip;
    }

    /// <summary>One accent-filled button showing the current choice; each click moves to the next one.</summary>
    private static FrameworkElement CycleButton(string text, Action click)
    {
        var label = new TextBlock { Text = text + " ↻", Margin = new Thickness(12, 5, 12, 5), FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Surface");
        label.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        // A plain border, not a LinkButton: its hover fill would swallow the dark label on the accent.
        var pill = new Border { Child = label, Cursor = Cursors.Hand };
        pill.MouseLeftButtonUp += (_, _) => click();
        pill.MouseEnter += (_, _) => pill.Opacity = 0.85;
        pill.MouseLeave += (_, _) => pill.Opacity = 1;
        System.Windows.Automation.AutomationProperties.SetName(pill, text);
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        pill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return pill;
    }

    /// <summary>A small pill of mutually exclusive choices; the picked one is filled with the accent.</summary>
    private static FrameworkElement Segmented(IReadOnlyList<string> labels, int selected, Action<int> pick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var on = i == selected;
            var text = new TextBlock { Text = labels[i], Margin = new Thickness(8, 3, 8, 3), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Surface" : "Brush.TextSoft");
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            text.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            var seg = new Border { Child = text, Cursor = on ? Cursors.Arrow : Cursors.Hand };
            seg.MouseLeftButtonUp += (_, _) => { if (!on) pick(index); };
            seg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            if (on) seg.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            row.Children.Add(seg);
        }
        var pill = new Border { Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = row };
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        pill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return pill;
    }

    private FrameworkElement Months(AppDetailResult r)
    {
        var heat = Enumerable.Range(0, 5).Select(i => (Brush)FindResource("Brush.Heat" + i)).ToList();
        var outline = (Brush)FindResource("Brush.Border");
        var weekStart = _weekStart;
        var byDate = r.Days.ToDictionary(d => d.Date);

        var grid = new UniformGrid4();
        for (var m = 1; m <= 12; m++)
        {
            var month = MonthGrid.Layout(r.Year, m, weekStart);
            var first = month.First;
            var slots = new AppDetailDay?[42];
            long seconds = 0;
            for (var d = 0; d < month.Days; d++)
            {
                var day = byDate[first.AddDays(d)];
                slots[month.Lead + d] = day;
                seconds += day.Seconds;
            }

            var cells = new MonthCells { Days = slots, HeatBrushes = heat, OutlineBrush = outline };
            cells.HoverChanged += d => _readout.Text = d is null ? Str(AppDetailStringKeys.Hint) : DayText(d);

            var name = Label();
            name.Text = UsageText.MonthAbbr(first.Month).ToUpperInvariant();
            name.FontSize = 10;
            var total = Label();
            total.Text = seconds > 0 ? Str("matrixHours", UsageText.Hours(seconds)) : "";
            total.FontSize = 10;
            total.HorizontalAlignment = HorizontalAlignment.Right;
            var label = new Grid { Width = 7 * MonthCells.Cell + 6 * MonthCells.Gap, Margin = new Thickness(0, 0, 0, 5) };
            label.Children.Add(name);
            label.Children.Add(total);

            var block = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            block.Children.Add(label);
            block.Children.Add(cells);
            grid.Children.Add(block);
        }
        System.Windows.Automation.AutomationProperties.SetName(grid, Str(AppDetailStringKeys.YearCaption, r.Year.ToString(En)));
        return grid;
    }

    private static string DayText(AppDetailDay d)
    {
        var date = UsageText.WeekdayDayMonth(d.Date);
        return d.Kind switch
        {
            AppDetailDayKind.Future => Str(AppDetailStringKeys.NotYet, date),
            AppDetailDayKind.BeforeTracking => Str(AppDetailStringKeys.BeforeTracking, date),
            _ when d.Seconds <= 0 => Str(AppDetailStringKeys.NotOpened, date),
            _ => Str(AppDetailStringKeys.DayTracked, date, UsageText.HoursMinutes(d.Seconds),
                d.Sessions == 1 ? Str(AppDetailStringKeys.SessionOne) : Str(AppDetailStringKeys.SessionMany, d.Sessions)),
        };
    }

    private FrameworkElement Legend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var less = Label();
        less.Text = Str(StringKeys.MonthLess);
        less.FontSize = 10;
        less.Margin = new Thickness(0, 0, 4, 0);
        row.Children.Add(less);
        for (var i = 0; i < 5; i++)
        {
            var sw = new Border { Width = MonthCells.Cell, Height = MonthCells.Cell, Margin = new Thickness(0, 0, i < 4 ? 3 : 0, 0) };
            sw.SetResourceReference(Border.BackgroundProperty, "Brush.Heat" + i);
            sw.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            row.Children.Add(sw);
        }
        var more = Label();
        more.Text = Str(StringKeys.MonthMore);
        more.FontSize = 10;
        more.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(more);
        return row;
    }

    // ---- helpers ----


    private static TextBlock Label()
    {
        var t = new TextBlock();
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        return t;
    }

    /// <summary>Four month blocks per row, filled left to right (the grid's width is shared evenly between the columns).</summary>
    private sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid4() { Columns = 4; VerticalAlignment = VerticalAlignment.Top; }
    }
}
