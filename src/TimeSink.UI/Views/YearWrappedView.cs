using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Year > Wrapped: three pageable cards (total hours, top apps podium, habits; board PulseYearWrapped) and "Save as
/// image", which writes the visible card to a PNG. Built in code; data arrives after first paint via <see cref="Start"/>.
/// </summary>
public sealed class YearWrappedView : UserControl, IYearSubView
{
    private const int RefreshEverySeconds = 30;
    private const int CardCount = 3;
    private const double SwipeDistance = 40;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };
    private readonly WrappedCard _card = new();
    private readonly WrappedPager _pager = new() { Count = CardCount };
    private readonly Button _save = new();

    private WrappedResult? _result;
    private int _index;
    private int _seq;
    private bool _started;
    private Point? _dragFrom;
    private DispatcherTimer? _savedReset;

    public YearWrappedView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
#if DEBUG
        if (int.TryParse(Environment.GetEnvironmentVariable("TS_WRAPPED_CARD"), out var start)) _index = Math.Clamp(start - 1, 0, CardCount - 1); // test aid (decision 115)
#endif

        Bind(_card, WrappedCard.BackBrushProperty, "Brush.SurfaceAlt");
        Bind(_card, WrappedCard.TileBackBrushProperty, "Brush.Surface");
        Bind(_card, WrappedCard.LineBrushProperty, "Brush.Border");
        Bind(_card, WrappedCard.TextBrushProperty, "Brush.Text");
        Bind(_card, WrappedCard.SoftBrushProperty, "Brush.TextSoft");
        Bind(_card, WrappedCard.MutedBrushProperty, "Brush.TextMuted");
        Bind(_card, WrappedCard.AccentBrushProperty, "Brush.Accent");
        Bind(_card, WrappedCard.OnAccentBrushProperty, "Brush.OnAccent");
        Bind(_card, WrappedCard.FontFaceProperty, "FontFamily.Ui");
        Bind(_card, WrappedCard.NumberFontFaceProperty, "FontFamily.Numbers");
        Bind(_card, WrappedCard.SmallSizeProperty, "Double.FontSize.Small");
        Bind(_card, WrappedCard.BodySizeProperty, "Double.FontSize.Body");
        Bind(_card, WrappedCard.TitleSizeProperty, "Double.FontSize.Title");
        Bind(_card, WrappedCard.DisplaySizeProperty, "Double.FontSize.Display");
        Bind(_pager, WrappedPager.LineBrushProperty, "Brush.Border");
        Bind(_pager, WrappedPager.IconBrushProperty, "Brush.Text");
        Bind(_pager, WrappedPager.DotBrushProperty, "Brush.Border");
        Bind(_pager, WrappedPager.ActiveBrushProperty, "Brush.Accent");
        _card.Margin = new Thickness(16, 14, 16, 0);
        _card.Cursor = Cursors.Arrow;
        _card.MouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(this); _card.CaptureMouse(); };
        _card.MouseLeftButtonUp += (_, e) =>
        {
            _card.ReleaseMouseCapture();
            if (_dragFrom is not { } from) return;
            _dragFrom = null;
            var dx = e.GetPosition(this).X - from.X;
            if (dx <= -SwipeDistance) Go(_index + 1);
            else if (dx >= SwipeDistance) Go(_index - 1);
        };
        _pager.Width = 190;
        _pager.HorizontalAlignment = HorizontalAlignment.Left;
        _pager.PageRequested += Go;

        _save.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border Background="{DynamicResource Brush.Accent}" CornerRadius="{DynamicResource CornerRadius.Small}" Padding="14,0" Height="32">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{DynamicResource Brush.OnAccent}"/>
              </Border>
            </ControlTemplate>
            """);
        _save.SetResourceReference(FontFamilyProperty, "FontFamily.Ui");
        _save.SetResourceReference(FontSizeProperty, "Double.FontSize.Body");
        _save.FontWeight = FontWeights.Bold;
        _save.Cursor = Cursors.Hand;
        _save.Focusable = false;
        _save.HorizontalAlignment = HorizontalAlignment.Right;
        _save.VerticalAlignment = VerticalAlignment.Center;
        _save.Click += (_, _) => Save();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new Grid { Margin = new Thickness(16, 10, 16, 8) };
        bar.Children.Add(_pager);
        bar.Children.Add(_save);
        Grid.SetRow(bar, 1);
        root.Children.Add(_card);
        root.Children.Add(bar);
        Content = root;

        _timer.Tick += (_, _) => Refresh();
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        Unloaded += (_, _) => Stop();
        Redraw(); // static text and an empty card; data follows in Start
    }

    private static void Bind(FrameworkElement el, DependencyProperty dp, string key) => el.SetResourceReference(dp, key);

    private void Go(int index)
    {
        index = Math.Clamp(index, 0, CardCount - 1);
        if (index == _index) return;
        _index = index;
        Redraw();
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
        WrappedResult result;
        try { result = await Task.Run(() => WrappedQuery.Run(_dbPath, now, TimeZoneInfo.Local, running)); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { return; }
        if (seq != _seq || !_started) return;
        _result = result;
        Redraw();
#if DEBUG
        var auto = Environment.GetEnvironmentVariable("TS_WRAPPED_SAVETO"); // test aid: write the visible card, no dialog (decision 115)
        if (!string.IsNullOrEmpty(auto))
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { try { CardImage.Write(_card, auto); } catch (IOException) { } });
#endif
    }

    // ---- text -------------------------------------------------------------------------------------------------------

    private static string Plural(long n, string one, string many) => n.ToString(CultureInfo.CurrentCulture) + " " + Str(n == 1 ? one : many);
    private static string MonthAbbr(int month) => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames[(month - 1 + 12) % 12];
    private static string ShortDate(DateOnly d) => d.ToString("d MMM", CultureInfo.CurrentCulture);

    private static string Hours(long seconds) =>
        seconds < 3600 ? DurationFormat.HoursMinutes(seconds) : Str(WrappedStringKeys.ShortHours, seconds / 3600);

    private WrappedCardModel BuildModel(WrappedResult r, int index)
    {
        if (r.TotalSeconds <= 0)
        {
            var label = index switch { 1 => WrappedStringKeys.Card2Label, 2 => WrappedStringKeys.Card3Label, _ => WrappedStringKeys.Card1Label };
            return new EmptyCardModel(Str(label, r.Year), Str(YearStringKeys.Empty));
        }
        return index switch { 1 => TopApps(r), 2 => Habits(r), _ => Total(r) };
    }

    private static TotalCardModel Total(WrappedResult r)
    {
        var parts = new List<string>();
        if (r.EquivalentDays > 0) parts.Add(Plural(r.EquivalentDays, WrappedStringKeys.Day, WrappedStringKeys.Days));
        if (r.EquivalentHours > 0 || parts.Count == 0) parts.Add(Plural(r.EquivalentHours, WrappedStringKeys.Hour, WrappedStringKeys.Hours));
        var highlight = string.Join(Str(WrappedStringKeys.And), parts);
        var since = r.TrackingStarted is { } s ? (s.Year == r.Year ? ShortDate(s) : s.ToString("d MMM yyyy", CultureInfo.CurrentCulture)) : "";
        return new TotalCardModel(
            Str(WrappedStringKeys.Card1Label, r.Year),
            r.TotalHours.ToString(CultureInfo.CurrentCulture),
            Str(r.TotalHours == 1 ? WrappedStringKeys.Hour : WrappedStringKeys.Hours),
            Str(WrappedStringKeys.Equivalent, highlight), highlight,
            [
                new WrappedTile(r.DaysTracked.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileTracked, since)),
                new WrappedTile(r.DaysWithActivity.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileActive)),
                new WrappedTile(r.AppsTracked.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileApps)),
            ]);
    }

    private static string Letter(string name)
    {
        foreach (var ch in name) if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    private static TopAppsCardModel TopApps(WrappedResult r)
    {
        var p = r.Podium;
        WrappedPodiumEntry E(int i) => new(Letter(p[i].Name), p[i].Name, Hours(p[i].Seconds), i + 1);
        // Display order: second place, winner, third place.
        var order = p.Count >= 3 ? new[] { 1, 0, 2 } : p.Count == 2 ? new[] { 1, 0 } : new[] { 0 };
        var then = new List<(string, bool)>();
        if (r.Others.Count > 0)
        {
            then.Add((Str(WrappedStringKeys.Then) + " ", false));
            for (var i = 0; i < r.Others.Count; i++)
            {
                if (i > 0) then.Add((" · ", false));
                then.Add(($"{r.Others[i].Name} {Hours(r.Others[i].Seconds)}", true));
            }
        }
        return new TopAppsCardModel(Str(WrappedStringKeys.Card2Label), Str(WrappedStringKeys.TopHeadline, p[0].Name),
            order.Select(E).ToList(), then);
    }

    private static HabitsCardModel Habits(WrappedResult r)
    {
        const string none = "—";
        var streakValue = r.LongestStreakDays > 0 ? Plural(r.LongestStreakDays, WrappedStringKeys.Day, WrappedStringKeys.Days) : none;
        var streakSub = r.StreakStart is { } a && r.StreakEnd is { } b
            ? r.LongestStreakDays == 1 ? Str(WrappedStringKeys.StreakOne, ShortDate(a)) : Str(WrappedStringKeys.StreakSpan, ShortDate(a), ShortDate(b))
            : "";

        var busiestValue = r.BusiestMonth is { } m ? Str(WrappedStringKeys.BusiestValue, MonthAbbr(m), Hours(r.BusiestMonthSeconds)) : none;
        var busiestSub = r.BusiestMonth is { } bm
            ? r.BusiestMonthDeltaPercent is { } d
                ? Str(WrappedStringKeys.BusiestVs, (d >= 0 ? "+" : "-") + Math.Abs((long)Math.Round(d)).ToString(CultureInfo.CurrentCulture) + "%", MonthAbbr(bm - 1))
                : Str(WrappedStringKeys.BusiestOnly)
            : "";

        var peakValue = r.PeakHour is { } h ? Str(WrappedStringKeys.PeakValue, Hour(h), Hour(h + 1)) : none;

        return new HabitsCardModel(Str(WrappedStringKeys.Card3Label), Str(WrappedStringKeys.HabitsHeadline),
        [
            new WrappedHabit(Str(WrappedStringKeys.Streak), streakValue, streakSub),
            new WrappedHabit(Str(WrappedStringKeys.Busiest), busiestValue, busiestSub),
            new WrappedHabit(Str(WrappedStringKeys.Peak), peakValue, r.PeakHour is null ? "" : Str(WrappedStringKeys.PeakSub)),
            new WrappedHabit(Str(WrappedStringKeys.Noon), r.BeforeNoonPercent.ToString(CultureInfo.CurrentCulture) + "%",
                Str(r.BeforeNoonPercent >= 50 ? WrappedStringKeys.NoonSubEarly : WrappedStringKeys.NoonSub)),
        ]);
    }

    private void Redraw()
    {
        _pager.Index = _index;
        _pager.Count = CardCount;
        _save.Content = Str(WrappedStringKeys.Save);
        _card.AppBrushes = Enumerable.Range(0, 6).Select(i => Application.Current.TryFindResource("Brush.App" + i) as Brush ?? Brushes.Gray).ToList();
        _card.MediumRadius = Application.Current.TryFindResource("CornerRadius.Medium") is CornerRadius mr ? mr.TopLeft : 10;
        _card.LargeRadius = Application.Current.TryFindResource("CornerRadius.Large") is CornerRadius lr ? lr.TopLeft : 12;
        _pager.ButtonRadius = Application.Current.TryFindResource("CornerRadius.Small") is CornerRadius sr ? sr.TopLeft : 8;
        var names = new[] { Str(WrappedStringKeys.CardNameTotal), Str(WrappedStringKeys.CardNameTop), Str(WrappedStringKeys.CardNameHabits) };
        System.Windows.Automation.AutomationProperties.SetName(_card, Str(WrappedStringKeys.GoTo, _index + 1, names[_index]));
        System.Windows.Automation.AutomationProperties.SetName(_pager, Str(WrappedStringKeys.Prev) + " / " + Str(WrappedStringKeys.Next));
        _card.Model = _result is { } r ? BuildModel(r, _index) : null;
    }

    // ---- save as image ----------------------------------------------------------------------------------------------

    private void Save()
    {
        if (_result is not { } r || _card.Model is null) return;
        var window = Window.GetWindow(this) as FlyoutWindow;
        if (window is not null) window.KeepOpen = true; // the file dialog deactivates the flyout (decision 55)
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = Str(WrappedStringKeys.SaveTitle),
                FileName = string.Create(CultureInfo.InvariantCulture, $"timesink-{r.Year}-wrapped-{_index + 1}.png"),
                Filter = Str(WrappedStringKeys.SaveFilter),
                DefaultExt = ".png",
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;
            CardImage.Write(_card, dlg.FileName);
            _save.Content = Str(WrappedStringKeys.SaveDone);
            _savedReset?.Stop();
            _savedReset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _savedReset.Tick += (_, _) => { _savedReset.Stop(); _save.Content = Str(WrappedStringKeys.Save); };
            _savedReset.Start();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { if (window is not null) { window.KeepOpen = false; window.Activate(); } }
    }
}

/// <summary>Renders a card element to a PNG file (local only; <see cref="RenderTargetBitmap"/> at 2x for a crisp share image).</summary>
internal static class CardImage
{
    public static void Write(FrameworkElement card, string path, double scale = 2)
    {
        var w = card.ActualWidth;
        var h = card.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
