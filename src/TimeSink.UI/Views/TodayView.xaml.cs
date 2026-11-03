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
    private DayOfWeek _weekStart = DayOfWeek.Monday; // the picker's first column
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

    private DateOnly Today => _loader.Today;
    private readonly Dictionary<string, (string Name, bool IsLocal)> _deviceInfo = new(StringComparer.OrdinalIgnoreCase);

    private void LoadDeviceInfo()
    {
        _deviceInfo.Clear();
        foreach (var (k, v) in DeviceInfoService.Load(_dbPath))
            _deviceInfo[k] = v;
    }

    private bool IsLocalDevice(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return true;
        if (_deviceInfo.TryGetValue(deviceId, out var info)) return info.IsLocal;
        if (_live?.LocalDeviceId != null) return string.Equals(deviceId, _live.LocalDeviceId, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private string GetDeviceName(string? deviceId, bool isLocal)
    {
        if (!string.IsNullOrEmpty(deviceId) && _deviceInfo.TryGetValue(deviceId, out var info) && !string.IsNullOrEmpty(info.Name))
            return info.Name;
        return isLocal ? "PC" : "Mac";
    }

    private string GetOtherDeviceName(string? localDeviceId)
    {
        foreach (var (id, info) in _deviceInfo)
        {
            if (!info.IsLocal && !string.Equals(id, localDeviceId, StringComparison.OrdinalIgnoreCase))
                return info.Name;
        }
        return "Mac";
    }

    public TodayView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        LoadDeviceInfo();
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
        TileFirstBox.MouseLeftButtonUp += (_, _) => { if (_result?.FirstApp is { } f) JumpToApp(f.AppId); };
        TileLongestBox.MouseLeftButtonUp += (_, _) => { if (_result?.Longest is { } l) JumpToApp(l.AppId); };
        TileVsBox.MouseLeftButtonUp += (_, _) =>
        {
            if (_result is not { } r) return;
            var days = new PeriodNavigator(PeriodUnit.Day, _first ?? DateOnly.MinValue, Today);
            if (!days.CanPrev(r.Date)) return;
            ShowDay(days.Step(r.Date, -1));
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
        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly, () => _live.DeviceScope);
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

    public bool IsDrilldown => Panel.Visibility == Visibility.Visible || _day is not null;

    public bool TryGoBack()
    {
        if (Panel.Visibility == Visibility.Visible)
        {
            ClosePanel();
            return true;
        }
        if (_day is not null)
        {
            ResetToToday();
            return true;
        }
        return false;
    }

    public void NavigateDay(int delta)
    {
        var current = _day ?? Today;
        var next = current.AddDays(delta);
        if (next > Today) next = Today;
        ShowDay(next);
    }

    private async void Load()
    {
        if (!_driver.Started) return;
        _sinceRefresh = 0;
        var day = _day;
        var applied = await _loader.Refresh(
            c => (Result: TodayQuery.Run(c, day), WeekStart: c.WeekStartDay), // no DB yet: keep the empty state
            r =>
            {
                _result = r.Result;
                _first = r.Result.FirstTracked;
                _weekStart = r.WeekStart;
            });
        if (!applied) return;
        LoadDeviceInfo();
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
        var nowUtc = Math.Max(r.NowUtc, _loader.Now.ToUnixTimeSeconds());
        var uptime = r.UptimeAt(nowUtc, _day is null); // the PC is on while the view is live
        TotalMain.Text = UsageText.HoursMinutes(uptime);
        TotalSeconds.Text = $"{uptime % 60}s";
        Chart.NowFraction = _day is null ? r.DayFraction(nowUtc) : 0;
        Chart.NowLabel = Clock(nowUtc);
        Chart.CurrentHour = _day is null ? TimeText.Local(nowUtc).Hour : -1;
        foreach (var (_, (time, status, row)) in _rowViews)
        {
            if (!row.IsRunning) continue;
            time.Text = UsageText.HoursMinutes(r.RowSecondsAt(row, nowUtc));
            status.Text = Str(StringKeys.StatusRunning, UsageText.HoursMinutes(nowUtc - row.RunningSinceUtc!.Value));
        }
    }

    /// <summary>Full redraw from the last snapshot (also after a theme swap, since text is composed here).</summary>
    private void Redraw()
    {
        if (_result is not { } r) return;
        var live = _day is null;
        var early = live && TodayEarly.ShowCaption(r.UptimeSeconds);
        var since = early ? TodayEarly.TrackingSinceUtc(r.DayStartUtc, SignInTime.Cached(), r.FirstApp?.StartUtc) : null;

        var scope = _live?.DeviceScope ?? TimeSink.Core.Devices.DeviceScope.This;
        Nav.Configure(Today, _first ?? r.Date, r.Date, _weekStart);
        if (scope == TimeSink.Core.Devices.DeviceScope.All)
        {
            Nav.CaptionText = Str(StringKeys.TodayCaptionAll, UsageText.WeekdayDate(r.Date, full: true));
            var localName = GetDeviceName(_live?.LocalDeviceId, isLocal: true);
            RunningText.Text = Str(StringKeys.RunningPillDevice, r.RunningCount, localName);
        }
        else
        {
            Nav.CaptionText = since is not null
                ? Str(StringKeys.TodayEarlyCaption, Clock(since.Value))
                : Str(StringKeys.TodayCaption, UsageText.WeekdayDate(r.Date, full: true));
            RunningText.Text = Str(StringKeys.RunningPill, r.RunningCount);
        }
        EarlyHint.Visibility = early ? Visibility.Visible : Visibility.Collapsed;
        RunningPill.Visibility = live && r.RunningCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        var nowLocalHour = live ? TimeText.Local(r.NowUtc).Hour : -1;
        Chart.DeviceScope = scope;
        Chart.LocalDeviceId = _live?.LocalDeviceId;
        if (scope == TimeSink.Core.Devices.DeviceScope.All)
        {
            LegendAppsOpen.Visibility = Visibility.Collapsed;
            LegendDeviceA.Visibility = Visibility.Visible;
            LegendDeviceB.Visibility = Visibility.Visible;
            LegendOverlap.Visibility = Visibility.Visible;
            LegendDeviceAText.Text = Str(StringKeys.LegendDeviceOn, GetDeviceName(_live?.LocalDeviceId, isLocal: true));
            LegendDeviceBText.Text = Str(StringKeys.LegendDeviceOn, GetOtherDeviceName(_live?.LocalDeviceId));
            LegendAsleepText.Text = Str(StringKeys.LegendAsleepGeneric);
            StatTilesGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            LegendAppsOpen.Visibility = Visibility.Visible;
            LegendDeviceA.Visibility = Visibility.Collapsed;
            LegendDeviceB.Visibility = Visibility.Collapsed;
            LegendOverlap.Visibility = Visibility.Collapsed;
            LegendAsleepText.Text = Str(StringKeys.LegendAsleep);
            StatTilesGrid.Visibility = Visibility.Visible;
        }

        Chart.CurrentHour = nowLocalHour;
        Chart.DayStartUtc = r.DayStartUtc;
        Chart.SleepBands = r.SleepBands;
        Chart.HasTypical = r.HasTypical;
        Chart.Hours = r.Hours;
        Chart.NowFraction = live ? r.DayFraction(r.NowUtc) : 0;
        Chart.NowLabel = Clock(r.NowUtc);
        BuildAxis();

        if (scope == TimeSink.Core.Devices.DeviceScope.All && r.DeviceUptimes is { Count: > 0 })
        {
            DeviceSummaryRow.Visibility = Visibility.Visible;
            DeviceSummaryRow.Children.Clear();
            foreach (var (devId, du) in r.DeviceUptimes)
            {
                var isLocal = IsLocalDevice(devId);
                var devName = GetDeviceName(devId, isLocal);
                var devItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
                var marker = new DeviceMarker { IsLocal = isLocal, Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                devItem.Children.Add(marker);
                var offText = (!live || !du.IsLive) && du.LastActiveUtc is { } la ? $" · off {Clock(la)}" : "";
                var txt = new TextBlock
                {
                    Text = $"{devName} {UsageText.HoursMinutes(du.UptimeSeconds)}{offText}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                };
                txt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
                devItem.Children.Add(txt);
                DeviceSummaryRow.Children.Add(devItem);
            }
            if (r.OnBothSeconds > 0)
            {
                var bothText = new TextBlock
                {
                    Text = Str(StringKeys.SummaryOnBoth, UsageText.HoursMinutes(r.OnBothSeconds)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                };
                bothText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                DeviceSummaryRow.Children.Add(bothText);
            }
        }
        else
        {
            DeviceSummaryRow.Visibility = Visibility.Collapsed;
        }

        TileFirst.Text = r.FirstApp is { } f ? Clock(f.StartUtc) : Str(StringKeys.TileNone);
        AppIcons.Load(_dbPath, r.Longest is { } lg ? new[] { lg.AppId } : Array.Empty<long>());
        var icon = r.Longest is { } li ? AppIcons.Icon(li.AppId, 16) : null;
        TileLongestIcon.Content = icon;
        TileLongestIcon.ToolTip = icon is null ? null : r.Longest!.AppName;
        TileLongest.Text = r.Longest is { } l
            ? icon is null ? Str(StringKeys.TileLongestValue, UsageText.HoursMinutes(l.Seconds), l.AppName) : UsageText.HoursMinutes(l.Seconds)
            : Str(StringKeys.TileNone);
        TileVs.Text = Str(StringKeys.TileVsYesterdayValue, UsageText.SignedHoursMinutes(r.UptimeVsYesterdaySeconds));
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

    private static bool IsHourAsleep(int hour, long dayStartUtc, IReadOnlyList<SleepBand>? bands)
    {
        if (bands is not { Count: > 0 }) return false;
        var start = dayStartUtc + (long)hour * 3600;
        var end = start + 3600;
        return bands.Any(b => Math.Min(b.EndUtc, end) - Math.Max(b.StartUtc, start) >= 1800);
    }

    private string HourRange(int h) => $"{Hour(h)} – {Hour((h + 1) % 24)}";

    private void OnHourHovered(int h, Point p)
    {
        if (h < 0 || _result is not { } r) { _tip.IsOpen = false; return; }
        var d = BarDetail.ForHour(r, h);
        var isSlp = IsHourAsleep(h, r.DayStartUtc, r.SleepBands);
        var headline = d.UptimeSeconds > 0
            ? $"{HourRange(h)} · {UsageText.HoursMinutes(d.UptimeSeconds)} uptime"
            : (d.AppSeconds > 0 ? $"{HourRange(h)} · {UsageText.HoursMinutes(d.AppSeconds)}"
                : (isSlp ? $"{HourRange(h)} · {Str(StringKeys.LegendAsleepGeneric)}" : $"{HourRange(h)} · no activity"));
        var lines = new List<string> { headline };
        lines.AddRange(d.Apps.Take(3).Select(a => $"{a.Name} {UsageText.HoursMinutes(a.Seconds)}"));
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
        var c = PanelContents.ForHour(r, h);
        var compares = UsagePhrases.PanelLines(c).Select(l => (Str(l.Label), Str(l.Value), l.Good)).ToList();
        var isSlp = IsHourAsleep(h, r.DayStartUtc, r.SleepBands);
        var subtitle = (c.UptimeSeconds > 0 || c.TotalSeconds > 0)
            ? $"{UsageText.HoursMinutes(c.UptimeSeconds)} uptime · {UsageText.HoursMinutes(c.TotalSeconds)} app time"
            : (isSlp ? Str(StringKeys.LegendAsleepGeneric) : Str(PanelStringKeys.HourEmpty));
        var summary = UsagePhrases.HourSessionSummary(c, Clock);
        Panel.ShowHourCard(
            HourRange(h),
            c.SessionCount,
            subtitle,
            c.Apps.Select(a => (a.Name, a.Seconds, _themes.AppSlot(a.AppId))).ToList(),
            summary,
            compares);
    }

    /// <summary>The "Tracked today" label only shows under the untracked card.</summary>
    private void UpdateTrackedHeader()
    {
        TrackedHeader.Visibility = _untracked.HasCard && _result?.Apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildAxis()
    {
        if (Axis.Children.Count > 0) { RelabelAxis(); return; }
        for (var i = 0; i < 4; i++) Axis.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 5; i++)
        {
            var t = new TextBlock { Style = (Style)FindResource("Small"), FontSize = 10 };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
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

        var tile = AppTile.Tracked(_themes, row.AppId, row.Name, TileStyle.TodayRow);

        var name = new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");

        var scope = _live?.DeviceScope ?? TimeSink.Core.Devices.DeviceScope.This;
        var status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        status.Style = (Style)FindResource("Small");
        status.SetResourceReference(TextBlock.ForegroundProperty, row.IsRunning ? "Brush.Accent" : "Brush.TextMuted");
        if (row.IsRunning)
        {
            if (scope == TimeSink.Core.Devices.DeviceScope.All)
            {
                var localName = GetDeviceName(_live?.LocalDeviceId, isLocal: true);
                status.Text = Str(StringKeys.StatusRunningOn, localName, UsageText.HoursMinutes(r.NowUtc + elapsed - row.RunningSinceUtc!.Value));
            }
            else
            {
                status.Text = Str(StringKeys.StatusRunning, UsageText.HoursMinutes(r.NowUtc + elapsed - row.RunningSinceUtc!.Value));
            }
        }
        else if (scope == TimeSink.Core.Devices.DeviceScope.All && row.Sessions.FirstOrDefault(s => s.IsOpen && !IsLocalDevice(s.DeviceId)) is { } remoteOpen)
        {
            var remoteDevName = GetDeviceName(remoteOpen.DeviceId, isLocal: false);
            status.Text = Str(StringKeys.StatusRunningOn, remoteDevName, UsageText.HoursMinutes(r.NowUtc + elapsed - remoteOpen.StartUtc));
            status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        }
        else
        {
            status.Text = row.LastClosedUtc is { } closed ? Str(StringKeys.StatusClosed, Clock(closed)) : "";
        }

        var time = new TextBlock { Text = UsageText.HoursMinutes(row.Seconds + (row.IsRunning ? elapsed : 0)), VerticalAlignment = VerticalAlignment.Top };
        time.Style = (Style)FindResource("Number");
        time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");

        FrameworkElement stripElement;
        SessionStrip? stripA = null;
        SessionStrip? stripB = null;
        var shown = RowDetails.ShownSessions(row).ToList();
        var isMultiDev = scope == TimeSink.Core.Devices.DeviceScope.All && row.DeviceSeconds is { Count: > 1 };

        if (isMultiDev)
        {
            var stripBox = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            stripA = new SessionStrip
            {
                Height = 3,
                Segments = row.Sessions.Select(s => IsLocalDevice(s.DeviceId) ? (r.DayFraction(s.StartUtc), r.DayFraction(s.EndUtc)) : (0.0, 0.0)).ToList(),
            };
            stripA.SetResourceReference(SessionStrip.SegmentBrushProperty, "Brush.DeviceA");
            stripA.SetResourceReference(SessionStrip.TrackBrushProperty, "Brush.BorderSoft");

            stripB = new SessionStrip
            {
                Height = 3,
                Margin = new Thickness(0, 2, 0, 0),
                Segments = row.Sessions.Select(s => !IsLocalDevice(s.DeviceId) ? (r.DayFraction(s.StartUtc), r.DayFraction(s.EndUtc)) : (0.0, 0.0)).ToList(),
            };
            stripB.SetResourceReference(SessionStrip.SegmentBrushProperty, "Brush.DeviceB");
            stripB.SetResourceReference(SessionStrip.TrackBrushProperty, "Brush.BorderSoft");

            stripBox.Children.Add(stripA);
            stripBox.Children.Add(stripB);
            stripElement = stripBox;
        }
        else
        {
            var singleStrip = new SessionStrip
            {
                Margin = new Thickness(0, 8, 0, 0),
                Segments = row.Sessions.Select(s => (r.DayFraction(s.StartUtc), r.DayFraction(s.EndUtc))).ToList(),
            };
            singleStrip.SetResourceReference(SessionStrip.SegmentBrushProperty, appBrush);
            singleStrip.SetResourceReference(SessionStrip.TrackBrushProperty, "Brush.BorderSoft");
            stripElement = singleStrip;
            stripA = singleStrip;
        }

        var link = new SessionLink();
        // The dial, stats and pills are built the first time the row opens: most rows are never expanded.
        var details = new Border { Visibility = _expanded.Contains(row.AppId) ? Visibility.Visible : Visibility.Collapsed };
        void EnsureDetails()
        {
            if (details.Child is not null) return;
            details.Child = BuildDetails(r, row, appBrush, link);
            link.Apply!();
        }
        if (_pinnedSession.TryGetValue(row.AppId, out var pinnedStart))
            link.Pinned = shown.FindIndex(x => x.StartUtc == pinnedStart);
        // Opened by hand, not through ToolTipService: that only shows a tip once per enter, so dragging across the gap
        // between two sessions closed it and the next session never got one.
        var stripTip = new ToolTip { PlacementTarget = stripElement, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative, VerticalOffset = -30, StaysOpen = true };
        StyleTip(stripTip);
        stripElement.Unloaded += (_, _) => stripTip.IsOpen = false; // the rows are rebuilt on refresh

        Border? card = null;

        void WireStrip(SessionStrip s)
        {
            s.SegmentHovered += (i, p) =>
            {
                link.Hover = i < 0 ? -1 : shown.IndexOf(row.Sessions[i]);
                link.Apply!();
                if (i < 0) { stripTip.IsOpen = false; return; }
                var session = row.Sessions[i];
                stripTip.Content = $"{Clock(session.FullStartUtc)} – {(session.IsOpen ? Str(StringKeys.SessionNow) : Clock(session.EndUtc))} · {UsageText.HoursMinutes(session.FullSeconds)}";
                stripTip.HorizontalOffset = p.X + 8;
                stripTip.IsOpen = true;
            };
            s.SegmentClicked += i =>
            {
                SetOpen(true);
                var at = shown.IndexOf(row.Sessions[i]);
                link.Pinned = link.Pinned == at ? -1 : at;
                PinSession(row, link, shown);
            };
        }
        if (stripA != null) WireStrip(stripA);
        if (stripB != null) WireStrip(stripB);

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
        stack.Children.Add(stripElement);
        stack.Children.Add(details);

        card = new Border { Margin = new Thickness(-8, 0, -8, 0), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, Child = stack };
        card.SetResourceReference(Border.PaddingProperty, "Thickness.RowCard");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        void SetOpen(bool open)
        {
            if (open) EnsureDetails();
            details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) _expanded.Add(row.AppId); else _expanded.Remove(row.AppId);
            if (card != null)
            {
                if (open) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                else card.Background = System.Windows.Media.Brushes.Transparent;
            }
        }
        card.MouseLeftButtonUp += (_, _) => SetOpen(details.Visibility != Visibility.Visible);
        _cards[row.AppId] = (card, SetOpen);
        if (_expanded.Contains(row.AppId)) card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");

        // The strip, dial and pills of a row share one highlighted session: hover previews it, a click pins it.
        link.Apply = () =>
        {
            link.ApplyDetails?.Invoke();
            var active = link.Active;
            var activeIdx = active >= 0 ? IndexOf(row.Sessions, shown[active]) : -1;
            if (stripA != null) stripA.HighlightIndex = activeIdx;
            if (stripB != null) stripB.HighlightIndex = activeIdx;
        };
        if (details.Visibility == Visibility.Visible) EnsureDetails();
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

        var detail = RowDetails.Of(row);
        var shown = detail.Shown.ToList();
        var hidden = detail.Blips;
        var avg = detail.AverageSeconds;

        var isMultiDev = _live?.DeviceScope == TimeSink.Core.Devices.DeviceScope.All && row.DeviceSeconds is { Count: > 1 };
        var dial = new SessionDial
        {
            Width = DialSize, Height = DialSize, VerticalAlignment = VerticalAlignment.Top,
            Segments = shown.Select(s => (r.DayFraction(s.StartUtc), r.DayFraction(s.EndUtc))).ToList(),
            Tails = shown.Select(s => s.CarriesOver ? (double?)r.DayFractionBefore(s.FullStartUtc) : null).ToList(),
            NowFraction = _day is null ? r.DayFraction(r.NowUtc) : null,
            TotalFraction = detail.TotalFraction,
            AverageFraction = detail.AverageFraction,
        };
        dial.SetResourceReference(SessionDial.SegmentBrushProperty, appBrush);
        dial.SetResourceReference(SessionDial.TrackBrushProperty, "Brush.BorderSoft");
        dial.SetResourceReference(SessionDial.TickBrushProperty, "Brush.Text");
        dial.SetResourceReference(SessionDial.NowBrushProperty, "Brush.Accent");
        dial.SetResourceReference(SessionDial.LabelBrushProperty, "Brush.TextMuted");
        dial.SetResourceReference(SessionDial.NumberFontProperty, "FontFamily.Numbers");

        if (isMultiDev)
        {
            dial.IsMultiDevice = true;
            dial.SetResourceReference(SessionDial.DeviceABrushProperty, "Brush.DeviceA");
            dial.SetResourceReference(SessionDial.DeviceBBrushProperty, "Brush.DeviceB");
            dial.CenterSubText = Str(StringKeys.DialVsAverage);

            var shownA = new List<(double Start, double End, int Index)>();
            var tailsA = new List<double?>();
            var shownB = new List<(double Start, double End, int Index)>();
            var tailsB = new List<double?>();

            for (var i = 0; i < shown.Count; i++)
            {
                var s = shown[i];
                var isA = IsLocalDevice(s.DeviceId);
                var sf = r.DayFraction(s.StartUtc);
                var ef = r.DayFraction(s.EndUtc);
                var tail = s.CarriesOver ? (double?)r.DayFractionBefore(s.FullStartUtc) : null;
                if (isA) { shownA.Add((sf, ef, i)); tailsA.Add(tail); }
                else { shownB.Add((sf, ef, i)); tailsB.Add(tail); }
            }
            dial.SegmentsA = shownA;
            dial.TailsA = tailsA;
            dial.SegmentsB = shownB;
            dial.TailsB = tailsB;
        }

        if (detail.VsAverage is { } change)
        {
            dial.CenterText = UsageText.Delta(change);
            dial.SetResourceReference(SessionDial.CenterBrushProperty, Tones.Of(change).Key());
        }
        else
        {
            dial.CenterText = Str(StringKeys.TileNone);
            dial.SetResourceReference(SessionDial.CenterBrushProperty, "Brush.TextMuted");
        }
        System.Windows.Automation.AutomationProperties.SetName(dial, Str(StringKeys.DialLabel, row.Name));

        var longest = detail.LongestSeconds;
        var stats = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (isMultiDev && row.DeviceSeconds is { Count: > 1 })
        {
            var secA = 0L;
            var secB = 0L;
            if (_live?.LocalDeviceId is { } loc)
            {
                row.DeviceSeconds.TryGetValue(loc, out secA);
                foreach (var (k, v) in row.DeviceSeconds) if (k != loc) secB += v;
            }
            else
            {
                var first = true;
                foreach (var v in row.DeviceSeconds.Values)
                {
                    if (first) { secA = v; first = false; }
                    else secB += v;
                }
            }
            var devAName = GetDeviceName(_live?.LocalDeviceId, isLocal: true).ToUpperInvariant();
            var devBName = GetOtherDeviceName(_live?.LocalDeviceId).ToUpperInvariant();
            var countA = shown.Count(s => IsLocalDevice(s.DeviceId));
            var countB = shown.Count(s => !IsLocalDevice(s.DeviceId));

            stats.Children.Add(DeviceStat(
                Str(StringKeys.DialOuterRing, devAName),
                $"{UsageText.HoursMinutes(secA)} · {countA} {(countA == 1 ? "session" : "sessions")}",
                isLocal: true, first: true));

            stats.Children.Add(DeviceStat(
                Str(StringKeys.DialMiddleRing, devBName),
                $"{UsageText.HoursMinutes(secB)} · {countB} {(countB == 1 ? "session" : "sessions")}",
                isLocal: false, first: false));

            if (row.OnBothSeconds > 0)
            {
                stats.Children.Add(PlainStat(
                    Str(StringKeys.DialOverlap),
                    Str(StringKeys.DialOnBoth, UsageText.HoursMinutes(row.OnBothSeconds))));
            }

            stats.Children.Add(PlainStat(
                Str(StringKeys.DialAverage),
                avg is { } m ? UsageText.HoursMinutes((long)Math.Round(m)) : Str(StringKeys.TileNone)));
        }
        else
        {
            stats.Children.Add(Stat(StringKeys.DialSessions, shown.Count.ToString(), first: true));
            stats.Children.Add(Stat(StringKeys.DialLongest, longest > 0 ? UsageText.HoursMinutes(longest) : Str(StringKeys.TileNone)));
            stats.Children.Add(Stat(StringKeys.DialAverage, avg is { } m ? UsageText.HoursMinutes((long)Math.Round(m)) : Str(StringKeys.TileNone)));
        }

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(dial);
        top.Children.Add(stats);

        var pills = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var pillBorders = new List<Border>();
        foreach (var s in shown)
        {
            var pill = Pill(s, appBrush);
            pillBorders.Add(pill);
            pills.Children.Add(pill);
        }

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

    private UIElement DeviceStat(string labelText, string value, bool isLocal, bool first = false)
    {
        var label = new TextBlock { Text = labelText, Style = (Style)FindResource("Small") };
        var number = new TextBlock { Text = value, Style = (Style)FindResource("Number") };
        number.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        var valRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var marker = new DeviceMarker { IsLocal = isLocal, Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        valRow.Children.Add(marker);
        valRow.Children.Add(number);
        var s = new StackPanel { Margin = new Thickness(0, first ? 0 : 8, 0, 0) };
        s.Children.Add(label);
        s.Children.Add(valRow);
        return s;
    }

    private UIElement PlainStat(string labelText, string value, bool first = false)
    {
        var label = new TextBlock { Text = labelText, Style = (Style)FindResource("Small") };
        var number = new TextBlock { Text = value, Style = (Style)FindResource("Number") };
        number.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        var s = new StackPanel { Margin = new Thickness(0, first ? 0 : 8, 0, 0) };
        s.Children.Add(label);
        s.Children.Add(number);
        return s;
    }

    /// <summary>One session as a rounded chip: time span, then its length; the open session reads in the accent colour.</summary>
    private Border Pill(TodaySession s, string appBrush)
    {
        // A session carried over from the day before reads with its real start and whole length, behind a moon.
        var span = new Run(Str(StringKeys.SessionLine, Clock(s.FullStartUtc), s.IsOpen ? Str(StringKeys.SessionNow) : Clock(s.EndUtc)) + " · ");
        var dur = new Run(UsageText.HoursMinutes(s.FullSeconds)) { FontWeight = FontWeights.SemiBold };
        var text = new TextBlock { Style = (Style)FindResource("Small") };
        text.Inlines.Add(span);
        text.Inlines.Add(dur);
        if (s.IsOpen) text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        UIElement content = text;
        if (s.CarriesOver)
        {
            var moon = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"), StrokeThickness = 2,
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Round, Stretch = System.Windows.Media.Stretch.Uniform, Width = 11, Height = 11, Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            moon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, appBrush);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(moon);
            row.Children.Add(text);
            content = row;
        }

        if (_live?.DeviceScope == TimeSink.Core.Devices.DeviceScope.All && !string.IsNullOrEmpty(s.DeviceId))
        {
            var isLocal = IsLocalDevice(s.DeviceId);
            var marker = new DeviceMarker { IsLocal = isLocal, Width = 6, Height = 6, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
            pillRow.Children.Add(marker);
            pillRow.Children.Add(content);
            content = pillRow;
        }

        var pill = new Border { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), Child = content };
        pill.SetResourceReference(Border.BorderBrushProperty, s.IsOpen ? "Brush.Accent" : "Brush.Border");
        pill.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        pill.SizeChanged += (_, e) => pill.CornerRadius = new CornerRadius(e.NewSize.Height / 2); // as RunningPill: WPF does not clamp
        return pill;
    }
}
