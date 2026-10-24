using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// All tab (board PulseAllTable): lifetime table with sortable headers, a per-app detail line, a totals row, dimmed
/// retired rows and the CSV export link. Built in code on first visit; data loads only from <see cref="Start"/>
/// (after the first frame), then every 30 s and on pipe state change.
/// </summary>
public sealed class AllView : UserControl
{
    private const int RefreshEverySeconds = 30;
    private const double RetiredOpacity = 0.7;
    private static readonly CultureInfo En = CultureInfo.InvariantCulture; // English names in v1 (spec 8.1)
    private static readonly AllColumn[] Columns = [AllColumn.App, AllColumn.Hours, AllColumn.DaysUsed, AllColumn.AvgPerDay, AllColumn.Share];
    private static readonly double[] ColumnWidths = [1.7, 0.9, 0.8, 0.9, 1.2];

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly FlyoutWindow? _window;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };

    private readonly TextBlock _caption = Label(), _total = new(), _counts = Label(), _sorted = Label();
    private readonly Grid _head = Columns2();
    private readonly StackPanel _rows = new();
    private readonly Grid _totals = Columns2();
    private readonly TextBlock _note = Label();
    private readonly Button _export = new() { HorizontalAlignment = HorizontalAlignment.Right };

    private AllResult? _result;
    private AllSort _sort = AllSort.Default;
    private long? _selected;
    private int _seq;
    private bool _started;

    public AllView(string dbPath, ThemeService themes, LiveState live, FlyoutWindow? window)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _window = window;

        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _total.FontWeight = FontWeights.SemiBold;
        _note.TextWrapping = TextWrapping.Wrap;
        foreach (var t in new[] { _counts, _sorted }) t.HorizontalAlignment = HorizontalAlignment.Right;

        // Header: caption + big lifetime total on the left, counts and sort state on the right.
        var header = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var left = new StackPanel();
        left.Children.Add(_caption);
        left.Children.Add(_total);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        right.Children.Add(_counts);
        right.Children.Add(_sorted);
        header.Children.Add(left);
        header.Children.Add(right);

        // Table frame: header row, scrolling body, totals row.
        _head.SetResourceReference(Panel.BackgroundProperty, "Brush.SurfaceAlt");
        var headLine = Line("Brush.Border", bottom: true);
        headLine.Child = _head;
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _rows };
        _totals.Height = 38;
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

        var noteBox = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 8, 12, 8), MinHeight = 36, BorderThickness = new Thickness(1), Child = _note };
        noteBox.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        noteBox.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        noteBox.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        _export.SetResourceReference(StyleProperty, "LinkButton");
        _export.Margin = new Thickness(0, 2, -10, 0);
        _export.Click += (_, _) => Export();

        var root = new Grid { Margin = new Thickness(16, 6, 16, 4) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(frame, 1);
        Grid.SetRow(noteBox, 2);
        Grid.SetRow(_export, 3);
        root.Children.Add(header);
        root.Children.Add(frame);
        root.Children.Add(noteBox);
        root.Children.Add(_export);
        Content = root;

        _timer.Tick += (_, _) => { if (_window is not { IsRevealed: false }) Refresh(); };
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        Unloaded += (_, _) => Stop();
        Redraw(); // static text and empty table; data follows in Start
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _live.Start();
        _timer.Start();
        Refresh();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
    }

    public async void Refresh()
    {
        if (!_started) return;
        var seq = ++_seq;
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        var path = _dbPath;
        AllResult result;
        try { result = await Task.Run(() => AllQuery.Run(path, now, TimeZoneInfo.Local, running)); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { return; } // no DB yet
        if (seq != _seq || !_started) return;
        _result = result;
        Redraw();
    }

    // ---- drawing ----

    private void Redraw()
    {
        _caption.Text = Str(AllStringKeys.Caption);
        _export.Content = Str(AllStringKeys.Export);
        BuildHead();
        var r = _result;
        if (r is null)
        {
            _total.Text = "";
            _counts.Inlines.Clear();
            _rows.Children.Clear();
            _totals.Children.Clear();
            _note.Inlines.Clear();
            return;
        }
        _total.Text = Str(AllStringKeys.TotalHours, AllFormat.Hours(r.Totals.Seconds));
        SetBold(_counts, [(r.Apps.Count.ToString(En), true), (Str(AllStringKeys.TrackedSuffix), false),
            (r.Retired.Count.ToString(En), true), (Str(AllStringKeys.RetiredSuffix), false)]);
        SetBold(_sorted, [(Str(AllStringKeys.SortedBy), false), (ColumnName(_sort.Column).ToLowerInvariant(), true), (" " + Arrow(_sort.Descending), false)]);

        var sorted = AllSorting.Apply(r.Apps, _sort);
        if (_selected is not { } sel || sorted.All(a => a.AppId != sel)) _selected = sorted.Count > 0 ? sorted[0].AppId : null;

        _rows.Children.Clear();
        if (sorted.Count == 0 && r.Retired.Count == 0)
        {
            var empty = Label();
            empty.Text = Str(AllStringKeys.Empty);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(12);
            _rows.Children.Add(empty);
        }
        for (var i = 0; i < sorted.Count; i++) _rows.Children.Add(AppRow(sorted[i], i, sorted[i].AppId == _selected, r.Apps.Max(a => a.Seconds)));
        if (r.Retired.Count > 0)
        {
            var head = Label();
            head.Text = Str(AllStringKeys.RetiredHeader).ToUpper(CultureInfo.CurrentCulture);
            head.Margin = new Thickness(10, 8, 10, 4);
            _rows.Children.Add(head);
            foreach (var x in r.Retired) _rows.Children.Add(RetiredRow(x));
        }
        BuildTotals(r);
        BuildNote(r);
    }

    private void BuildHead()
    {
        _head.Children.Clear();
        for (var i = 0; i < Columns.Length; i++)
        {
            var col = Columns[i];
            var active = col == _sort.Column;
            var label = ColumnName(col).ToUpper(CultureInfo.CurrentCulture) + (active ? " " + Arrow(_sort.Descending) : "");
            var text = new TextBlock { Text = label, FontWeight = FontWeights.Bold, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(-6, 0, -6, 0) };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            text.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            var b = new Button { Content = text, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch };
            b.SetResourceReference(StyleProperty, "LinkButton");
            b.SetResourceReference(Control.ForegroundProperty, active ? "Brush.Accent" : "Brush.TextMuted");
            System.Windows.Automation.AutomationProperties.SetName(b, Str(AllStringKeys.SortBy, ColumnName(col)));
            b.Click += (_, _) => { _sort = _sort.Tap(col); Redraw(); };
            Grid.SetColumn(b, i);
            _head.Children.Add(b);
        }
    }

    private FrameworkElement AppRow(AllAppRow a, int index, bool selected, long maxSeconds)
    {
        var grid = Columns2();
        grid.Height = 40;
        grid.Children.Add(NameCell(a.Name, AppBrush(a.AppId), dashed: false));
        AddNumber(grid, 1, AllFormat.Hours(a.Seconds), bold: true, "Brush.Text");
        AddNumber(grid, 2, a.DaysUsed.ToString(En), bold: false, "Brush.TextSoft");
        AddNumber(grid, 3, AllFormat.AveragePerDay(a.AverageSecondsPerDay), bold: false, "Brush.TextSoft");

        // Share: a 30 x 4 bar scaled to the biggest app, then the percentage.
        var track = new Border { Width = 30, Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
        track.CornerRadius = new CornerRadius(2);
        var fill = new Border { Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Width = maxSeconds > 0 ? 30.0 * a.Seconds / maxSeconds : 0, CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, AppBrush(a.AppId));
        track.Child = fill;
        var pct = Cell(AllFormat.Share(a.Share), "Brush.TextSoft", "Double.FontSize.Small");
        pct.Width = 32;
        pct.TextAlignment = TextAlignment.Right;
        var share = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        share.Children.Add(track);
        share.Children.Add(pct);
        Grid.SetColumn(share, 4);
        grid.Children.Add(share);

        var box = Line("Brush.BorderSoft", bottom: true);
        box.Cursor = System.Windows.Input.Cursors.Hand;
        box.SetResourceReference(Border.BackgroundProperty, selected ? "Brush.BorderSoft" : index % 2 == 1 ? "Brush.Panel" : "Brush.Surface");
        box.Child = grid;
        var id = a.AppId;
        box.MouseLeftButtonUp += (_, _) => { _selected = id; Redraw(); };
        return box;
    }

    private FrameworkElement RetiredRow(AllRetiredRow x)
    {
        var grid = Columns2();
        grid.Height = 34;
        grid.Opacity = RetiredOpacity;
        grid.Children.Add(NameCell(x.Name, "Brush.TextMuted", dashed: true));
        AddNumber(grid, 1, AllFormat.Hours(x.Seconds), bold: false, "Brush.Text");
        AddNumber(grid, 2, x.DaysUsed.ToString(En), bold: false, "Brush.Text");
        AddNumber(grid, 3, AllFormat.AveragePerDay(x.AverageSecondsPerDay), bold: false, "Brush.Text");
        var stopped = Cell(Str(AllStringKeys.Stopped, x.StoppedDate.ToString("d MMM", En)), "Brush.TextMuted", "Double.FontSize.Small");
        stopped.HorizontalAlignment = HorizontalAlignment.Right;
        stopped.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(stopped, 4);
        grid.Children.Add(stopped);
        var box = Line("Brush.BorderSoft", bottom: true);
        box.Child = grid;
        return box;
    }

    private void BuildTotals(AllResult r)
    {
        _totals.Children.Clear();
        var name = Cell(Str(AllStringKeys.TotalRow), "Brush.Text", "Double.FontSize.Small");
        name.FontWeight = FontWeights.Bold;
        name.Margin = new Thickness(10, 0, 0, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        _totals.Children.Add(name);
        AddNumber(_totals, 1, AllFormat.Hours(r.Totals.Seconds), bold: true, "Brush.Accent", totals: true);
        AddNumber(_totals, 2, r.Totals.DaysUsed.ToString(En), bold: false, "Brush.TextSoft", totals: true);
        AddNumber(_totals, 3, AllFormat.AveragePerDay(r.Totals.AverageSecondsPerDay), bold: false, "Brush.TextSoft", totals: true);
        AddNumber(_totals, 4, AllFormat.Share(r.Totals.Seconds > 0 ? 1 : 0), bold: false, "Brush.TextSoft", totals: true);
    }

    private void BuildNote(AllResult r)
    {
        var a = r.Apps.FirstOrDefault(x => x.AppId == _selected);
        if (a is null) { _note.Inlines.Clear(); return; }
        var now = DateTime.Now;
        string Month(AllBestMonth m) => new DateTime(m.Year, m.Month, 1).ToString(m.Year == now.Year ? "MMM" : "MMM yyyy", En);
        var text = a.FirstTracked is { } first && a.BestMonth is { } best
            ? Str(AllStringKeys.Note, first.ToString(first.Year == now.Year ? "d MMM" : "d MMM yyyy", En), Month(best),
                AllFormat.Hours(best.Seconds), DurationFormat.HoursMinutes(a.LongestSessionSeconds))
            : Str(AllStringKeys.NoteNone);
        SetBold(_note, [(a.Name, true), (text, false)]);
    }

    // ---- helpers ----

    private string AppBrush(long appId) => "Brush.App" + (int)(appId % Math.Max(1, _themes.Current.Apps.Count));

    private static string ColumnName(AllColumn c) => Str(c switch
    {
        AllColumn.App => AllStringKeys.ColApp,
        AllColumn.Hours => AllStringKeys.ColHours,
        AllColumn.DaysUsed => AllStringKeys.ColDays,
        AllColumn.AvgPerDay => AllStringKeys.ColAvg,
        _ => AllStringKeys.ColShare,
    });

    private static string Arrow(bool descending) => Str(descending ? AllStringKeys.ArrowDesc : AllStringKeys.ArrowAsc);

    private static Grid Columns2()
    {
        var g = new Grid();
        foreach (var w in ColumnWidths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        return g;
    }

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

    private static void AddNumber(Grid grid, int column, string text, bool bold, string brushKey, bool totals = false)
    {
        var t = Cell(text, brushKey, column == 4 && totals ? "Double.FontSize.Small" : "Double.FontSize.Body");
        t.HorizontalAlignment = HorizontalAlignment.Right;
        t.Margin = new Thickness(0, 0, 10, 0);
        if (bold) t.FontWeight = FontWeights.Bold;
        Grid.SetColumn(t, column);
        grid.Children.Add(t);
    }

    private static FrameworkElement NameCell(string name, string brushKey, bool dashed)
    {
        var dot = new Rectangle { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), RadiusX = 2, RadiusY = 2 };
        if (dashed)
        {
            dot.SetResourceReference(Shape.StrokeProperty, brushKey);
            dot.StrokeThickness = 1;
            dot.StrokeDashArray = new DoubleCollection { 2, 2 };
        }
        else dot.SetResourceReference(Shape.FillProperty, brushKey);
        var text = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        text.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var row = new DockPanel { Margin = new Thickness(10, 0, 4, 0), LastChildFill = true };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
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

    // ---- export ----

    private void Export()
    {
        if (_result is not { } r) return;
        var sort = _sort;
        if (_window is not null) _window.KeepOpen = true; // the file dialog deactivates the flyout (decision 55)
        try
        {
            var dlg = new SaveFileDialog { Title = Str(AllStringKeys.Export), FileName = AllExport.FileName, Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;
            AllExport.Write(r, sort, dlg.FileName);
            _export.Content = Str(AllStringKeys.ExportDone);
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            reset.Tick += (_, _) => { reset.Stop(); _export.Content = Str(AllStringKeys.Export); };
            reset.Start();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { if (_window is not null) _window.KeepOpen = false; }
    }
}
