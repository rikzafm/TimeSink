using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
public partial class TodayView : UserControl, ILiveView
{
    private const int RefreshEverySeconds = 30;
    private const int MinShownSessionSeconds = 60; // shorter sessions are blips: not drawn, only counted in the note
    private const double DialSize = 150;
#if DEBUG
    private static readonly bool ExpandAll = Environment.GetEnvironmentVariable("TS_EXPAND") == "1"; // test aid for screenshots (decision 115)
#endif

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;
    private readonly HashSet<long> _expanded = new();
    private readonly Dictionary<long, (TextBlock Time, TextBlock Status, TodayAppRow Row)> _rowViews = new();

    private readonly UntrackedCardView _untracked;
    private TodayResult? _result;
    private int _sinceRefresh;
    private DateOnly? _day;   // null = the live day; otherwise a past day being browsed
    private DateOnly? _first; // first tracked day, the earliest the picker allows
    private readonly ToolTip _tip = new();
    private readonly Dictionary<long, (Border Card, Action<bool> SetOpen)> _cards = new();
    private readonly Dictionary<long, long> _pinnedSession = new(); // app -> start of the session pinned in its dial/pills

    /// <summary>Which session of a row is emphasised across its strip, dial and pills: the hovered one, else the pinned one.</summary>
    private sealed class SessionLink
    {
        public int Hover = -1, Pinned = -1;
        public Action? Apply;
        public Action? ApplyDetails; // set once the row's detail panel has been built
        public int Active => Hover >= 0 ? Hover : Pinned;
    }

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
        StyleTip(_tip);
        Chart.ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(Chart, 80);
        ToolTipService.SetPlacement(Chart, System.Windows.Controls.Primitives.PlacementMode.Top);
        Chart.HourHovered += OnHourHovered;
        Chart.HourClicked += OnHourClicked;
        TileFirstBox.MouseLeftButtonUp += (_, _) => { if (_result?.FirstApp is { } f && _result.Apps.FirstOrDefault(a => a.Name == f.AppName) is { } row) JumpToApp(row.AppId); };
        TileLongestBox.MouseLeftButtonUp += (_, _) => { if (_result?.Longest is { } l) JumpToApp(l.AppId); };
        TileVsBox.MouseLeftButtonUp += (_, _) =>
        {
            if (_result is not { } r) return;
            var prev = r.Date.AddDays(-1);
            if (_first is { } first && prev < first) return;
            ShowDay(prev);
        };
        TileFirstBox.ToolTip = "Jump to the app you opened first";
        TileLongestBox.ToolTip = "Jump to the app with the longest session";
        TileVsBox.ToolTip = "Show the previous day";
        Chart.SetResourceReference(HourlyBars.SelectedBrushProperty, "Brush.Accent");
        Panel.CloseRequested += () => Chart.SelectedHour = -1;
        Nav.DateChosen += d => ShowDay(d);
        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right && _result is not null)
            {
                var step = e.Key == Key.Left ? -1 : 1;
                var h = Chart.SelectedHour >= 0 ? Math.Clamp(Chart.SelectedHour + step, 0, 23)
                    : Chart.CurrentHour >= 0 ? Chart.CurrentHour : step < 0 ? 23 : 0;
                Chart.SelectedHour = h;
                ShowHourPanel(h);
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Escape || Panel.Visibility != Visibility.Visible) return;
            ClosePanel();
            e.Handled = true;
        };
        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(1), Load, Redraw, onTick: OnTick, onLiveChanged: Refresh);
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Begin loading and ticking. Called once the window has rendered (first paint is never delayed by data).</summary>
    public void Start() => _driver.Start();

    public void Stop() => _driver.Stop();

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
        if (!_driver.Started) return;
        _sinceRefresh = 0;
        var (path, day) = (_dbPath, _day);
        var applied = await _loader.Refresh(
            c => (Result: TodayQuery.Run(path, c.Now, c.Zone, c.Running, day, c.SignInUtc), First: TodayQuery.FirstDate(path, c.Zone)), // no DB yet: keep the empty state
            r =>
            {
                _result = r.Result;
                _first = r.First;
            });
        if (!applied) return;
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
        var since = early ? TodayEarly.TrackingSinceUtc(r.DayStartUtc, SignInTime.Cached(), r.FirstApp?.StartUtc) : null;

        Nav.Configure(Today, _first ?? r.Date, r.Date);
        Nav.CaptionText = since is not null
            ? Str(StringKeys.TodayEarlyCaption, Clock(since.Value))
            : Str(StringKeys.TodayCaption, r.Date.ToString("dddd, d MMM", System.Globalization.CultureInfo.CurrentCulture));
        EarlyHint.Visibility = early ? Visibility.Visible : Visibility.Collapsed;
        RunningText.Text = Str(StringKeys.RunningPill, r.RunningCount);
        RunningPill.Visibility = live ? Visibility.Visible : Visibility.Collapsed;

        var nowLocalHour = live ? TimeText.Local(r.NowUtc).Hour : -1;
        Chart.Hours = r.Hours;
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

    private void OnHourHovered(int h, Point p)
    {
        if (h < 0 || _result is not { } r) { _tip.IsOpen = false; return; }
        var d = BarDetail.ForHour(r, h, TimeZoneInfo.Local);
        var lines = new List<string> { $"{HourRange(h)} · " + (d.AppSeconds > 0 ? DurationFormat.HoursMinutes(d.AppSeconds) : "no activity") };
        lines.AddRange(d.Apps.Take(3).Select(a => $"{a.Name} {DurationFormat.HoursMinutes(a.Seconds)}"));
        _tip.Content = string.Join(Environment.NewLine, lines);
    }

    /// <summary>A stat tile points at an app: open its row and bring it into view.</summary>
    private void JumpToApp(long appId)
    {
        if (!_cards.TryGetValue(appId, out var c)) return;
        c.SetOpen(true);
        c.Card.BringIntoView();
    }

    private static void StyleTip(ToolTip tip)
    {
        tip.SetResourceReference(Control.BackgroundProperty, "Brush.Panel");
        tip.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        tip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        tip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
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
        var compares = new List<(string, string, bool)>();
        if (r.HasTypical)
        {
            var diff = (long)Math.Round(d.AppSeconds - d.TypicalAppSeconds);
            compares.Add(("Typical for this hour", $"{DurationFormat.HoursMinutes((long)Math.Round(d.TypicalAppSeconds))} · {DurationFormat.Signed(diff)}", diff >= 0));
        }
        Panel.Show(
            HourRange(h),
            d.AppSeconds > 0 ? $"{DurationFormat.HoursMinutes(d.AppSeconds)} app time · {d.Sessions.Count} sessions" : "No activity this hour",
            d.Apps.Select(a => (a.Name, a.Seconds, _themes.AppSlot(a.AppId))).ToList(),
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
        _cards.Clear();
#if DEBUG
        if (ExpandAll) foreach (var a in r.Apps) _expanded.Add(a.AppId);
#endif
        foreach (var row in r.Apps) Rows.Children.Add(BuildRow(r, row, _themes.AppSlot(row.AppId)));
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

        var link = new SessionLink();
        // The dial, stats and pills are built the first time the row opens: most rows are never expanded.
        var details = new Border { Visibility = _expanded.Contains(row.AppId) ? Visibility.Visible : Visibility.Collapsed };
        void EnsureDetails()
        {
            if (details.Child is not null) return;
            details.Child = BuildDetails(r, row, appBrush, link);
            link.Apply!();
        }
        var shown = row.Sessions.Where(x => x.Seconds >= MinShownSessionSeconds).ToList();
        if (_pinnedSession.TryGetValue(row.AppId, out var pinnedStart))
            link.Pinned = shown.FindIndex(x => x.StartUtc == pinnedStart);
        // Opened by hand, not through ToolTipService: that only shows a tip once per enter, so dragging across the gap
        // between two sessions closed it and the next session never got one.
        var stripTip = new ToolTip { PlacementTarget = strip, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, VerticalOffset = -30, StaysOpen = true };
        StyleTip(stripTip);
        strip.Unloaded += (_, _) => stripTip.IsOpen = false; // the rows are rebuilt on refresh
        strip.SegmentHovered += (i, p) =>
        {
            link.Hover = i < 0 ? -1 : shown.IndexOf(row.Sessions[i]);
            link.Apply!();
            if (i < 0) { stripTip.IsOpen = false; return; }
            var s = row.Sessions[i];
            stripTip.Content = $"{Clock(s.StartUtc)} – {(s.IsOpen ? Str(StringKeys.SessionNow) : Clock(s.EndUtc))} · {DurationFormat.HoursMinutes(s.Seconds)}";
            stripTip.HorizontalOffset = p.X + 8;
            stripTip.IsOpen = true;
        };

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
        void SetOpen(bool open)
        {
            if (open) EnsureDetails();
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) _expanded.Add(row.AppId); else _expanded.Remove(row.AppId);
            if (open) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            else card.Background = System.Windows.Media.Brushes.Transparent;
        }
        card.MouseLeftButtonUp += (_, _) => SetOpen(details.Visibility != Visibility.Visible);
        _cards[row.AppId] = (card, SetOpen);
        if (_expanded.Contains(row.AppId)) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");

        // The strip, dial and pills of a row share one highlighted session: hover previews it, a click pins it.
        link.Apply = () =>
        {
            link.ApplyDetails?.Invoke();
            var active = link.Active;
            strip.HighlightIndex = active >= 0 ? IndexOf(row.Sessions, shown[active]) : -1;
        };
        if (details.Visibility == Visibility.Visible) EnsureDetails();
        strip.SegmentClicked += i =>
        {
            SetOpen(true);
            var at = shown.IndexOf(row.Sessions[i]);
            link.Pinned = link.Pinned == at ? -1 : at;
            PinSession(row, link, shown);
        };
        link.Apply();

        _rowViews[row.AppId] = (time, status, row);
        return card;
    }

    private UIElement BuildDetails(TodayResult r, TodayAppRow row, string appBrush, SessionLink link)
    {
        var panel = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 10, 10, 10) };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        panel.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        panel.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var shown = row.Sessions.Where(s => s.Seconds >= MinShownSessionSeconds).ToList();
        var hidden = row.Sessions.Count - shown.Count;
        var avg = row.SevenDayAverageSeconds is { } a && a > 0 ? a : (double?)null;

        var dial = new SessionDial
        {
            Width = DialSize, Height = DialSize, VerticalAlignment = VerticalAlignment.Top,
            Segments = shown.Select(s => (Fraction(r, s.StartUtc), Fraction(r, s.EndUtc))).ToList(),
            NowFraction = _day is null ? Fraction(r, r.NowUtc) : null,
            TotalFraction = row.Seconds / 86400.0,
            AverageFraction = avg is { } v ? v / 86400.0 : null,
        };
        dial.SetResourceReference(SessionDial.SegmentBrushProperty, appBrush);
        dial.SetResourceReference(SessionDial.TrackBrushProperty, "Brush.BorderSoft");
        dial.SetResourceReference(SessionDial.TickBrushProperty, "Brush.Text");
        dial.SetResourceReference(SessionDial.NowBrushProperty, "Brush.Accent");
        dial.SetResourceReference(SessionDial.LabelBrushProperty, "Brush.TextMuted");
        dial.SetResourceReference(SessionDial.NumberFontProperty, "FontFamily.Numbers");
        if (avg is { } mean)
        {
            var pct = (int)Math.Round((row.Seconds / mean - 1) * 100);
            dial.CenterText = (pct >= 0 ? "+" : "") + pct + "%";
            dial.SetResourceReference(SessionDial.CenterBrushProperty, pct >= 0 ? "Brush.Accent" : "Brush.Warning");
        }
        else
        {
            dial.CenterText = Str(StringKeys.TileNone);
            dial.SetResourceReference(SessionDial.CenterBrushProperty, "Brush.TextMuted");
        }
        System.Windows.Automation.AutomationProperties.SetName(dial, Str(StringKeys.DialLabel, row.Name));

        var longest = shown.Count > 0 ? shown.Max(s => s.Seconds) : 0;
        var stats = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        stats.Children.Add(Stat(StringKeys.DialSessions, shown.Count.ToString(), first: true));
        stats.Children.Add(Stat(StringKeys.DialLongest, longest > 0 ? DurationFormat.HoursMinutes(longest) : Str(StringKeys.TileNone)));
        stats.Children.Add(Stat(StringKeys.DialAverage, avg is { } m ? DurationFormat.HoursMinutes((long)Math.Round(m)) : Str(StringKeys.TileNone)));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(dial);
        top.Children.Add(stats);

        var pills = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var pillBorders = new List<Border>();
        foreach (var s in shown) { var pill = Pill(s); pillBorders.Add(pill); pills.Children.Add(pill); }

        link.ApplyDetails = () =>
        {
            var active = link.Active;
            dial.HighlightIndex = active;
            for (var i = 0; i < pillBorders.Count; i++)
            {
                pillBorders[i].Opacity = active < 0 || i == active ? 1 : 0.45;
                pillBorders[i].SetResourceReference(Border.BorderBrushProperty, i == active || shown[i].IsOpen ? "Brush.Accent" : "Brush.Border");
            }
        };
        for (var i = 0; i < pillBorders.Count; i++)
        {
            var at = i;
            pillBorders[i].Cursor = Cursors.Hand;
            pillBorders[i].Background = System.Windows.Media.Brushes.Transparent;
            pillBorders[i].MouseEnter += (_, _) => { link.Hover = at; link.Apply!(); };
            pillBorders[i].MouseLeave += (_, _) => { link.Hover = -1; link.Apply!(); };
            pillBorders[i].MouseLeftButtonUp += (_, e) => { link.Pinned = link.Pinned == at ? -1 : at; PinSession(row, link, shown); e.Handled = true; };
        }
        dial.SegmentHovered += i => { link.Hover = i; link.Apply!(); };
        dial.SegmentClicked += i => { link.Pinned = link.Pinned == i ? -1 : i; PinSession(row, link, shown); };
        dial.Cursor = Cursors.Arrow;

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(pills);
        if (hidden > 0)
        {
            var note = new TextBlock { Text = Str(StringKeys.DialBlips, hidden), Margin = new Thickness(0, 2, 0, 0), Style = (Style)FindResource("Small") };
            stack.Children.Add(note);
        }

        panel.Child = stack;
        return panel;
    }

    private static int IndexOf(IReadOnlyList<TodaySession> list, TodaySession s)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == s) return i;
        return -1;
    }

    /// <summary>Remembers (per app) which session is pinned so the next redraw restores it, and repaints the row's highlight.</summary>
    private void PinSession(TodayAppRow row, SessionLink link, List<TodaySession> shown)
    {
        if (link.Pinned >= 0) _pinnedSession[row.AppId] = shown[link.Pinned].StartUtc;
        else _pinnedSession.Remove(row.AppId);
        link.Apply!();
    }

    private UIElement Stat(string labelKey, string value, bool first = false)
    {
        var label = new TextBlock { Text = Str(labelKey), Style = (Style)FindResource("Small") };
        var number = new TextBlock { Text = value, Style = (Style)FindResource("Number") };
        number.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        var s = new StackPanel { Margin = new Thickness(0, first ? 0 : 10, 0, 0) };
        s.Children.Add(label);
        s.Children.Add(number);
        return s;
    }

    /// <summary>One session as a rounded chip: time span, then its length; the open session reads in the accent colour.</summary>
    private Border Pill(TodaySession s)
    {
        var span = new Run(Str(StringKeys.SessionLine, Clock(s.StartUtc), s.IsOpen ? Str(StringKeys.SessionNow) : Clock(s.EndUtc)) + " · ");
        var dur = new Run(DurationFormat.HoursMinutes(s.Seconds)) { FontWeight = FontWeights.SemiBold };
        var text = new TextBlock { Style = (Style)FindResource("Small") };
        text.Inlines.Add(span);
        text.Inlines.Add(dur);
        if (s.IsOpen) text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        var pill = new Border { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), Child = text };
        pill.SetResourceReference(Border.BorderBrushProperty, s.IsOpen ? "Brush.Accent" : "Brush.Border");
        pill.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        pill.SizeChanged += (_, e) => pill.CornerRadius = new CornerRadius(e.NewSize.Height / 2); // as RunningPill: WPF does not clamp
        return pill;
    }
}
