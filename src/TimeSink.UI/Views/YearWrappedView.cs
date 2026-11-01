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
using TimeSink.Core.Apps;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Year > Wrapped: six pageable cards (total hours, top apps, top games and top gacha podiums, habits, longest sessions; board PulseYearWrapped) and "Save as
/// image", which writes the visible card to a PNG. Built in code; data arrives after first paint via <see cref="Start"/>.
/// </summary>
public sealed class YearWrappedView : UserControl, IYearSubView
{
    private const int RefreshEverySeconds = 30;
    private const int CardCount = 6; // total, top apps, top games, top gacha, habits, longest sessions
    private const double SwipeDistance = 40;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;
    private readonly WrappedCard _card = new();
    private readonly WrappedPager _pager = new() { Count = CardCount };
    private readonly Button _save = new();

    private WrappedResult? _result;
    private int _index;
    private int _year = DateTime.Today.Year;
    private readonly Action<DateOnly?>? _onTrackingStarted;
    private Point? _dragFrom;
    private DispatcherTimer? _savedReset;

    public YearWrappedView(string dbPath, ThemeService themes, LiveState live, Action<DateOnly?>? onTrackingStarted = null)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _onTrackingStarted = onTrackingStarted;
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
        Bind(_card, WrappedCard.DeviceABrushProperty, "Brush.DeviceA");
        Bind(_card, WrappedCard.DeviceBBrushProperty, "Brush.DeviceB");
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

        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly, () => _live.DeviceScope);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw);
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

    public void SetYear(int year)
    {
        if (_year == year) return;
        _year = year;
        Refresh();
    }

    public void Start() => _driver.Start();

    public void Stop() => _driver.Stop();

    public async void Refresh()
    {
        WrappedResult? loaded = null;
        if (!await _loader.Refresh(c => WrappedQuery.Run(c, _year), r => { loaded = r; _onTrackingStarted?.Invoke(r.TrackingStarted); })) return;
        var result = loaded!;
        _result = result;
        AppIcons.Load(_dbPath, result.Podium.Concat(result.Others).Concat(result.GamesTop?.Podium ?? []).Concat(result.GachaTop?.Podium ?? []).Concat(result.LongestSessions ?? []).Select(a => a.AppId));
        Redraw();
#if DEBUG
        var auto = Environment.GetEnvironmentVariable("TS_WRAPPED_SAVETO"); // test aid: write the visible card, no dialog (decision 115)
        if (!string.IsNullOrEmpty(auto))
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { try { CardImage.Write(_card, auto); } catch (IOException) { } });
#endif
    }

    // ---- text -------------------------------------------------------------------------------------------------------

    private static string Plural(long n, string one, string many) => n.ToString(CultureInfo.CurrentCulture) + " " + Str(n == 1 ? one : many);

    private static string Hours(long seconds) =>
        seconds < 3600 ? UsageText.HoursMinutes(seconds) : Str(WrappedStringKeys.ShortHours, UsageText.Hours(seconds));

    private WrappedCardModel BuildModel(WrappedResult r, int index)
    {
        if (r.TotalSeconds <= 0)
        {
            var label = index switch
            {
                1 => WrappedStringKeys.Card2Label, 2 => WrappedStringKeys.Card4Label, 3 => WrappedStringKeys.Card5Label,
                4 => WrappedStringKeys.Card6Label, 5 => WrappedStringKeys.Card3Label, _ => WrappedStringKeys.Card1Label,
            };
            return new EmptyCardModel(Str(label, r.Year), Str(YearStringKeys.Empty));
        }
        return index switch
        {
            1 => Podium(Str(WrappedStringKeys.Card2Label), WrappedStringKeys.TopHeadline, r.Podium, r.Others, ""),
            2 => Podium(Str(WrappedStringKeys.Card4Label), WrappedStringKeys.GamesHeadline, r.GamesTop?.Podium, r.GamesTop?.Others, WrappedStringKeys.NoGames),
            3 => Podium(Str(WrappedStringKeys.Card5Label), WrappedStringKeys.GachaHeadline, r.GachaTop?.Podium, r.GachaTop?.Others, WrappedStringKeys.NoGacha),
            4 => Podium(Str(WrappedStringKeys.Card6Label), WrappedStringKeys.LongestHeadline, r.LongestSessions, null, "", isLongest: true),
            5 => Habits(r),
            _ => Total(r),
        };
    }

    private (bool IsMultiDev, string DevAName, string DevBName, Dictionary<string, (string Name, bool IsLocal)> DevInfo, string? LocalId) GetDeviceInfo(WrappedResult r)
    {
        var scope = _live?.DeviceScope ?? TimeSink.Core.Devices.DeviceScope.This;
        var isMultiDev = scope == TimeSink.Core.Devices.DeviceScope.All && r.DeviceTotals is { Count: > 1 };
        var devInfo = DeviceInfoService.Load(_dbPath);
        var localId = _live?.LocalDeviceId;
        var localEntry = devInfo.Values.FirstOrDefault(v => v.IsLocal);
        var remoteEntry = devInfo.Values.FirstOrDefault(v => !v.IsLocal);
        var devAName = !string.IsNullOrEmpty(localEntry.Name) ? localEntry.Name : "PC";
        var devBName = !string.IsNullOrEmpty(remoteEntry.Name) ? remoteEntry.Name : "Mac";
        return (isMultiDev, devAName, devBName, devInfo, localId);
    }

    private TotalCardModel Total(WrappedResult r)
    {
        var parts = new List<string>();
        if (r.EquivalentDays > 0) parts.Add(Plural(r.EquivalentDays, WrappedStringKeys.Day, WrappedStringKeys.Days));
        if (r.EquivalentHours > 0 || parts.Count == 0) parts.Add(Plural(r.EquivalentHours, WrappedStringKeys.Hour, WrappedStringKeys.Hours));
        var highlight = string.Join(Str(WrappedStringKeys.And), parts);
        var since = r.TrackingStarted is { } s ? (s.Year == r.Year ? UsageText.ShortDate(s) : UsageText.ShortDate(s, withYear: true)) : "";

        var (isMultiDev, devAName, devBName, devInfo, localId) = GetDeviceInfo(r);


        long secA = 0, secB = 0;
        if (isMultiDev)
        {






            if (localId != null && r.DeviceTotals!.TryGetValue(localId, out secA))
            {
                foreach (var (k, v) in r.DeviceTotals)
                    if (k != localId) secB += v;
            }
            else
            {
                var first = true;
                foreach (var v in r.DeviceTotals!.Values)
                {
                    if (first) { secA = v; first = false; }
                    else secB += v;
                }
            }
        }

        return new TotalCardModel(
            Str(r.Year == _loader.Today.Year ? WrappedStringKeys.Card1Label : WrappedStringKeys.Card1LabelPast, r.Year),
            r.TotalHours.ToString(CultureInfo.CurrentCulture),
            Str(r.TotalHours == 1 ? WrappedStringKeys.Hour : WrappedStringKeys.Hours),
            Str(isMultiDev ? WrappedStringKeys.EquivalentAll : WrappedStringKeys.Equivalent, highlight), highlight,
            [
                new WrappedTile(r.DaysTracked.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileTracked, since)),
                new WrappedTile(r.DaysWithActivity.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileActive)),
                new WrappedTile(r.AppsTracked.ToString(CultureInfo.CurrentCulture), Str(WrappedStringKeys.TileApps)),
            ],
            IsMultiDevice: isMultiDev,
            DeviceAName: devAName,
            DeviceASeconds: secA,
            DeviceBName: devBName,
            DeviceBSeconds: secB,
            OnBothSeconds: r.OnBothSeconds,
            TotalSeconds: r.TotalSeconds);
    }

    private WrappedCardModel Podium(string label, string headlineKey, IReadOnlyList<WrappedApp>? p, IReadOnlyList<WrappedApp>? others, string emptyKey, bool isLongest = false)
    {
        if (p is not { Count: > 0 }) return new EmptyCardModel(label, Str(emptyKey.Length > 0 ? emptyKey : YearStringKeys.Empty));
        var (isMultiDev, devAName, devBName, devInfo, localId) = _result != null ? GetDeviceInfo(_result) : (false, "PC", "Mac", new Dictionary<string, (string, bool)>(), null);

        WrappedPodiumEntry E(int i)
        {
            var app = p[i];
            long appSecA = 0, appSecB = 0;
            string? subtext = null;
            var isDevBOnly = false;

            if (isMultiDev)
            {
                if (isLongest)
                {
                    var isLocal = DeviceInfoService.IsLocal(devInfo, app.DeviceId, localId);
                    isDevBOnly = !isLocal;
                    if (isLocal) appSecA = app.Seconds;
                    else appSecB = app.Seconds;
                    var marker = isLocal ? "●" : "■";
                    var dName = isLocal ? devAName : devBName;
                    subtext = $"{marker} {dName}";
                }
                else if (app.DeviceSeconds is { Count: > 0 })
                {
                    foreach (var (dev, s) in app.DeviceSeconds)
                    {
                        if (DeviceInfoService.IsLocal(devInfo, dev, localId)) appSecA += s;
                        else appSecB += s;
                    }

                    if (appSecA > 0 && appSecB > 0)
                        subtext = $"● {Hours(appSecA)} · ■ {Hours(appSecB)}";
                    else if (appSecA > 0)
                        subtext = $"● {Hours(appSecA)}";
                    else if (appSecB > 0)
                    {
                        isDevBOnly = true;
                        subtext = $"■ {Hours(appSecB)}";
                    }
                }
            }

            return new WrappedPodiumEntry(
                LetterTile.Letter(app.Name),
                app.Name,
                Hours(app.Seconds),
                i + 1,
                AppIcons.Get(app.AppId),
                _themes.AppSlot(app.AppId),
                DeviceASeconds: appSecA,
                DeviceBSeconds: appSecB,
                DeviceSubtext: subtext,
                IsDeviceBOnly: isDevBOnly);
        }
        // Display order: winner in the middle, then 2nd to its left, 3rd right, 4th far left, 5th far right.
        var order = new List<int> { 0 };
        for (var i = 1; i < p.Count; i++) { if (i % 2 == 1) order.Insert(0, i); else order.Add(i); }
        var then = new List<(string, bool)>();
        if (others is { Count: > 0 })
        {
            then.Add((Str(WrappedStringKeys.Then) + " ", false));
            for (var i = 0; i < others.Count; i++)
            {
                if (i > 0) then.Add((" · ", false));
                var o = others[i];
                var markers = "";
                if (isMultiDev && o.DeviceSeconds is { Count: > 0 })
                {
                    long oSecA = 0, oSecB = 0;
                    foreach (var (dev, s) in o.DeviceSeconds)
                    {
                        if (DeviceInfoService.IsLocal(devInfo, dev, localId)) oSecA += s;
                        else oSecB += s;
                    }
                    if (oSecA > 0 && oSecB > 0) markers = " ●■";
                    else if (oSecA > 0) markers = " ●";
                    else if (oSecB > 0) markers = " ■";
                }
                then.Add(($"{o.Name} {Hours(o.Seconds)}{markers}", true));
            }
        }
        return new TopAppsCardModel(label, Str(headlineKey, p[0].Name), order.Select(E).ToList(), then, IsMultiDevice: isMultiDev);
    }

    private HabitsCardModel Habits(WrappedResult r)
    {
        const string none = "â";
        var streakValue = r.LongestStreakDays > 0 ? Plural(r.LongestStreakDays, WrappedStringKeys.Day, WrappedStringKeys.Days) : none;
        var streakSub = r.StreakStart is { } a && r.StreakEnd is { } b
            ? r.LongestStreakDays == 1 ? Str(WrappedStringKeys.StreakOne, UsageText.ShortDate(a)) : Str(WrappedStringKeys.StreakSpan, UsageText.ShortDate(a), UsageText.ShortDate(b))
            : "";

        var busiestValue = r.BusiestMonth is { } m ? Str(WrappedStringKeys.BusiestValue, UsageText.MonthAbbr(m), Hours(r.BusiestMonthSeconds)) : none;
        var busiestSub = r.BusiestMonth is { } bm
            ? r.BusiestMonthDelta is { } d
                ? Str(WrappedStringKeys.BusiestVs, UsageText.Delta(d), UsageText.MonthAbbr(bm - 1))
                : Str(WrappedStringKeys.BusiestOnly)
            : "";

        var peakValue = r.PeakHour is { } h ? Str(WrappedStringKeys.PeakValue, Hour(h), Hour(h + 1)) : none;
        var noonSub = Str(r.BeforeNoonPercent >= 50 ? WrappedStringKeys.NoonSubEarly : WrappedStringKeys.NoonSub);

        var (isMultiDev, devAName, devBName, devInfo, localId) = GetDeviceInfo(r);
        double bmSplitA = 0, bmSplitB = 0;
        double bnSplitA = 0, bnSplitB = 0;

        if (isMultiDev)
        {
            if (r.BusiestMonthDeviceSeconds is { Count: > 0 })
            {
                long bmSecA = 0, bmSecB = 0;
                foreach (var (dev, s) in r.BusiestMonthDeviceSeconds)
                {
                    if (DeviceInfoService.IsLocal(devInfo, dev, localId)) bmSecA += s;
                    else bmSecB += s;
                }
                bmSplitA = bmSecA;
                bmSplitB = bmSecB;
                var splitText = $"● {devAName} {Hours(bmSecA)} · ■ {devBName} {Hours(bmSecB)}";
                busiestSub = string.IsNullOrEmpty(busiestSub) ? splitText : $"{busiestSub}\n{splitText}";
            }

            if (r.BeforeNoonDeviceSeconds is { Count: > 0 })
            {
                long bnSecA = 0, bnSecB = 0;
                foreach (var (dev, s) in r.BeforeNoonDeviceSeconds)
                {
                    if (DeviceInfoService.IsLocal(devInfo, dev, localId)) bnSecA += s;
                    else bnSecB += s;
                }
                bnSplitA = bnSecA;
                bnSplitB = bnSecB;
                var splitText = $"● {devAName} {Hours(bnSecA)} · ■ {devBName} {Hours(bnSecB)}";
                noonSub = $"{noonSub}\n{splitText}";
            }
        }

        return new HabitsCardModel(Str(WrappedStringKeys.Card3Label), Str(WrappedStringKeys.HabitsHeadline),
        [
            new WrappedHabit(Str(WrappedStringKeys.Streak), streakValue, streakSub),
            new WrappedHabit(Str(WrappedStringKeys.Busiest), busiestValue, busiestSub, bmSplitA, bmSplitB),
            new WrappedHabit(Str(WrappedStringKeys.Peak), peakValue, r.PeakHour is null ? "" : Str(WrappedStringKeys.PeakSub)),
            new WrappedHabit(Str(WrappedStringKeys.Noon), r.BeforeNoonPercent.ToString(CultureInfo.CurrentCulture) + "%", noonSub, bnSplitA, bnSplitB),

        ]);
    }

    private void Redraw()
    {
        _pager.Index = _index;
        _pager.Count = CardCount;
        _save.Content = Str(WrappedStringKeys.Save);
        _card.AppBrushes = Enumerable.Range(0, Math.Max(1, _themes.Current.Apps.Count)).Select(i => Application.Current.TryFindResource("Brush.App" + i) as Brush ?? (Brush)Application.Current.FindResource("Brush.TextMuted")).ToList();
        _card.MediumRadius = Application.Current.TryFindResource("CornerRadius.Medium") is CornerRadius mr ? mr.TopLeft : 10;
        _card.LargeRadius = Application.Current.TryFindResource("CornerRadius.Large") is CornerRadius lr ? lr.TopLeft : 12;
        _pager.ButtonRadius = Application.Current.TryFindResource("CornerRadius.Small") is CornerRadius sr ? sr.TopLeft : 8;
        var names = new[] { Str(WrappedStringKeys.CardNameTotal), Str(WrappedStringKeys.CardNameTop), Str(WrappedStringKeys.CardNameGames), Str(WrappedStringKeys.CardNameGacha), Str(WrappedStringKeys.CardNameLongest), Str(WrappedStringKeys.CardNameHabits) };
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
