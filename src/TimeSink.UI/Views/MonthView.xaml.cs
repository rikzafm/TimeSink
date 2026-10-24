using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Month tab (calendar heatmap). Built on first visit, loads nothing until <see cref="Start"/> (called after the first
/// frame). A query runs off-thread on start, month change, pipe state change (current month) and every 30 s.
/// </summary>
public partial class MonthView : UserControl
{
    private const int RefreshEverySeconds = 30;
    private static readonly CultureInfo En = CultureInfo.InvariantCulture; // English names in v1 (spec 8.1)

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly FlyoutWindow? _window;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };

    private int _year, _month;
    private DateOnly? _selected;
    private MonthResult? _result;
    private int _seq;
    private bool _started;

    public MonthView(string dbPath, ThemeService themes, LiveState live, FlyoutWindow? window)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _window = window;
        var today = DateTime.Now;
        _year = today.Year;
        _month = today.Month;
        InitializeComponent();
        ExportButton.SetResourceReference(ContentProperty, "Str.MonthExport");
        Prev.Click += (_, _) => Navigate(-1);
        Next.Click += (_, _) => Navigate(1);
        ExportButton.Click += (_, _) => Export();
        Calendar.DayClicked += d => { _selected = d; Redraw(); };
        _timer.Tick += (_, _) => { if (_window is not { IsRevealed: false }) Refresh(); };
        _live.Changed += () => { if (_result?.IsCurrent == true) Refresh(); };
        _themes.Applied += Redraw;
        AppIcons.Updated += Redraw;
        Unloaded += (_, _) => Stop();
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

    private void Navigate(int delta)
    {
        var m = new DateOnly(_year, _month, 1).AddMonths(delta);
        if (m > DateOnly.FromDateTime(DateTime.Now)) return;
        _year = m.Year;
        _month = m.Month;
        _selected = null;
        Refresh();
    }

    public async void Refresh()
    {
        if (!_started) return;
        var seq = ++_seq;
        var (year, month) = (_year, _month);
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        var path = _dbPath;
        MonthResult result;
        try
        {
            result = await Task.Run(() =>
                MonthQuery.Run(path, now, TimeZoneInfo.Local, year, month,
                    MonthQuery.ParseWeekStart(SettingsStore.Get(path, "week.start")), running));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { return; } // no DB yet
        if (seq != _seq || !_started) return;
        _result = result;
        if (_selected is not { } s || s.Year != year || s.Month != month) _selected = result.DefaultSelected;
        Redraw();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void Redraw()
    {
        if (_result is not { } r) return;
        var first = r.FirstDay;
        MonthName.Text = first.ToString("MMMM yyyy", En);
        Span.Text = r.IsCurrent ? Str(StringKeys.MonthSpanSoFar, Ordinal(r.DaysCounted)) : Str(StringKeys.MonthSpanFull);
        Next.IsEnabled = !r.IsCurrent && first.AddMonths(1) <= DateOnly.FromDateTime(DateTime.Now);

        Total.Text = DurationFormat.HoursMinutes(r.TotalSeconds);
        BuildDelta(r);
        SetPair(Avg, StringKeys.MonthAvgPrefix, DurationFormat.HoursMinutes((long)Math.Round(r.DailyAverageSeconds)), StringKeys.MonthAvgSuffix);
        if (r.Best is { } b)
            SetPair(Best, StringKeys.MonthBestPrefix, $"{b.Date.ToString("ddd d", En)} · {DurationFormat.HoursMinutes(b.Seconds)}", null);
        else Best.Text = "";

        // Calendar.
        var names = Enumerable.Range(0, 7).Select(i => En.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)r.WeekStart + i) % 7))).ToList();
        names.Add(Str(StringKeys.MonthWeek));
        Calendar.Headers = names;
        Calendar.Rows = r.Weeks.Select(w => (IReadOnlyList<CalendarCell?>)w.Days
            .Select(d => d is null ? null : new CalendarCell(d.Date, d.Heat, ShortHours(d.Seconds), d.IsFuture, d.IsToday)).ToList()).ToList();
        Calendar.RowTotals = r.Weeks.Select(w => w.TotalSeconds > 0 ? ShortHours(w.TotalSeconds) : "").ToList();
        Calendar.Selected = _selected;
        Calendar.HeatBrushes = Enumerable.Range(0, 5).Select(i => Res("Brush.Heat" + i)).ToList();
        BuildLegend();

        BuildDay(r);
        BuildTop(r);
        Empty.Visibility = r.TotalSeconds == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPair(TextBlock target, string prefixKey, string value, string? suffixKey)
    {
        target.Inlines.Clear();
        target.Inlines.Add(new System.Windows.Documents.Run(Str(prefixKey) + " "));
        var v = new System.Windows.Documents.Run(value) { FontWeight = FontWeights.SemiBold };
        v.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Text");
        target.Inlines.Add(v);
        if (suffixKey is not null) target.Inlines.Add(new System.Windows.Documents.Run(" " + Str(suffixKey)));
    }

    private void BuildDelta(MonthResult r)
    {
        if (r.DeltaPercent is not { } pct || r.DaysCounted == 0) { DeltaPill.Visibility = Visibility.Collapsed; return; }
        DeltaPill.Visibility = Visibility.Visible;
        var prev = r.FirstDay.AddMonths(-1).ToString("MMM", En);
        Delta.Text = Str(StringKeys.MonthDeltaVs, Percent(pct), prev);
        var key = pct >= 0 ? "Brush.Accent" : "Brush.Warning";
        Delta.SetResourceReference(TextBlock.ForegroundProperty, key);
        var tint = Res(key).Clone();
        tint.Opacity = Application.Current.TryFindResource("Double.Opacity.Wash") is double wash ? wash : 0;
        DeltaPill.Background = tint;
        DeltaPill.BorderBrush = Brushes.Transparent;
    }

    private void BuildLegend()
    {
        Legend.Children.Clear();
        Legend.Children.Add(LegendText(StringKeys.MonthLess));
        for (var i = 0; i < 5; i++)
        {
            var sw = new Border { Width = 11, Height = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sw.SetResourceReference(Border.BackgroundProperty, "Brush.Heat" + i);
            sw.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            Legend.Children.Add(sw);
        }
        var more = LegendText(StringKeys.MonthMore);
        more.Margin = new Thickness(4, 0, 0, 0);
        Legend.Children.Add(more);
    }

    private TextBlock LegendText(string key)
    {
        var t = new TextBlock { Text = Str(key), Style = (Style)FindResource("Small") };
        return t;
    }

    private void BuildDay(MonthResult r)
    {
        var day = r.Days.FirstOrDefault(d => d.Date == _selected) ?? r.Days[0];
        SelDate.Text = day.Date.ToString("ddd, d MMM", En);
        SelTotal.Text = DurationFormat.HoursMinutes(day.Seconds);
        var palette = Math.Max(1, _themes.Current.Apps.Count);
        SelBar.Segments = day.Apps.Select(a => ((double)a.Seconds, Res("Brush.App" + (int)(a.AppId % palette)))).ToList();
        SelNote.Text = day.Apps.Count > 0
            ? Str(StringKeys.MonthMostly, day.Apps[0].Name, DurationFormat.HoursMinutes(day.Apps[0].Seconds))
            : Str(StringKeys.MonthDayEmpty);
    }

    private void BuildTop(MonthResult r)
    {
        TopApps.Children.Clear();
        AppIcons.Load(_dbPath, r.TopApps.Select(a => a.AppId));
        var palette = Math.Max(1, _themes.Current.Apps.Count);
        var max = r.TopApps.Count == 0 ? 1 : r.TopApps.Max(a => a.Seconds);
        foreach (var app in r.TopApps)
        {
            var brush = "Brush.App" + (int)(app.AppId % palette);
            var tile = new Border { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top };
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            tile.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
            tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            var letter = new TextBlock
            {
                Text = app.Name.Length > 0 ? char.ToUpperInvariant(app.Name[0]).ToString() : "?",
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold,
            };
            letter.SetResourceReference(TextBlock.ForegroundProperty, brush);
            letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            letter.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
            if (!AppIcons.TryFill(tile, app.AppId, 3)) tile.Child = letter;

            var name = new TextBlock { Text = app.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

            var time = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
            time.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            time.Inlines.Add(new System.Windows.Documents.Run(DurationFormat.HoursMinutes(app.Seconds) + " "));
            var deltaRun = new System.Windows.Documents.Run(app.DeltaPercent is { } p ? Percent(p) : Str(StringKeys.MonthNewApp)) { FontWeight = FontWeights.SemiBold };
            deltaRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
                app.DeltaPercent is null ? "Brush.TextMuted" : app.DeltaPercent >= 0 ? "Brush.Accent" : "Brush.Warning");
            time.Inlines.Add(deltaRun);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(time, 1);
            top.Children.Add(name);
            top.Children.Add(time);

            var track = new Grid { Height = 4, Margin = new Thickness(0, 5, 0, 0) };
            var trackBg = new Border();
            trackBg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            trackBg.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
            var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left };
            fill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            fill.SetResourceReference(Border.BackgroundProperty, brush);
            var frac = Math.Max(0.02, app.Seconds / (double)max);
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * frac;
            track.Children.Add(trackBg);
            track.Children.Add(fill);

            var body = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            body.Children.Add(top);
            body.Children.Add(track);

            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(body, 1);
            row.Children.Add(tile);
            row.Children.Add(body);
            TopApps.Children.Add(row);
        }
    }

    private void Export()
    {
        if (_result is not { } r) return;
        if (_window is not null) _window.KeepOpen = true; // the file dialog deactivates the flyout (decision 55)
        try
        {
            var dlg = new SaveFileDialog { Title = Str(StringKeys.MonthExport), FileName = MonthExport.FileName(r), Filter = "CSV (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;
            MonthExport.Write(r, dlg.FileName);
            ExportButton.SetResourceReference(ContentProperty, "Str.MonthExportDone");
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            reset.Tick += (_, _) => { reset.Stop(); ExportButton.SetResourceReference(ContentProperty, "Str.MonthExport"); };
            reset.Start();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { if (_window is not null) _window.KeepOpen = false; }
    }

    private static string Percent(double fraction)
    {
        var p = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        return (p >= 0 ? "+" : "") + p.ToString(En) + "%";
    }

    /// <summary>"7h" for an hour or more, "42m" under an hour, blank for none (cell text).</summary>
    private static string ShortHours(long seconds)
    {
        if (seconds <= 0) return "";
        if (seconds < 3600) return (seconds / 60).ToString(En) + "m";
        return ((int)Math.Round(seconds / 3600.0, MidpointRounding.AwayFromZero)).ToString(En) + "h";
    }

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(En) + suffix;
    }
}
