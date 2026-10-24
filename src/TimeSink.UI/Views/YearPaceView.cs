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
/// Year > Pace: 12 monthly columns with projection and ghost bars, the on-pace banner and the tap-a-month panel.
/// Built in code (no XAML) so the skeleton is cheap; data arrives after first paint via <see cref="Start"/>.
/// </summary>
public sealed class YearPaceView : UserControl, IYearSubView
{
    private const int RefreshEverySeconds = 30;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };

    private readonly TextBlock _caption = Label("Small"), _total = new(), _avg = Label("Small"), _best = Label("Small");
    private readonly TextBlock _banner = Label("Soft");
    private readonly PaceBars _bars = new() { Height = 150 };
    private readonly TextBlock _legendTracked = Label("Small"), _legendProjected = Label("Small"), _legendAverage = Label("Small"), _hint = Label("Small");
    private readonly TextBlock _panelName = Label("Number"), _panelValue = new(), _panelNote = Label("Small");
    private readonly StackPanel _panelApps = new() { Margin = new Thickness(0, 8, 0, 0) };

    private YearResult? _result;
    private int _selected = -1;
    private int _seq;
    private bool _started;

    public YearPaceView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;

        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _total.FontWeight = FontWeights.SemiBold;
        _panelValue.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _panelValue.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _panelValue.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        _panelValue.FontWeight = FontWeights.SemiBold;
        _panelName.FontWeight = FontWeights.SemiBold;
        _banner.TextWrapping = TextWrapping.Wrap;
        foreach (var t in new[] { _avg, _best }) t.HorizontalAlignment = HorizontalAlignment.Right;
        _caption.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

        // Header: caption + big total on the left, avg/best on the right.
        var header = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var left = new StackPanel();
        left.Children.Add(_caption);
        left.Children.Add(_total);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        right.Children.Add(_avg);
        right.Children.Add(_best);
        header.Children.Add(left);
        header.Children.Add(right);

        var banner = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 7, 12, 7), Child = _banner, BorderThickness = new Thickness(1) };
        banner.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        banner.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        banner.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        _bars.Margin = new Thickness(0, 14, 0, 0);
        _bars.SetResourceReference(PaceBars.BarBrushProperty, "Brush.Heat3");
        _bars.SetResourceReference(PaceBars.CurrentBrushProperty, "Brush.Accent");
        _bars.SetResourceReference(PaceBars.ProjectedBrushProperty, "Brush.Heat3");
        _bars.SetResourceReference(PaceBars.MutedBrushProperty, "Brush.TextMuted");
        _bars.SetResourceReference(PaceBars.LabelBrushProperty, "Brush.TextSoft");
        _bars.SetResourceReference(PaceBars.SelectedLabelBrushProperty, "Brush.Text");
        _bars.SetResourceReference(PaceBars.SelectedBackBrushProperty, "Brush.SurfaceAlt");
        _bars.SetResourceReference(PaceBars.BaselineBrushProperty, "Brush.Border");
        _bars.SetResourceReference(PaceBars.LabelFontFamilyProperty, "FontFamily.Numbers");
        _bars.SetResourceReference(PaceBars.LabelFontSizeProperty, "Double.FontSize.Small");
        _bars.MonthPicked += i => { _selected = i; DrawChartAndPanel(); };

        var legend = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        var legendItems = new StackPanel { Orientation = Orientation.Horizontal };
        legendItems.Children.Add(LegendItem(_legendTracked, "Brush.Heat3", dashed: false));
        legendItems.Children.Add(LegendItem(_legendProjected, "Brush.Heat3", dashed: true));
        legendItems.Children.Add(LegendItem(_legendAverage, "Brush.TextMuted", dashed: true, line: true));
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        legend.Children.Add(legendItems);
        legend.Children.Add(_hint);

        var panelHead = new Grid();
        panelHead.Children.Add(_panelName);
        panelHead.Children.Add(_panelValue);
        _panelValue.HorizontalAlignment = HorizontalAlignment.Right;
        var panelBody = new StackPanel();
        panelBody.Children.Add(panelHead);
        panelBody.Children.Add(_panelNote);
        panelBody.Children.Add(_panelApps);
        var panel = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 10, 12, 10), Child = panelBody, BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        panel.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var stack = new StackPanel { Margin = new Thickness(16, 6, 16, 12) };
        stack.Children.Add(header);
        stack.Children.Add(banner);
        stack.Children.Add(_bars);
        stack.Children.Add(legend);
        stack.Children.Add(panel);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = stack };

        _timer.Tick += (_, _) => Refresh();
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        Unloaded += (_, _) => Stop();
        Redraw(); // static text and empty chart; data follows in Start
    }

    private static FrameworkElement LegendItem(TextBlock text, string brushKey, bool dashed, bool line = false)
    {
        var swatch = new System.Windows.Shapes.Rectangle { Width = line ? 12 : 9, Height = line ? 1 : 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        if (line || dashed)
        {
            swatch.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
            swatch.StrokeThickness = 1;
            swatch.StrokeDashArray = new DoubleCollection { 2, 2 };
        }
        else swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        row.Children.Add(swatch);
        row.Children.Add(text);
        return row;
    }

    private static TextBlock Label(string style)
    {
        var t = new TextBlock();
        var (brush, font, size) = style switch
        {
            "Small" => ("Brush.TextMuted", "FontFamily.Ui", "Double.FontSize.Small"),
            "Soft" => ("Brush.TextSoft", "FontFamily.Ui", "Double.FontSize.Body"),
            _ => ("Brush.Text", "FontFamily.Numbers", "Double.FontSize.Body"),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, font);
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
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
        YearResult result;
        try { result = await Task.Run(() => YearQuery.Run(_dbPath, now, TimeZoneInfo.Local, running, SignInTime.Cached())); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { return; }
        if (seq != _seq || !_started) return;
        if (_result is null || _result.Year != result.Year) _selected = -1;
        _result = result;
        if (_selected < 0) _selected = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime).Month - 1;
        Redraw();
    }

    private static string H(long seconds) => Str(YearStringKeys.Hours, (long)Math.Round(seconds / 3600.0));

    private void Redraw()
    {
        _legendTracked.Text = Str(YearStringKeys.LegendTracked);
        _legendProjected.Text = Str(YearStringKeys.LegendProjected);
        _legendAverage.Text = Str(YearStringKeys.LegendAverage);
        _hint.Text = Str(YearStringKeys.TapHint);
        if (_result is not { } r)
        {
            _banner.Text = Str(YearStringKeys.BannerNone);
            return;
        }
        _caption.Text = Str(YearStringKeys.Caption, r.Year);
        _total.Text = DurationFormat.HoursMinutes(r.TotalSeconds);
        _avg.Text = r.AverageMonthSeconds is { } a ? Str(YearStringKeys.Average, H(a)) : "";
        _best.Text = r.BestMonth is { } b ? Str(YearStringKeys.Best, Str(YearStringKeys.BestValue, MonthAbbr(b), H(r.BestMonthSeconds))) : "";
        _banner.Text = r.PaceYearSeconds is { } pace
            ? r.RecentMonthCount > 1 ? Str(YearStringKeys.Banner, H(pace), r.RecentMonthCount) : Str(YearStringKeys.BannerOne, H(pace))
            : Str(YearStringKeys.BannerNone);
        DrawChartAndPanel();
    }

    private static string MonthAbbr(int month) => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames[month - 1];
    private static string MonthName(int month) => CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[month - 1];

    private static string ValueLabel(YearMonth m) => m.Kind switch
    {
        YearMonthKind.BeforeTracking => Str(StringKeys.TileNone),
        YearMonthKind.Future => "~" + (long)Math.Round(m.ProjectedSeconds / 3600.0),
        _ => ((long)Math.Round(m.Seconds / 3600.0)).ToString(CultureInfo.CurrentCulture),
    };

    private void DrawChartAndPanel()
    {
        if (_result is not { } r) return;
        _bars.Months = r.Months;
        _bars.ValueLabels = r.Months.Select(ValueLabel).ToList();
        _bars.MonthLabels = r.Months.Select(m => MonthAbbr(m.Month)).ToList();
        _bars.AverageSeconds = r.AverageMonthSeconds;
        _bars.SelectedIndex = _selected;
        if (_selected is >= 0 and < 12) DrawPanel(r, r.Months[_selected]);
    }

    private void DrawPanel(YearResult r, YearMonth m)
    {
        var started = r.TrackingStarted;
        string StartText() => started is { } d ? d.ToString("d MMM", CultureInfo.CurrentCulture) : "";
        var prevName = m.Month > 1 ? MonthAbbr(m.Month - 1) : MonthAbbr(12);
        string Delta() => (m.DeltaPercent!.Value >= 0 ? "+" : "-") + Math.Abs((long)Math.Round(m.DeltaPercent.Value)) + "%";

        _panelName.Text = MonthName(m.Month) + (m.Kind == YearMonthKind.Future ? Str(YearStringKeys.ProjectedSuffix) : "");
        switch (m.Kind)
        {
            case YearMonthKind.BeforeTracking:
                _panelValue.Text = Str(StringKeys.TileNone);
                _panelNote.Text = started is null ? Str(YearStringKeys.NoteBefore) : Str(YearStringKeys.NoteBeforeOn, StartText());
                break;
            case YearMonthKind.Future:
                _panelValue.Text = "~" + H(m.ProjectedSeconds);
                _panelNote.Text = Str(YearStringKeys.NoteProjected);
                break;
            case YearMonthKind.Current:
                _panelValue.Text = H(m.Seconds);
                _panelNote.Text = m.DeltaPercent is null
                    ? Str(YearStringKeys.NoteCurrent, H(m.Seconds), H(m.ProjectedSeconds))
                    : Str(YearStringKeys.NoteCurrentVs, H(m.Seconds), H(m.ProjectedSeconds), Delta(), prevName);
                break;
            default:
                _panelValue.Text = H(m.Seconds);
                var note = m.DeltaPercent is not null ? Str(YearStringKeys.NoteVs, Delta(), prevName)
                    : started is { } s && s.Year == r.Year && s.Month == m.Month ? Str(YearStringKeys.NoteFirst, StartText()) : "";
                if (m.Month == r.BestMonth) note += Str(YearStringKeys.NoteBest);
                _panelNote.Text = note;
                break;
        }
        _panelNote.Visibility = _panelNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        _panelApps.Children.Clear();
        if (m.TopApps.Count == 0)
        {
            if (m.Kind is YearMonthKind.Tracked or YearMonthKind.Current)
            {
                var none = Label("Small");
                none.Text = Str(YearStringKeys.NoteNoApps);
                _panelApps.Children.Add(none);
            }
            return;
        }
        var max = Math.Max(1, m.TopApps.Max(a => a.Seconds));
        for (var i = 0; i < m.TopApps.Count; i++)
        {
            var app = m.TopApps[i];
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            var dot = new System.Windows.Shapes.Rectangle { Width = 9, Height = 9, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.App" + i % 6);
            var name = Label("Soft");
            name.Text = app.Name;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(name, 1);
            var track = new Grid { Height = 5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var back = new Border();
            back.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            back.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
            var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            fill.SetResourceReference(Border.BackgroundProperty, "Brush.App" + i % 6);
            var fraction = app.Seconds / (double)max;
            track.SizeChanged += (_, e) => fill.Width = Math.Max(2, e.NewSize.Width * fraction);
            track.Children.Add(back);
            track.Children.Add(fill);
            Grid.SetColumn(track, 2);
            var value = Label("Soft");
            value.Text = H(app.Seconds);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            Grid.SetColumn(value, 3);
            row.Children.Add(dot);
            row.Children.Add(name);
            row.Children.Add(track);
            row.Children.Add(value);
            _panelApps.Children.Add(row);
        }
    }
}
