using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Year > App x month matrix: hours per app per month coloured relative to each app's own best month, row and column
/// totals, and a tap summary for an app row or a month header. Built in code so the skeleton is cheap; data arrives after
/// first paint via <see cref="Start"/> (decisions 95-98).
/// </summary>
public sealed class YearMatrixView : UserControl, IYearSubView
{
    private const int RefreshEverySeconds = 30;
    private const double BarsHeight = 34;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;

    private readonly TextBlock _caption = Label("Small"), _total = new(), _footnote = Label("Small"), _empty = Label("Small");
    private readonly MatrixGrid _grid = new();
    private readonly TextBlock _hint = Label("Small");
    private readonly TextBlock _selTitle = new(), _selTotal = new(), _selNote = Label("Soft");
    private readonly Grid _selBars = new() { Height = BarsHeight, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _panel = new();

    private MatrixResult? _result;
    private bool _selectMonth;
    private int _selected; // row index, or month column index when _selectMonth

    public YearMatrixView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;

        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _total.FontWeight = FontWeights.SemiBold;
        _caption.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _footnote.Margin = new Thickness(0, 8, 0, 0);
        _footnote.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(0, 24, 0, 0);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Visibility = Visibility.Collapsed;

        _grid.Margin = new Thickness(0, 12, 0, 0);
        _grid.SetResourceReference(MatrixGrid.TextBrushProperty, "Brush.Text");
        _grid.SetResourceReference(MatrixGrid.MutedBrushProperty, "Brush.TextMuted");
        _grid.SetResourceReference(MatrixGrid.AccentBrushProperty, "Brush.Accent");
        _grid.SetResourceReference(MatrixGrid.OnHeatBrushProperty, "Brush.OnAccent");
        _grid.SetResourceReference(MatrixGrid.OnSelectionBrushProperty, "Brush.Surface");
        _grid.SetResourceReference(MatrixGrid.BorderBrushLineProperty, "Brush.Border");
        _grid.SetResourceReference(MatrixGrid.LabelFontFamilyProperty, "FontFamily.Numbers");
        _grid.SetResourceReference(MatrixGrid.LabelFontSizeProperty, "Double.FontSize.Small");
        _grid.RowClicked += i => { _selectMonth = false; _selected = i; DrawSelection(); };
        _grid.MonthClicked += i => { _selectMonth = true; _selected = i; DrawSelection(); };

        _selTitle.FontWeight = FontWeights.SemiBold;
        _selTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _selTitle.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        _selTitle.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _selTotal.FontWeight = FontWeights.SemiBold;
        _selTotal.HorizontalAlignment = HorizontalAlignment.Right;
        _selTotal.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _selTotal.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _selTotal.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _selNote.TextWrapping = TextWrapping.Wrap;
        _selNote.Margin = new Thickness(0, 3, 0, 0);

        var head = new Grid();
        head.Children.Add(_selTitle);
        head.Children.Add(_selTotal);
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_selNote);
        body.Children.Add(_selBars);
        _panel.Padding = new Thickness(12, 10, 12, 10);
        _panel.BorderThickness = new Thickness(1);
        _panel.Child = body;
        _panel.Visibility = Visibility.Collapsed;
        _panel.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        _panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _panel.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        _hint.HorizontalAlignment = HorizontalAlignment.Right;

        var stack = new StackPanel { Margin = new Thickness(16, 6, 16, 12) };
        stack.Children.Add(_caption);
        stack.Children.Add(_total);
        stack.Children.Add(_empty);
        stack.Children.Add(_grid);
        stack.Children.Add(_footnote);
        // design: the app/month summary and hint stay pinned under the grid; only the grid scrolls when there are many apps
        _panel.Margin = new Thickness(16, 10, 16, 0);
        _hint.Margin = new Thickness(16, 8, 16, 10);
        var pinned = new StackPanel();
        pinned.Children.Add(_panel);
        pinned.Children.Add(_hint);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = stack };
        var root = new DockPanel();
        DockPanel.SetDock(pinned, Dock.Bottom);
        root.Children.Add(pinned);
        root.Children.Add(scroll);
        Content = root;

        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw);
        Unloaded += (_, _) => Stop();
        Redraw(); // static text and empty grid; data follows in Start
    }

    private static TextBlock Label(string style)
    {
        var t = new TextBlock();
        var (brush, size) = style switch
        {
            "Small" => ("Brush.TextMuted", "Double.FontSize.Small"),
            _ => ("Brush.TextSoft", "Double.FontSize.Body"),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }

    public void Start() => _driver.Start();

    public void Stop() => _driver.Stop();

    public async void Refresh()
    {
        if (await _loader.Refresh(c => MatrixQuery.Run(c), r => _result = r)) Redraw();
    }

    private static string H(long seconds) => Str(MatrixStringKeys.Hours, UsageText.Hours(seconds));

    private void Redraw()
    {
        _footnote.Text = Str(MatrixStringKeys.Footnote);
        var small = (double)FindResource("Double.FontSize.Small");
        _caption.FontSize = _hint.FontSize = small + 1; // design: 12px caption and hint, 10px footnote
        _footnote.FontSize = small - 1;
        _hint.Text = Str(MatrixStringKeys.Hint);
        _empty.Text = Str(MatrixStringKeys.Empty);
        _grid.TotalHeader = Str(MatrixStringKeys.Total);
        _grid.AllAppsLabel = Str(MatrixStringKeys.AllApps);
        _grid.HeatBrushes = Enumerable.Range(0, 5).Select(i => (Brush)FindResource("Brush.Heat" + i)).ToList();
        _grid.AppBrushes = Enumerable.Range(0, Math.Max(1, _themes.Current.Apps.Count)).Select(i => (Brush)FindResource("Brush.App" + i)).ToList();

        if (_result is not { } r)
        {
            _caption.Text = "";
            _total.Text = "";
            _panel.Visibility = Visibility.Collapsed;
            return;
        }
        var empty = r.Rows.Count == 0;
        _caption.Text = r.TrackingStarted is { } s && s.Year == r.Year
            ? Str(MatrixStringKeys.CaptionSince, r.Year, UsageText.ShortDate(s))
            : Str(MatrixStringKeys.Caption, r.Year);
        _total.Text = H(r.GrandSeconds);
        _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _grid.Visibility = _footnote.Visibility = _hint.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        AppIcons.Load(_dbPath, r.Rows.Select(row => row.AppId));
        _grid.MonthLabels = r.Months.Select(m => UsageText.MonthAbbr(m.Month)).ToList();
        _grid.Rows = r.Rows.Select(row => new MatrixGridRow(row.Name, _themes.AppSlot(row.AppId), row.Cells.Select(CellText).ToList(),
            row.Cells.Select(c => c.Level).ToList(), H(row.TotalSeconds), AppIcons.Get(row.AppId))).ToList();
        _grid.ColumnTotals = r.Months.Select(m => UsageText.Hours(m.TotalSeconds)).ToList();
        _grid.GrandTotal = H(r.GrandSeconds);
        DrawSelection();
    }

    private static string CellText(MatrixCell c) =>
        c.Seconds == 0 ? Str(StringKeys.TileNone) : UsageText.Hours(c.Seconds);

    private void DrawSelection()
    {
        if (_result is not { } r || r.Rows.Count == 0)
        {
            _panel.Visibility = Visibility.Collapsed;
            return;
        }
        if (_selectMonth && _selected >= r.Months.Count) _selectMonth = false;
        if (!_selectMonth) _selected = Math.Clamp(_selected, 0, r.Rows.Count - 1);
        _grid.SelectedMonth = _selectMonth ? _selected : -1;
        _grid.SelectedRow = _selectMonth ? -1 : _selected;
        _panel.Visibility = Visibility.Visible;
        _selBars.Children.Clear();
        _selBars.ColumnDefinitions.Clear();

        IReadOnlyList<(long Seconds, Brush Fill)> bars;
        long max;
        if (_selectMonth)
        {
            var m = r.Months[_selected];
            _selTitle.Text = UsageText.MonthName(m.Month);
            _selTotal.Text = H(m.TotalSeconds);
            _selNote.Text = m.TopApps.Count switch
            {
                0 => Str(MatrixStringKeys.MonthNoteNone),
                1 => Str(MatrixStringKeys.MonthNote1, m.TopApps[0].Name, H(m.TopApps[0].Seconds)),
                2 => Str(MatrixStringKeys.MonthNote2, m.TopApps[0].Name, H(m.TopApps[0].Seconds), m.TopApps[1].Name, H(m.TopApps[1].Seconds)),
                _ => Str(MatrixStringKeys.MonthNote3, m.TopApps[0].Name, H(m.TopApps[0].Seconds), m.TopApps[1].Name, H(m.TopApps[1].Seconds),
                    m.TopApps[2].Name, H(m.TopApps[2].Seconds)),
            };
            var col = _selected;
            bars = r.Rows.Select(row => (row.Cells[col].Seconds, (Brush)FindResource(_themes.AppBrushKey(row.AppId)))).ToList();
            max = bars.Max(b => b.Seconds);
        }
        else
        {
            var row = r.Rows[_selected];
            _selTitle.Text = row.Name;
            _selTotal.Text = Str(MatrixStringKeys.RowTotal, H(row.TotalSeconds), row.SharePercent);
            var peak = Str(MatrixStringKeys.RowNotePeak, UsageText.MonthName(row.PeakMonth), H(row.PeakSeconds));
            _selNote.Text = row.TrendPercent is { } t
                ? Str(t >= 0 ? MatrixStringKeys.RowNoteUp : MatrixStringKeys.RowNoteDown, UsageText.MonthName(row.PeakMonth), H(row.PeakSeconds), UsageText.DeltaMagnitude(t))
                : peak;
            var accent = (Brush)FindResource(_themes.AppBrushKey(row.AppId));
            var muted = (Brush)FindResource("Brush.Heat1");
            bars = row.Cells.Select(c => (c.Seconds, c.Seconds == row.PeakSeconds ? accent : muted)).ToList();
            max = row.PeakSeconds;
        }
        for (var i = 0; i < bars.Count; i++)
        {
            _selBars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var h = max <= 0 ? 0 : Math.Max(6, Math.Round(bars[i].Seconds / (double)max * BarsHeight));
            if (bars[i].Seconds <= 0) h = 0;
            var bar = new Border
            {
                Height = h, Margin = new Thickness(i == 0 ? 0 : 2, 0, i == bars.Count - 1 ? 0 : 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom, Background = bars[i].Fill, CornerRadius = new CornerRadius(2, 2, 0, 0),
            };
            Grid.SetColumn(bar, i);
            _selBars.Children.Add(bar);
        }
    }
}
