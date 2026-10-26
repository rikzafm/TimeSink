using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
/// All tab (board PulseAllTable): lifetime list with a big hours figure per app, avg session and last opened, a totals
/// row, dimmed retired rows and the CSV export link. Two switchable row layouts: labeled stats (small caps captions) and
/// icon stats (clock and restore glyphs). The "sorted by" text cycles the sort column and its arrow flips the direction.
/// Built in code on first visit; data loads only from <see cref="Start"/> (after the first frame), then every 30 s and on pipe state change.
/// </summary>
public sealed class AllView : UserControl, ILiveView
{
    private const int RefreshEverySeconds = 30;
    private const string LayoutKey = "all.layout"; // "labeled" (default) or "icons"
    private const string ClockPath = "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 M12,8 L12,12 L15,14";
    private const string RestorePath = "M4.5,12 A7.5,7.5 0 1 0 7,6.5 M4,3.5 L4,8.5 L9,8.5";
    private const string LayoutPath = "M4,4 L10,4 L10,10 L4,10 Z M13,6 L20,6 M13,9 L18,9 M4,14 L10,14 L10,20 L4,20 Z M13,16 L20,16 M13,19 L18,19";
    private static readonly CultureInfo En = CultureInfo.InvariantCulture; // English names in v1 (spec 8.1)

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly FlyoutWindow? _window;
    private readonly Action<long>? _openDetail;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;

    private readonly TextBlock _caption = Label(), _total = new(), _counts = Label();
    private readonly WrapPanel _sorted = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Dictionary<long, Border> _rowBoxes = new();
    private readonly Grid _head = new();
    private readonly StackPanel _rows = new();
    private readonly Grid _totals = new();
    private readonly Button _layoutToggle = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Shapes.Path _layoutGlyph = new() { Data = Geometry.Parse(LayoutPath), StrokeThickness = 1.6, Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    private AllResult? _result;
    private AllSort _sort = AllSort.Default;
    private bool _icons;
    private long? _selected;

    public AllView(string dbPath, ThemeService themes, LiveState live, FlyoutWindow? window, Action<long>? openDetail = null)
    {
        _openDetail = openDetail;
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _window = window;
        _icons = SettingsStore.Get(dbPath, LayoutKey) == "icons";

        Focusable = true;
        Loaded += (_, _) => Focus();
        PreviewMouseDown += (_, _) => Focus();
        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _total.FontWeight = FontWeights.SemiBold;
        _counts.HorizontalAlignment = HorizontalAlignment.Right;
        _counts.TextTrimming = TextTrimming.CharacterEllipsis;

        // Header: caption and layout switch, then the big lifetime total with counts and sort state on its right.
        _layoutToggle.SetResourceReference(StyleProperty, "LinkButton");
        _layoutToggle.Content = _layoutGlyph;
        _layoutToggle.Margin = new Thickness(0, 0, -6, 0);
        _layoutToggle.Click += (_, _) => SetLayout(!_icons);
        var captionRow = new Grid();
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _caption.TextTrimming = TextTrimming.CharacterEllipsis;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_layoutToggle, 1);
        captionRow.Children.Add(_caption);
        captionRow.Children.Add(_layoutToggle);

        var totalRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 4) };
        right.Children.Add(_counts);
        right.Children.Add(_sorted);
        Grid.SetColumn(right, 1);
        totalRow.Children.Add(_total);
        totalRow.Children.Add(right);
        var header = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        header.Children.Add(captionRow);
        header.Children.Add(totalRow);

        // Table frame: header row, scrolling body, totals row.
        _head.SetResourceReference(Panel.BackgroundProperty, "Brush.SurfaceAlt");
        _head.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.TableHead");
        var headLine = Line("Brush.Border", bottom: true);
        headLine.Child = _head;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _rows };
        _totals.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.TableTotals");
        var totalsBox = Line("Brush.Border", bottom: false);
        totalsBox.BorderThickness = new Thickness(0, 1, 0, 0);
        totalsBox.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        totalsBox.Child = _totals;
        var table = new Grid();
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 1);
        Grid.SetRow(totalsBox, 2);
        table.Children.Add(headLine);
        table.Children.Add(scroll);
        table.Children.Add(totalsBox);
        var frame = new Border { Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(1), ClipToBounds = true, Child = table };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        // ClipToBounds is a rectangle: clip the table to the frame's rounded inside so row fills don't poke out of the corners.
        table.SizeChanged += (_, e) =>
        {
            var radius = Math.Max(0, frame.CornerRadius.TopLeft - 1);
            table.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
        };

        var root = new Grid { Margin = new Thickness(16, 6, 16, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(frame, 1);
        root.Children.Add(header);
        root.Children.Add(frame);
        Content = root;

        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw,
            isIdle: () => _window is { IsRevealed: false });
        Unloaded += (_, _) => Stop();
        Redraw(); // static text and empty table; data follows in Start
    }

    public void Start() => _driver.Start();

    public void Stop() => _driver.Stop();

    public async void Refresh()
    {
        var path = _dbPath;
        if (await _loader.Refresh(c => AllQuery.Run(path, c.Now, c.Zone, c.Running, c.SignInUtc), r => _result = r)) Redraw();
    }

    private void SetLayout(bool icons)
    {
        _icons = icons;
        SettingsStore.Set(_dbPath, LayoutKey, icons ? "icons" : "labeled");
        Redraw();
    }

    // ---- drawing ----

    private void Redraw()
    {
        _caption.Text = Str(AllStringKeys.Caption);
        _layoutGlyph.SetResourceReference(Shape.StrokeProperty, _icons ? "Brush.Accent" : "Brush.TextMuted");
        System.Windows.Automation.AutomationProperties.SetName(_layoutToggle, Str(_icons ? AllStringKeys.LayoutIcons : AllStringKeys.LayoutLabeled));
        BuildHead();
        BuildSorted();
        var r = _result;
        if (r is null)
        {
            _total.Text = "";
            _counts.Inlines.Clear();
            _rows.Children.Clear();
            _rowBoxes.Clear();
            _totals.Children.Clear();
            return;
        }
        _total.Text = Str(AllStringKeys.TotalHours, UsageText.Hours(r.Totals.Seconds));
        SetBold(_counts, [(r.Apps.Count.ToString(En), true), (Str(AllStringKeys.TrackedSuffix), false),
            (r.Retired.Count.ToString(En), true), (Str(AllStringKeys.RetiredSuffix), false)]);

        AppIcons.Load(_dbPath, r.Apps.Select(a => a.AppId));
        var sorted = AllSorting.Apply(r.Apps, _sort);
        if (_selected is not { } sel || sorted.All(a => a.AppId != sel)) _selected = sorted.Count > 0 ? sorted[0].AppId : null;

        _rows.Children.Clear();
        _rowBoxes.Clear();
        if (sorted.Count == 0 && r.Retired.Count == 0)
        {
            var empty = Label();
            empty.Text = Str(AllStringKeys.Empty);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(12);
            _rows.Children.Add(empty);
        }
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (var i = 0; i < sorted.Count; i++) _rows.Children.Add(AppRow(sorted[i], sorted[i].AppId == _selected, nowUtc));
        if (r.Retired.Count > 0)
        {
            var head = Label();
            head.Text = Str(AllStringKeys.RetiredHeader).ToUpper(CultureInfo.CurrentCulture);
            head.Margin = new Thickness(16, 8, 16, 4);
            _rows.Children.Add(head);
            foreach (var x in r.Retired) _rows.Children.Add(RetiredRow(x));
        }
        BuildTotals(r);
    }

    /// <summary>"APP" on the left and "HOURS ↓" on the right (accent while the list is sorted by hours).</summary>
    private void BuildHead()
    {
        _head.Children.Clear();
        _head.ColumnDefinitions.Clear();
        _head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var app = HeadLabel(Str(AllStringKeys.ColApp), _sort.Column == AllColumn.App ? Arrow(_sort.Descending) : "", _sort.Column == AllColumn.App);
        var hours = HeadLabel(Str(AllStringKeys.ColHours), _sort.Column == AllColumn.Hours ? Arrow(_sort.Descending) : "", _sort.Column == AllColumn.Hours);
        app.HorizontalAlignment = HorizontalAlignment.Left;
        hours.HorizontalAlignment = HorizontalAlignment.Right;
        app.Margin = new Thickness(16, 0, 0, 0);
        hours.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(hours, 1);
        _head.Children.Add(app);
        _head.Children.Add(hours);
    }

    private static TextBlock HeadLabel(string name, string arrow, bool active)
    {
        var t = new TextBlock { Text = (name.ToUpper(CultureInfo.CurrentCulture) + (arrow.Length > 0 ? " " + arrow : "")), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        t.SetResourceReference(TextBlock.ForegroundProperty, active ? "Brush.Accent" : "Brush.TextMuted");
        return t;
    }

    /// <summary>"sorted by <b>hours</b> ↓": the name steps to the next sort column, the arrow flips the direction.</summary>
    private void BuildSorted()
    {
        _sorted.Children.Clear();
        _sorted.Children.Add(Muted(Str(AllStringKeys.SortedBy)));
        var name = Clickable(ColumnName(_sort.Column).ToLowerInvariant(), Str(AllStringKeys.ChangeSort), () => { _sort = _sort.Next(); Redraw(); });
        name.FontWeight = FontWeights.SemiBold;
        _sorted.Children.Add(name);
        _sorted.Children.Add(Muted(" "));
        _sorted.Children.Add(Clickable(Arrow(_sort.Descending), Str(AllStringKeys.FlipSort), () => { _sort = _sort with { Descending = !_sort.Descending }; Redraw(); }));
    }

    private static TextBlock Muted(string text)
    {
        var t = Label();
        t.Text = text;
        return t;
    }

    private static TextBlock Clickable(string text, string automationName, Action onClick)
    {
        var t = Label();
        t.Text = text;
        t.Cursor = System.Windows.Input.Cursors.Hand;
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        System.Windows.Automation.AutomationProperties.SetName(t, automationName);
        t.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return t;
    }

    // ---- rows ----

    private FrameworkElement AppRow(AllAppRow a, bool selected, long nowUtc)
    {
        var brush = _themes.AppBrushKey(a.AppId);
        var grid = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = Tile(a.Name, a.AppId, brush, 32);
        grid.Children.Add(tile);

        var name = new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(name);
        if (a.Background) // why this app ranks below the others
        {
            var badge = BackgroundBadge.CreateIcon(); // icon only: the text pill squeezed names down to ".."
            badge.Margin = new Thickness(6, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            nameRow.Children.Add(badge);
        }

        var avg = a.SessionCount > 0 ? UsageText.HoursMinutes((long)Math.Round(a.AverageSessionSeconds)) : Str(AllStringKeys.Never);
        var last = LastOpenedText(a.LastUsedUtc, nowUtc);
        var stats = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0) };
        if (_icons)
        {
            stats.Children.Add(IconStat(ClockPath, avg, Str(AllStringKeys.ColAvgSession)));
            stats.Children.Add(IconStat(RestorePath, last, Str(AllStringKeys.ColLastOpened)));
        }
        else
        {
            stats.Children.Add(LabeledStat(Str(AllStringKeys.ColAvgSession), avg));
            stats.Children.Add(LabeledStat(Str(AllStringKeys.ColLastOpened), last));
        }
        var middle = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(nameRow);
        middle.Children.Add(stats);
        // The badge sits right after the name; the name gives up width (and trims) before the badge is pushed out.
        var reserve = a.Background ? 26 : 0;
        middle.SizeChanged += (_, _) => name.MaxWidth = Math.Max(40, middle.ActualWidth - reserve);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);

        var hours = HoursFigure(UsageText.Hours(a.Seconds), big: true, _icons);
        Grid.SetColumn(hours, 2);
        grid.Children.Add(hours);

        var box = Line("Brush.BorderSoft", bottom: true);
        box.Padding = new Thickness(0, _icons ? 5 : 4, 0, _icons ? 5 : 4);
        box.Cursor = System.Windows.Input.Cursors.Hand;
        box.SetResourceReference(Border.BackgroundProperty, selected ? "Brush.BorderSoft" : "Brush.Surface");
        box.Child = grid;
        var id = a.AppId;
        _rowBoxes[id] = box;
        box.MouseLeftButtonUp += (_, _) => SelectRow(id);
        box.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) _openDetail?.Invoke(id); }; // double-click: app detail
        return box;
    }

    /// <summary>Moves the selection highlight between two rows; the table is not rebuilt for it.</summary>
    private void SelectRow(long id)
    {
        if (_selected == id) return;
        if (_selected is { } old && _rowBoxes.TryGetValue(old, out var oldBox)) oldBox.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _selected = id;
        if (_rowBoxes.TryGetValue(id, out var box)) box.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
    }

    /// <summary>The app's own icon at full tile size, or a coloured tile with its first letter until one is cached.</summary>
    private static Border Tile(string name, long appId, string brushKey, double size)
    {
        var tile = new Border { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, brushKey);
        tile.CornerRadius = new CornerRadius(size / 4);
        var letter = new TextBlock
        {
            Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Bold, FontSize = size * 0.42, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x1A)),
        };
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        if (!AppIcons.TryFill(tile, appId)) tile.Child = letter;
        return tile;
    }

    private static FrameworkElement LabeledStat(string label, string value)
    {
        var l = Label();
        l.Text = label.ToUpper(CultureInfo.CurrentCulture);
        l.FontSize = 9;
        l.TextTrimming = TextTrimming.CharacterEllipsis;
        var v = Cell(value, "Brush.TextSoft", "Double.FontSize.Small");
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        s.Children.Add(l);
        s.Children.Add(v);
        return s;
    }

    private static FrameworkElement IconStat(string pathData, string value, string automationName)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData), StrokeThickness = 1.8, Width = 11, Height = 11, Stretch = Stretch.Uniform,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
        };
        icon.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
        var v = Cell(value, "Brush.TextSoft", "Double.FontSize.Small");
        v.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0), ToolTip = automationName };
        System.Windows.Automation.AutomationProperties.SetName(s, automationName);
        s.Children.Add(icon);
        s.Children.Add(v);
        return s;
    }

    /// <summary>"42 h": the figure in the accent colour, the unit small and muted.</summary>
    private static FrameworkElement HoursFigure(string hours, bool big, bool tight = false)
    {
        var num = new TextBlock { Text = hours, FontWeight = FontWeights.Bold, FontSize = big ? 18 : 14, VerticalAlignment = VerticalAlignment.Center };
        num.SetResourceReference(TextBlock.ForegroundProperty, big ? "Brush.Accent" : "Brush.Text");
        num.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        var unit = new TextBlock { Text = " h", FontSize = big ? 12 : 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, big ? 3 : 1) };
        unit.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        unit.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        var s = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(num);
        s.Children.Add(unit);
        return s;
    }

    private static string LastOpenedText(long lastUtc, long nowUtc)
    {
        if (lastUtc <= 0) return Str(AllStringKeys.Never);
        var age = nowUtc - lastUtc;
        if (age < 60) return Str(AllStringKeys.JustNow);
        if (age < 3600) return Str(AllStringKeys.MinutesAgo, (age / 60).ToString(En));
        var bucket = UsageText.DayOf(lastUtc, nowUtc, TimeZoneInfo.Local);
        return bucket.Kind switch
        {
            DayKind.Today => Str(AllStringKeys.HoursAgo, (age / 3600).ToString(En)),
            DayKind.Yesterday => Str(AppDetailStringKeys.Yesterday),
            _ => bucket.Date.ToString(bucket.InCurrentYear ? "d MMM" : "d MMM yyyy", En),
        };
    }

    private FrameworkElement RetiredRow(AllRetiredRow x)
    {
        var grid = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = x.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var stopped = Label();
        stopped.Text = Str(AllStringKeys.Stopped, x.StoppedDate.ToString("d MMM", En));
        stopped.TextTrimming = TextTrimming.CharacterEllipsis;
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        left.Children.Add(name);
        left.Children.Add(stopped);
        var hours = HoursFigure(UsageText.Hours(x.Seconds), big: false);
        Grid.SetColumn(hours, 1);
        grid.Children.Add(left);
        grid.Children.Add(hours);
        grid.SetResourceReference(UIElement.OpacityProperty, "Double.Opacity.Retired");
        var box = Line("Brush.BorderSoft", bottom: true);
        box.Padding = new Thickness(0, 8, 0, 8);
        box.Child = grid;
        return box;
    }

    private void BuildTotals(AllResult r)
    {
        _totals.Children.Clear();
        _totals.ColumnDefinitions.Clear();
        _totals.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _totals.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Cell(Str(AllStringKeys.TotalRow), "Brush.Text", "Double.FontSize.Body");
        name.FontWeight = FontWeights.Bold;
        name.Margin = new Thickness(16, 0, 8, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var hours = Cell(Str(AllStringKeys.Hours, UsageText.Hours(r.Totals.Seconds)), "Brush.Accent", "Double.FontSize.Body");
        hours.FontWeight = FontWeights.Bold;
        hours.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(hours, 1);
        _totals.Children.Add(name);
        _totals.Children.Add(hours);
    }

    // ---- helpers ----


    private static string ColumnName(AllColumn c) => Str(c switch
    {
        AllColumn.App => AllStringKeys.ColApp,
        AllColumn.Hours => AllStringKeys.ColHours,
        AllColumn.AvgSession => AllStringKeys.ColAvgSession,
        AllColumn.LastOpened => AllStringKeys.ColLastOpened,
        AllColumn.DaysUsed => AllStringKeys.ColDays,
        AllColumn.AvgPerDay => AllStringKeys.ColAvg,
        _ => AllStringKeys.ColShare,
    });

    private static string Arrow(bool descending) => Str(descending ? AllStringKeys.ArrowDesc : AllStringKeys.ArrowAsc);

    private static Border Line(string brushKey, bool bottom)
    {
        var b = new Border { BorderThickness = bottom ? new Thickness(0, 0, 0, 1) : new Thickness(0) };
        b.SetResourceReference(Border.BorderBrushProperty, brushKey);
        return b;
    }

    private static TextBlock Cell(string text, string brushKey, string sizeKey)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        t.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return t;
    }

    private static TextBlock Label()
    {
        var t = new TextBlock();
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        return t;
    }

    /// <summary>Runs in the muted style with the flagged ones bold in the primary text colour.</summary>
    private static void SetBold(TextBlock target, IEnumerable<(string Text, bool Bold)> parts)
    {
        target.Inlines.Clear();
        foreach (var (text, bold) in parts)
        {
            var run = new Run(text);
            if (bold)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
            }
            target.Inlines.Add(run);
        }
    }
}
