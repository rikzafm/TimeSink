using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TimeSink.Core.Apps;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Today tab. The skeleton is built with the window (cheap); data arrives after first paint: <see cref="Start"/>
/// runs the Core query off-thread, then a 1 s timer ticks the live total from the last snapshot and the pipe state.
/// A fresh query runs on every pipe state change and every 30 s.
/// </summary>
public partial class TodayView : UserControl
{
    private const int RefreshEverySeconds = 30;
#if DEBUG
    private static readonly bool ExpandAll = Environment.GetEnvironmentVariable("TS_EXPAND") == "1"; // test aid for screenshots (decision 115)
#endif

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<long> _expanded = new();
    private readonly Dictionary<long, (TextBlock Time, TextBlock Status, TodayAppRow Row)> _rowViews = new();

    private readonly UntrackedCardView _untracked;
    private long? _signInUtc;
    private TodayResult? _result;
    private int _seq;
    private int _sinceRefresh;
    private bool _started;
    private DateOnly? _day;   // null = the live day; otherwise a past day being browsed
    private DateOnly? _first; // first tracked day, the earliest the picker allows
    private readonly ToolTip _tip = new();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public TodayView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        InitializeComponent();
        // WPF doesn't clamp CornerRadius like CSS does, so a huge value draws an oval: use half the height.
        RunningPill.SizeChanged += (_, e) => RunningPill.CornerRadius = new CornerRadius(e.NewSize.Height / 2);
        _untracked =new UntrackedCardView(dbPath, themes, Refresh);
        _untracked.Rendered += UpdateTrackedHeader;
        UntrackedHost.Children.Add(_untracked);
        _tip.SetResourceReference(Control.BackgroundProperty, "Brush.Panel");
        _tip.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _tip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _tip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        Chart.ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(Chart, 80);
        ToolTipService.SetPlacement(Chart, System.Windows.Controls.Primitives.PlacementMode.Top);
        Chart.HourHovered += OnHourHovered;
        Chart.HourClicked += OnHourClicked;
        Chart.SetResourceReference(HourlyBars.SelectedBrushProperty, "Brush.Accent");
        Panel.CloseRequested += () => Chart.SelectedHour = -1;
        Nav.DateChosen += d => ShowDay(d);
        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Panel.Visibility != Visibility.Visible) return;
            ClosePanel();
            e.Handled = true;
        };
        _timer.Tick += (_, _) => OnTick();
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        AppIcons.Updated += Redraw;
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Begin loading and ticking. Called once the window has rendered (first paint is never delayed by data).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _live.Start();
        _timer.Start();
        Load();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
    }

    /// <summary>Live-state refresh: a past day is static, so pipe changes and the timer leave it alone.</summary>
    public void Refresh()
    {
        if (_day is null) Load();
    }

    /// <summary>Show a given local day (today or earlier); today goes back to the live view.</summary>
    public void ShowDay(DateOnly day)
    {
        _day = day >= Today ? null : day;
        ClosePanel();
        Load();
    }

    /// <summary>Back to the live day with nothing selected (the flyout opens on today).</summary>
    public void ResetToToday()
    {
        if (_day is null && Panel.Visibility != Visibility.Visible) return;
        ShowDay(Today);
    }

    private async void Load()
    {
        if (!_started) return;
        var seq = ++_seq;
        _sinceRefresh = 0;
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        var day = _day;
        TodayResult result;
        try
        {
            (result, var first) = await Task.Run(() =>
            {
                _signInUtc ??= SignInTime.Get() ?? 0; // uptime starts at sign-in; looked up once, off the UI thread
                return (TodayQuery.Run(_dbPath, now, TimeZoneInfo.Local, running, day, _signInUtc is > 0 ? _signInUtc : null),
                    TodayQuery.FirstDate(_dbPath, TimeZoneInfo.Local));
            });
            _first = first;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { return; } // no DB yet: keep the empty state
        if (seq != _seq || !_started) return;
        _result = result;
        Redraw();
        _untracked.Reload(); // after real data is on screen; icons are extracted off-thread
    }

    private void OnTick()
    {
        if (Window.GetWindow(this) is FlyoutWindow { IsRevealed: false }) return; // warm standby: stay idle
        if (_result is null || _day is not null) return; // a past day does not tick
        if (++_sinceRefresh >= RefreshEverySeconds || Today != _result.Date)
        {
            Load();
            return;
        }
        UpdateLive();
    }

    private long ElapsedSinceSnapshot => Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _result!.NowUtc);

    /// <summary>Per-second work: only the numbers that move.</summary>
    private void UpdateLive()
    {
        var r = _result!;
        var elapsed = ElapsedSinceSnapshot;
        var uptime = r.UptimeSeconds + (_day is null ? elapsed : 0); // the PC is on while the view is live
        TotalMain.Text = DurationFormat.HoursMinutes(uptime);
        TotalSeconds.Text = $"{uptime % 60:00}s";
        CombinedText.Text = Str(StringKeys.TodayCombined, DurationFormat.HoursMinutes(r.TotalSeconds + elapsed * r.OpenSessionCount));
        var nowUtc = r.NowUtc + elapsed;
        Chart.NowFraction = _day is null ? Fraction(r, nowUtc) : 0;
        Chart.NowLabel = Clock(nowUtc);
        foreach (var (_, (time, status, row)) in _rowViews)
        {
            if (!row.IsRunning) continue;
            time.Text = DurationFormat.HoursMinutes(row.Seconds + elapsed);
            status.Text = Str(StringKeys.StatusRunning, DurationFormat.HoursMinutes(nowUtc - row.RunningSinceUtc!.Value));
        }
    }

    /// <summary>Position in the day as a wall-clock fraction (the hour columns are wall-clock hours, so a DST day is still 24 columns).</summary>
    private static double Fraction(TodayResult r, long utc) =>
        Math.Clamp((TimeText.Local(utc) - r.Date.ToDateTime(TimeOnly.MinValue)).TotalSeconds / 86400.0, 0, 1);

    /// <summary>Full redraw from the last snapshot (also after a theme swap, since text is composed here).</summary>
    private void Redraw()
    {
        if (_result is not { } r) return;
        var live = _day is null;
        var early = live && TodayEarly.ShowCaption(r.UptimeSeconds);
        var since = early ? TodayEarly.TrackingSinceUtc(r.DayStartUtc, _signInUtc is > 0 ? _signInUtc : null, r.FirstApp?.StartUtc) : null;

        Nav.Configure(Today, _first ?? r.Date, r.Date);
        Nav.CaptionText = since is not null
            ? Str(StringKeys.TodayEarlyCaption, Clock(since.Value))
            : Str(StringKeys.TodayCaption, r.Date.ToString("dddd, d MMM", System.Globalization.CultureInfo.CurrentCulture));
        EarlyHint.Visibility = early ? Visibility.Visible : Visibility.Collapsed;
        RunningText.Text = Str(StringKeys.RunningPill, r.RunningCount);
        RunningPill.Visibility = live ? Visibility.Visible : Visibility.Collapsed;

        var nowLocalHour = live ? TimeText.Local(r.NowUtc).Hour : -1;
        Chart.Hours = r.Hours;
        Chart.CurrentHour = nowLocalHour;
        Chart.SleepFractions = r.SleepBands.Select(b => (Fraction(r, b.StartUtc), Fraction(r, b.EndUtc))).ToList();
        BuildAxis();

        TileFirst.Text = r.FirstApp is { } f ? Clock(f.StartUtc) : Str(StringKeys.TileNone);
        AppIcons.Load(_dbPath, r.Longest is { } lg ? new[] { lg.AppId } : Array.Empty<long>());
        var icon = r.Longest is { } li ? AppIcons.Icon(li.AppId, 16) : null;
        TileLongestIcon.Content = icon;
        TileLongestIcon.ToolTip = icon is null ? null : r.Longest!.AppName;
        TileLongest.Text = r.Longest is { } l
            ? icon is null ? Str(StringKeys.TileLongestValue, DurationFormat.HoursMinutes(l.Seconds), l.AppName) : DurationFormat.HoursMinutes(l.Seconds)
            : Str(StringKeys.TileNone);
        TileVs.Text = Str(StringKeys.TileVsYesterdayValue, DurationFormat.Signed(r.UptimeVsYesterdaySeconds));
        TileVs.SetResourceReference(TextBlock.ForegroundProperty, r.UptimeVsYesterdaySeconds >= 0 ? "Brush.Accent" : "Brush.Warning");

        BuildRows(r);
        Empty.Visibility = r.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrackedHeader();
        UpdateLive();
        if (Chart.SelectedHour >= 0) ShowHourPanel(Chart.SelectedHour);
    }

    private void ClosePanel()
    {
        Chart.SelectedHour = -1;
        Panel.Hide();
    }

    private string HourRange(int h) => $"{Hour(h)} – {Hour((h + 1) % 24)}";

    private void OnHourHovered(int h, Point _)
    {
        if (h < 0 || _result is not { } r) { _tip.IsOpen = false; return; }
        var d = BarDetail.ForHour(r, h, TimeZoneInfo.Local);
        var lines = new List<string> { $"{HourRange(h)} · " + (d.AppSeconds > 0 ? DurationFormat.HoursMinutes(d.AppSeconds) : "no activity") };
        lines.AddRange(d.Apps.Take(3).Select(a => $"{a.Name} {DurationFormat.HoursMinutes(a.Seconds)}"));
        _tip.Content = string.Join(Environment.NewLine, lines);
    }

    private void OnHourClicked(int h)
    {
        if (Chart.SelectedHour == h) { ClosePanel(); return; }
        Chart.SelectedHour = h;
        Focus();
        ShowHourPanel(h);
    }

    private void ShowHourPanel(int h)
    {
        if (_result is not { } r) return;
        var d = BarDetail.ForHour(r, h, TimeZoneInfo.Local);
        var palette = Math.Max(1, _themes.Current.Apps.Count);
        var compares = new List<(string, string, bool)>();
        if (r.HasTypical)
        {
            var diff = (long)Math.Round(d.AppSeconds - d.TypicalAppSeconds);
            compares.Add(("Typical for this hour", $"{DurationFormat.HoursMinutes((long)Math.Round(d.TypicalAppSeconds))} · {DurationFormat.Signed(diff)}", diff >= 0));
        }
        Panel.Show(
            HourRange(h),
            d.AppSeconds > 0 ? $"{DurationFormat.HoursMinutes(d.AppSeconds)} app time · {d.Sessions.Count} sessions" : "No activity this hour",
            d.Apps.Select(a => (a.Name, a.Seconds, (int)(a.AppId % palette))).ToList(),
            d.Sessions.Select(s => (s.AppName, Str(StringKeys.SessionLine, Clock(s.StartUtc), Clock(s.EndUtc)), DurationFormat.HoursMinutes(s.Seconds))).ToList(),
            compares);
    }

    /// <summary>The "Tracked today" line carries the combined app time; its label only shows under the untracked card.</summary>
    private void UpdateTrackedHeader()
    {
        TrackedHeader.Visibility = _result?.Apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackedLabel.Visibility = _untracked.HasCard ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildAxis()
    {
        if (Axis.Children.Count > 0) { RelabelAxis(); return; }
        for (var i = 0; i < 4; i++) Axis.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 5; i++)
        {
            var t = new TextBlock { Style = (Style)FindResource("Small") };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            if (i < 4) { Grid.SetColumn(t, i); t.HorizontalAlignment = HorizontalAlignment.Left; }
            else { Grid.SetColumnSpan(t, 4); t.HorizontalAlignment = HorizontalAlignment.Right; }
            Axis.Children.Add(t);
        }
        RelabelAxis();
    }

    private void RelabelAxis()
    {
        for (var i = 0; i < 5; i++) ((TextBlock)Axis.Children[i]).Text = Hour(i * 6);
    }

    private void BuildRows(TodayResult r)
    {
        AppIcons.Load(_dbPath, r.Apps.Select(a => a.AppId));
        Rows.Children.Clear();
        _rowViews.Clear();
        var paletteSize = Math.Max(1, _themes.Current.Apps.Count);
#if DEBUG
        if (ExpandAll) foreach (var a in r.Apps) _expanded.Add(a.AppId);
#endif
        foreach (var row in r.Apps) Rows.Children.Add(BuildRow(r, row, (int)(row.AppId % paletteSize)));
    }

    private UIElement BuildRow(TodayResult r, TodayAppRow row, int paletteIndex)
    {
        var appBrush = "Brush.App" + paletteIndex;
        var elapsed = ElapsedSinceSnapshot;

        var tile = new Border { VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(FrameworkElement.WidthProperty, "Double.Row.Tile");
        tile.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.Tile");
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        tile.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        var letter = new TextBlock
        {
            Text = row.Name.Length > 0 ? char.ToUpperInvariant(row.Name[0]).ToString() : "?",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        letter.SetResourceReference(TextBlock.ForegroundProperty, appBrush);
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        letter.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        if (!AppIcons.TryFill(tile, row.AppId)) tile.Child = letter;

        var name = new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");

        var status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        status.Style = (Style)FindResource("Small");
        status.SetResourceReference(TextBlock.ForegroundProperty, row.IsRunning ? "Brush.Accent" : "Brush.TextMuted");
        status.Text = row.IsRunning
            ? Str(StringKeys.StatusRunning, DurationFormat.HoursMinutes(r.NowUtc + elapsed - row.RunningSinceUtc!.Value))
            : row.LastClosedUtc is { } closed ? Str(StringKeys.StatusClosed, Clock(closed)) : "";

        var time = new TextBlock { Text = DurationFormat.HoursMinutes(row.Seconds + (row.IsRunning ? elapsed : 0)), VerticalAlignment = VerticalAlignment.Top };
        time.Style = (Style)FindResource("Number");
        time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");

        var strip = new SessionStrip
        {
            Margin = new Thickness(0, 8, 0, 0),
            Segments = row.Sessions.Select(s => (Fraction(r, s.StartUtc), Fraction(r, s.EndUtc))).ToList(),
        };
        strip.SetResourceReference(SessionStrip.SegmentBrushProperty, appBrush);
        strip.SetResourceReference(SessionStrip.TrackBrushProperty, "Brush.BorderSoft");

        var details = BuildDetails(r, row);
        details.Visibility = _expanded.Contains(row.AppId) ? Visibility.Visible : Visibility.Collapsed;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0) };
        text.Children.Add(name);
        text.Children.Add(status);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(time, 2);
        grid.Children.Add(tile);
        grid.Children.Add(text);
        grid.Children.Add(time);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(strip);
        stack.Children.Add(details);

        var card = new Border { Margin = new Thickness(-8, 0, -8, 0), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, Child = stack };
        card.SetResourceReference(Border.PaddingProperty, "Thickness.RowCard");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        card.MouseLeftButtonUp += (_, _) =>
        {
            var open = details.Visibility != Visibility.Visible;
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) _expanded.Add(row.AppId); else _expanded.Remove(row.AppId);
            if (open) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            else card.Background = System.Windows.Media.Brushes.Transparent;
        };
        if (_expanded.Contains(row.AppId)) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");

        _rowViews[row.AppId] = (time, status, row);
        return card;
    }

    private UIElement BuildDetails(TodayResult r, TodayAppRow row)
    {
        var panel = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 8, 10, 8) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        panel.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        panel.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var stack = new StackPanel();
        var header = new TextBlock { Text = Str(StringKeys.SessionsToday), Margin = new Thickness(0, 0, 0, 4), Style = (Style)FindResource("Small") };
        stack.Children.Add(header);

        foreach (var s in row.Sessions)
        {
            var line = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var span = new TextBlock { Style = (Style)FindResource("Soft"), Text = Str(StringKeys.SessionLine, Clock(s.StartUtc), s.IsOpen ? Str(StringKeys.SessionNow) : Clock(s.EndUtc)) };
            var dur = new TextBlock { Style = (Style)FindResource("Number"), Text = DurationFormat.HoursMinutes(s.Seconds) };
            Grid.SetColumn(dur, 1);
            line.Children.Add(span);
            line.Children.Add(dur);
            stack.Children.Add(line);
        }

        var compare = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Style = (Style)FindResource("Small") };
        if (row.SevenDayAverageSeconds is { } avg && avg > 0)
        {
            var pct = (int)Math.Round((row.Seconds / avg - 1) * 100);
            compare.Text = Str(StringKeys.VsAverage, (pct >= 0 ? "+" : "") + pct + "%");
            compare.SetResourceReference(TextBlock.ForegroundProperty, pct >= 0 ? "Brush.Accent" : "Brush.Warning");
        }
        else compare.Text = Str(StringKeys.VsAverageNone);
        stack.Children.Add(compare);

        panel.Child = stack;
        return panel;
    }
}
