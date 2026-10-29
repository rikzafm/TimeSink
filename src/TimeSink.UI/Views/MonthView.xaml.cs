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
public partial class MonthView : UserControl, ILiveView
{
    private const int RefreshEverySeconds = 30;
    private static readonly CultureInfo En = CultureInfo.InvariantCulture; // English names in v1 (spec 8.1)

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly FlyoutWindow? _window;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;

    private int _year, _month;
    private DateOnly? _selected;
    private DateOnly? _firstTracked; // first tracked day; months before it are empty, so ◂ stops there
    private MonthResult? _result;
    private List<(string Name, long Seconds)> _barLabels = new();
    private string _defaultNote = "";

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
        Prev.Click += (_, _) => Navigate(-1);
        Next.Click += (_, _) => Navigate(1);
        Calendar.DayClicked += d => { _selected = d; Redraw(); };
        SelBar.HoverChanged += ShowBarNote;
        SelBar.SegmentClicked += _ => ShowBarNote(-1);
        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw,
            onLiveChanged: () => { if (_result?.IsCurrent == true) Refresh(); }, // only the current month follows the live state
            isIdle: () => _window is { IsRevealed: false });
        Unloaded += (_, _) => Stop();
    }

    public void Start() => _driver.Start();

    public void Stop() => _driver.Stop();

    private PeriodNavigator Months()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new PeriodNavigator(PeriodUnit.Month, _firstTracked ?? today, today);
    }

    private void Navigate(int delta)
    {
        var months = Months();
        var shown = new DateOnly(_year, _month, 1);
        if (delta < 0 ? !months.CanPrev(shown) : !months.CanNext(shown)) return;
        var m = months.Step(shown, delta);
        _year = m.Year;
        _month = m.Month;
        _selected = null;
        Refresh();
    }

    public async void Refresh()
    {
        var (year, month) = (_year, _month);
        var path = _dbPath;
        var applied = await _loader.Refresh(
            c => MonthQuery.Run(path, c.Now, c.Zone, year, month, c.WeekStartDay, c.Running, c.SignInUtc, c.GamesOnly),
            r =>
            {
                _firstTracked = r.FirstTracked;
                _result = r;
            });
        if (!applied) return;
        if (_selected is not { } s || s.Year != year || s.Month != month) _selected = _result!.DefaultSelected;
        Redraw();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void Redraw()
    {
        if (_result is not { } r) return;
        var first = r.FirstDay;
        MonthName.Text = UsageText.MonthYear(first);
        var months = Months();
        Prev.IsEnabled = _firstTracked is not null && months.CanPrev(first);
        Next.IsEnabled = months.CanNext(first);

        Total.Text = UsageText.HoursMinutes(r.TotalSeconds);
        BuildDelta(r);
        SetPair(Avg, StringKeys.MonthAvgPrefix, UsageText.HoursMinutes((long)Math.Round(r.DailyAverageSeconds)), StringKeys.MonthAvgSuffix);
        if (r.Best is { } b)
            SetPair(Best, StringKeys.MonthBestPrefix, $"{UsageText.WeekdayDay(b.Date)} · {UsageText.HoursMinutes(b.Seconds)}", null);
        else Best.Text = "";

        // Calendar.
        var names = MonthGrid.Layout(r.Year, r.Month, r.WeekStart).Weekdays.Select(d => UsageText.WeekdayAbbr(d)).ToList();
        names.Add(Str(StringKeys.MonthWeek));
        Calendar.Headers = names;
        Calendar.Rows = r.Weeks.Select(w => (IReadOnlyList<CalendarCell?>)w.Days
            .Select(d => d is null ? null : new CalendarCell(d.Date, d.Heat, UsageText.MonthCell(d.Seconds), d.IsFuture, d.IsToday)).ToList()).ToList();
        Calendar.RowTotals = r.Weeks.Select(w => UsageText.MonthCell(w.TotalSeconds)).ToList();
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
        var prev = UsageText.MonthAbbr(r.FirstDay.AddMonths(-1).Month);
        Delta.Text = Str(StringKeys.MonthDeltaVs, UsageText.DeltaOfRatio(pct), prev);
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
        SelDate.Text = UsageText.WeekdayDate(day.Date);
        SelTotal.Text = UsageText.HoursMinutes(day.Seconds);
        var bar = day.Bar;
        _barLabels = bar.Segments.Select(s => (s.Name, s.Seconds)).ToList();
        SelBar.ClearPinned();
        SelBar.Segments = bar.Segments
            .Select(s => ((double)s.Seconds, Res(s.AppId is { } id ? _themes.AppBrushKey(id) : "Brush.TextMuted"))).ToList();
        _defaultNote = bar.Verdict switch
        {
            DayVerdict.Empty => Str(StringKeys.MonthDayEmpty),
            DayVerdict.Mostly => Str(StringKeys.MonthMostly, bar.Leader!.Name, UsageText.HoursMinutes(bar.Leader.Seconds)),
            _ => Str(StringKeys.MonthSpread, bar.AppCount),
        };
        ShowBarNote(-1);
    }

    private void ShowBarNote(int index)
    {
        if (index < 0) index = SelBar.PinnedIndex;
        SelNote.Text = index >= 0 && index < _barLabels.Count
            ? $"{_barLabels[index].Name} · {UsageText.HoursMinutes(_barLabels[index].Seconds)}"
            : _defaultNote;
    }

    private void BuildTop(MonthResult r)
    {
        TopApps.Children.Clear();
        AppIcons.Load(_dbPath, r.TopApps.Select(a => a.AppId));
        var max = r.TopApps.Count == 0 ? 1 : r.TopApps.Max(a => a.Seconds);
        foreach (var app in r.TopApps)
        {
            var brush = _themes.AppBrushKey(app.AppId);
            var tile = AppTile.Tracked(_themes, app.AppId, app.Name, TileStyle.Mini);

            var name = new TextBlock { Text = app.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

            var time = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
            time.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            time.Inlines.Add(new System.Windows.Documents.Run(UsageText.HoursMinutes(app.Seconds) + " "));
            var deltaRun = new System.Windows.Documents.Run(app.DeltaPercent is { } p ? UsageText.DeltaOfRatio(p) : Str(StringKeys.MonthNewApp)) { FontWeight = FontWeights.SemiBold };
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

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(En) + suffix;
    }
}
