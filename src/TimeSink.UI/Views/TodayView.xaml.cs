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
    private static readonly bool ExpandAll = Environment.GetEnvironmentVariable("TS_EXPAND") == "1"; // test aid for screenshots

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

    public TodayView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        InitializeComponent();
        _untracked = new UntrackedCardView(dbPath, themes, Refresh);
        _untracked.Rendered += UpdateTrackedHeader;
        UntrackedHost.Children.Add(_untracked);
        _timer.Tick += (_, _) => OnTick();
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Begin loading and ticking. Called once the window has rendered (first paint is never delayed by data).</summary>
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
        _sinceRefresh = 0;
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        TodayResult result;
        try { result = await Task.Run(() => TodayQuery.Run(_dbPath, now, TimeZoneInfo.Local, running)); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { return; } // no DB yet: keep the empty state
        if (seq != _seq || !_started) return;
        _result = result;
        Redraw();
        _signInUtc ??= await Task.Run(SignInTime.Get) ?? 0;
        if (seq == _seq && _signInUtc > 0) Redraw();
        _untracked.Reload(); // after real data is on screen; icons are extracted off-thread
    }

    private void OnTick()
    {
        if (Window.GetWindow(this) is FlyoutWindow { IsRevealed: false }) return; // warm standby: stay idle
        if (_result is null) return;
        if (++_sinceRefresh >= RefreshEverySeconds || DateOnly.FromDateTime(DateTime.Now) != _result.Date)
        {
            Refresh();
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
        var total = r.TotalSeconds + elapsed * r.OpenSessionCount;
        TotalMain.Text = DurationFormat.HoursMinutes(total);
        TotalSeconds.Text = $"{total % 60:00}s";
        var nowUtc = r.NowUtc + elapsed;
        Chart.NowFraction = Fraction(r, nowUtc);
        Chart.NowLabel = Clock(nowUtc);
        foreach (var (_, (time, status, row)) in _rowViews)
        {
            if (!row.IsRunning) continue;
            time.Text = DurationFormat.HoursMinutes(row.Seconds + elapsed);
            status.Text = Str(StringKeys.StatusRunning, DurationFormat.HoursMinutes(nowUtc - row.RunningSinceUtc!.Value));
        }
    }

    private static double Fraction(TodayResult r, long utc) => (utc - r.DayStartUtc) / (double)(r.DayEndUtc - r.DayStartUtc);

    /// <summary>Full redraw from the last snapshot (also after a theme swap, since text is composed here).</summary>
    private void Redraw()
    {
        if (_result is not { } r) return;
        var total = r.TotalSeconds;
        var early = TodayEarly.ShowCaption(total);
        var since = early ? TodayEarly.TrackingSinceUtc(r.DayStartUtc, _signInUtc is > 0 ? _signInUtc : null, r.FirstApp?.StartUtc) : null;

        Caption.Text = since is not null
            ? Str(StringKeys.TodayEarlyCaption, Clock(since.Value))
            : Str(StringKeys.TodayCaption, TimeText.Local(r.NowUtc).ToString("dddd, d MMM", System.Globalization.CultureInfo.CurrentCulture));
        EarlyHint.Visibility = early ? Visibility.Visible : Visibility.Collapsed;
        RunningText.Text = Str(StringKeys.RunningPill, r.RunningCount);

        var nowLocalHour = Math.Min(23, (int)((r.NowUtc - r.DayStartUtc) / 3600));
        Chart.Hours = r.Hours;
        Chart.CurrentHour = nowLocalHour;
        Chart.SleepFractions = r.SleepBands.Select(b => (Fraction(r, b.StartUtc), Fraction(r, b.EndUtc))).ToList();
        BuildAxis();

        TileFirst.Text = r.FirstApp is { } f ? Clock(f.StartUtc) : Str(StringKeys.TileNone);
        TileLongest.Text = r.Longest is { } l ? Str(StringKeys.TileLongestValue, DurationFormat.HoursMinutes(l.Seconds), l.AppName) : Str(StringKeys.TileNone);
        TileVs.Text = Str(StringKeys.TileVsYesterdayValue, DurationFormat.Signed(r.VsYesterdaySeconds));
        TileVs.SetResourceReference(TextBlock.ForegroundProperty, r.VsYesterdaySeconds >= 0 ? "Brush.Accent" : "Brush.Warning");

        BuildRows(r);
        Empty.Visibility = r.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrackedHeader();
        UpdateLive();
    }

    private void UpdateTrackedHeader() =>
        TrackedHeader.Visibility = _untracked.HasCard && _result?.Apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        Rows.Children.Clear();
        _rowViews.Clear();
        var paletteSize = Math.Max(1, _themes.Current.Apps.Count);
        if (ExpandAll) foreach (var a in r.Apps) _expanded.Add(a.AppId);
        foreach (var row in r.Apps) Rows.Children.Add(BuildRow(r, row, (int)(row.AppId % paletteSize)));
    }

    private UIElement BuildRow(TodayResult r, TodayAppRow row, int paletteIndex)
    {
        var appBrush = "Brush.App" + paletteIndex;
        var elapsed = ElapsedSinceSnapshot;

        var tile = new Border { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top };
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
        tile.Child = letter;

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

        var card = new Border { Padding = new Thickness(8, 9, 8, 9), Margin = new Thickness(-8, 0, -8, 0), Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, Child = stack };
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
