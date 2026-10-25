using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };

    private TextBlock _readout = Label(); // rebuilt with each redraw (an element has one parent)
    private AppDetailResult? _result;
    private int _year = DateTime.Now.Year;
    private DayOfWeek _weekStart = DayOfWeek.Monday;
    private int _seq;

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
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _live.Changed += Refresh;
            _themes.Applied += Redraw;
            AppIcons.Updated += Redraw;
            _live.Start();
            _timer.Start();
            Redraw();
            Refresh();
            Focus();
        };
        Unloaded += (_, _) =>
        {
            _live.Changed -= Refresh;
            _themes.Applied -= Redraw;
            AppIcons.Updated -= Redraw;
            _timer.Stop();
        };
    }

    private async void Refresh()
    {
        var seq = ++_seq;
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        var (path, id, year) = (_dbPath, _appId, _year);
        AppDetailResult? result;
        try
        {
            (result, _weekStart) = await Task.Run(() => (AppDetailQuery.Run(path, now, TimeZoneInfo.Local, id, year, running),
                MonthQuery.ParseWeekStart(SettingsStore.Get(path, "week.start"))));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { return; }
        if (seq != _seq) return;
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
        var card = YearCard(r);
        var note = Label();
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        note.Text = r.PeakDaySeconds > 0
            ? Str(AppDetailStringKeys.Footnote, DurationFormat.HoursMinutes(r.PeakDaySeconds))
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

        var prev = YearButton(left: true, enabled: r.Year > r.FirstYear, AppDetailStringKeys.PrevYear, () => SwitchYear(r.Year - 1));
        var next = YearButton(left: false, enabled: r.Year < r.LastYear, AppDetailStringKeys.NextYear, () => SwitchYear(r.Year + 1));
        var year = new TextBlock { Text = r.Year.ToString(En), MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
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
        var tile = new Border { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        if (!AppIcons.TryFill(tile, r.AppId))
        {
            tile.SetResourceReference(Border.BackgroundProperty, AppBrush(r.AppId));
            var letter = new TextBlock
            {
                Text = r.Name.Length > 0 ? char.ToUpperInvariant(r.Name[0]).ToString() : "?", FontWeight = FontWeights.Bold, FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            letter.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Surface");
            letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            tile.Child = letter;
        }

        var name = new TextBlock { Text = r.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        var meta = Label();
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Text = r.FirstTracked is { } first
            ? Str(AppDetailStringKeys.Meta, r.ExeName, first.ToString(first.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", En))
            : r.ExeName;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(name);
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
            Tile(Str(AppDetailStringKeys.AvgSession), r.SessionCount > 0 ? DurationFormat.HoursMinutes(r.AverageSessionSeconds) : Str(AppDetailStringKeys.Never),
                Str(AppDetailStringKeys.Longest, DurationFormat.HoursMinutes(r.LongestSessionSeconds))),
            Tile(Str(AppDetailStringKeys.TimesOpened), r.SessionCount.ToString(En),
                Str(AppDetailStringKeys.TotalIn, Str(AllStringKeys.Hours, AllFormat.Hours(r.TotalSeconds)))),
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
        var day = DateOnly.FromDateTime(Local(last.StartUtc));
        var today = DateOnly.FromDateTime(DateTime.Now);
        var when = day == today ? Str(AppDetailStringKeys.Today)
            : day == today.AddDays(-1) ? Str(AppDetailStringKeys.Yesterday)
            : day.ToString(day.Year == today.Year ? "ddd d MMM" : "d MMM yyyy", En);
        return (when, Str(AppDetailStringKeys.LastSub, Clock(last.StartUtc), DurationFormat.HoursMinutes(last.Seconds)));
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
        var hours = new TextBlock { Text = Str(AppDetailStringKeys.YearHours, AllFormat.Hours(r.YearSeconds)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
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

        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(months, 1);
        Grid.SetRow(footerBox, 2);
        inner.Children.Add(head);
        inner.Children.Add(months);
        inner.Children.Add(footerBox);

        var card = new Border { Padding = new Thickness(14, 12, 14, 10), BorderThickness = new Thickness(1), Child = inner };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        return card;
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
            var first = new DateOnly(r.Year, m, 1);
            var lead = ((int)first.DayOfWeek - (int)weekStart + 7) % 7;
            var slots = new AppDetailDay?[42];
            long seconds = 0;
            for (var d = 0; d < DateTime.DaysInMonth(r.Year, m); d++)
            {
                var day = byDate[first.AddDays(d)];
                slots[lead + d] = day;
                seconds += day.Seconds;
            }

            var cells = new MonthCells { Days = slots, HeatBrushes = heat, OutlineBrush = outline };
            cells.HoverChanged += d => _readout.Text = d is null ? Str(AppDetailStringKeys.Hint) : DayText(d);

            var name = Label();
            name.Text = first.ToString("MMM", En).ToUpperInvariant();
            name.FontSize = 10;
            var total = Label();
            total.Text = seconds > 0 ? Str("matrixHours", Math.Round(seconds / 3600.0).ToString("0", En)) : "";
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
        var date = d.Date.ToString("ddd d MMM", En);
        return d.Kind switch
        {
            AppDetailDayKind.Future => Str(AppDetailStringKeys.NotYet, date),
            AppDetailDayKind.BeforeTracking => Str(AppDetailStringKeys.BeforeTracking, date),
            _ when d.Seconds <= 0 => Str(AppDetailStringKeys.NotOpened, date),
            _ => Str(AppDetailStringKeys.DayTracked, date, DurationFormat.HoursMinutes(d.Seconds),
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

    private string AppBrush(long appId) => "Brush.App" + (int)(appId % Math.Max(1, _themes.Current.Apps.Count));

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
