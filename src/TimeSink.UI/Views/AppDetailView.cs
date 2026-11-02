using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// App detail page: full-flyout overlay matching the Mac layout with header (‹ All, App detail, Settings),
/// hero with lifetime hours & share %, 3 stat cards, multi-device split bar, period stepper,
/// segmented control (Matrix, All, Year, Month, Week), and footer (App time, not uptime, Export app CSV).
/// </summary>
public sealed class AppDetailView : UserControl
{
    private const int RefreshEverySeconds = 30;
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly long _appId;
    private readonly Action _close;
    private readonly UsageSnapshotLoader _loader;
    private readonly LiveViewDriver _driver;

    private AppDetailResult? _result;
    private int _year;
    private DayOfWeek _weekStart = DayOfWeek.Monday;
    private AppDetailRange _range = AppDetailRange.Matrix;
    private DateOnly _anchor;
    private int _mergedCount;
    private long _lifetimeTotal;
    private bool _showingSettings;
    private AddAppView? _settingsView;
    private TextBlock _matrixReadout = Label();
    private TextBlock _barsReadout = Label();

    public AppDetailView(string dbPath, ThemeService themes, LiveState live, long appId, Action close)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;
        _appId = appId;
        _close = close;
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_showingSettings)
            {
                _close();
                e.Handled = true;
            }
        };

        _loader = UsageSnapshotLoader.ForDb(dbPath, () => _live.RunningIds, SignInTime.Cached, () => _live.GamesOnly, () => _live.DeviceScope);
        (_year, _anchor) = (_loader.Today.Year, _loader.Today);
        _driver = new LiveViewDriver(_live, _themes, _loader, TimeSpan.FromSeconds(RefreshEverySeconds), Refresh, Redraw);
        Loaded += (_, _) =>
        {
            Redraw();
            _driver.Start();
            Focus();
        };
        Unloaded += (_, _) => _driver.Stop();
    }

    public bool TryGoBack()
    {
        if (_showingSettings)
        {
            if (_settingsView != null && _settingsView.TryGoBack()) return true;
            _showingSettings = false;
            Refresh();
            return true;
        }
        return false;
    }

    private async void Refresh()
    {
        var (id, year) = (_appId, _year);
        AppDetailResult? result = null;
        var applied = await _loader.Refresh(
            c => (
                Result: AppDetailQuery.Run(c, id, year),
                WeekStart: c.WeekStartDay,
                LifetimeTotal: AllQuery.Run(c)?.Totals.Seconds ?? 0L
            ),
            r =>
            {
                result = r.Result;
                _weekStart = r.WeekStart;
                _lifetimeTotal = r.LifetimeTotal;
            });

        if (!applied) return;
        if (result is null) { _close(); return; }
        _result = result;
        _year = result.Year;
        _mergedCount = LoadMergedCount(id);
        Redraw();
    }

    private int LoadMergedCount(long id)
    {
        try
        {
            using var conn = Db.CreateConnection(_dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM apps WHERE canonical_id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            return 0;
        }
    }

    private void SwitchYear(int year)
    {
        _year = year;
        Refresh();
    }

    private void Redraw()
    {
        var root = new Grid();
        Content = root;

        if (_showingSettings)
        {
            _settingsView ??= new AddAppView(_themes, _dbPath, () => { });
            _settingsView.ShowAppSettingsDirect(_appId, () =>
            {
                _showingSettings = false;
                Refresh();
            });
            root.Children.Add(_settingsView);
            return;
        }

        var mainLayout = new Grid();
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44, GridUnitType.Pixel) });
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44, GridUnitType.Pixel) });

        var header = Header();
        Grid.SetRow(header, 0);
        mainLayout.Children.Add(header);

        var r = _result;
        if (r is not null)
        {
            AppIcons.Load(_dbPath, [r.AppId]);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = ContentSection(r)
            };
            Grid.SetRow(scroll, 1);
            mainLayout.Children.Add(scroll);

            var footer = Footer(r);
            Grid.SetRow(footer, 2);
            mainLayout.Children.Add(footer);
        }

        root.Children.Add(mainLayout);
    }

    private FrameworkElement Header()
    {
        var bar = new Grid { Height = 44, Margin = new Thickness(10, 0, 10, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: ‹ All
        var back = new Button { Height = 28, VerticalAlignment = VerticalAlignment.Center };
        back.SetResourceReference(StyleProperty, "LinkButton");
        back.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        var backStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        backStack.Children.Add(Chevron(left: true, "Brush.Accent", 16));
        var backText = new TextBlock { Text = "All", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        backStack.Children.Add(backText);
        back.Content = backStack;
        back.Click += (_, _) => _close();
        Grid.SetColumn(back, 0);
        bar.Children.Add(back);

        // Center: App detail
        var title = new TextBlock
        {
            Text = Str(AppDetailStringKeys.Title),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        // Right: Settings
        var settings = new Button { Height = 28, VerticalAlignment = VerticalAlignment.Center };
        settings.SetResourceReference(StyleProperty, "LinkButton");
        settings.SetResourceReference(Control.ForegroundProperty, "Brush.TextSoft");
        var settingsText = new TextBlock { Text = Str(AppDetailStringKeys.Settings), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        settings.Content = settingsText;
        settings.Click += (_, _) =>
        {
            _showingSettings = true;
            Redraw();
        };
        Grid.SetColumn(settings, 2);
        bar.Children.Add(settings);

        return bar;
    }

    private FrameworkElement Footer(AppDetailResult r)
    {
        var bar = new Grid { Height = 44, Margin = new Thickness(14, 0, 14, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        var uptime = new TextBlock
        {
            Text = Str(AppDetailStringKeys.FooterUptime),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        uptime.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        uptime.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        Grid.SetColumn(uptime, 0);
        bar.Children.Add(uptime);

        var export = new Button { VerticalAlignment = VerticalAlignment.Center };
        export.SetResourceReference(StyleProperty, "LinkButton");
        export.SetResourceReference(Control.ForegroundProperty, "Brush.TextSoft");
        var exportText = new TextBlock
        {
            Text = Str(AppDetailStringKeys.ExportCsv),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        export.Content = exportText;
        export.Click += (_, _) => ExportAppCsv(r);
        Grid.SetColumn(export, 2);
        bar.Children.Add(export);

        return border;
    }

    private void ExportAppCsv(AppDetailResult r)
    {
        var dlg = new SaveFileDialog
        {
            FileName = AppExport.FileName(r),
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv"
        };
        var window = Window.GetWindow(this) as FlyoutWindow;
        if (window is not null) window.KeepOpen = true;
        try
        {
            if (dlg.ShowDialog() == true)
            {
                AppExport.Write(r, dlg.FileName);
            }
        }
        finally
        {
            if (window is not null) { window.KeepOpen = false; window.Activate(); }
        }
    }

    private FrameworkElement ContentSection(AppDetailResult r)
    {
        var stack = new StackPanel { Margin = new Thickness(14, 4, 14, 10) };
        stack.Children.Add(Hero(r));

        var tiles = StatTiles(r);
        tiles.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(tiles);

        var scope = _live?.DeviceScope ?? TimeSink.Core.Devices.DeviceScope.This;
        if (scope == TimeSink.Core.Devices.DeviceScope.All && r.DeviceSeconds is { Count: > 1 })
        {
            var split = DeviceSplitBar(r);
            split.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(split);
        }

        var chartData = ChartData(r);
        var rangeRow = RangeRow(r, chartData);
        rangeRow.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(rangeRow);

        var chartBody = _range == AppDetailRange.Matrix ? MatrixSection(r) : BarsSection(chartData);
        chartBody.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(chartBody);

        return stack;
    }

    private FrameworkElement Hero(AppDetailResult r)
    {
        var tile = AppTile.Tracked(_themes, r.AppId, r.Name, TileStyle.Header);
        tile.Width = 44;
        tile.Height = 44;

        var name = new TextBlock
        {
            Text = r.Name,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");

        var trackedDate = r.FirstTracked is { } first
            ? UsageText.ShortDate(first, withYear: true)
            : "";
        var subText = _mergedCount > 0
            ? Str(AppDetailStringKeys.MergedTrackedSince, _mergedCount, trackedDate)
            : Str(AppDetailStringKeys.TrackedSince, trackedDate);

        var sub = new TextBlock
        {
            Text = subText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        sub.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");

        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (r.Background)
        {
            var titleRow = new DockPanel { LastChildFill = true };
            var badge = BackgroundBadge.Create();
            DockPanel.SetDock(badge, Dock.Right);
            titleRow.Children.Add(badge);
            titleRow.Children.Add(name);
            center.Children.Add(titleRow);
        }
        else center.Children.Add(name);
        center.Children.Add(sub);

        // Lifetime hours & share on the right
        var lifetime = Math.Max(1L, _lifetimeTotal > 0 ? _lifetimeTotal : r.TotalSeconds);
        var share = (int)Math.Round((double)r.TotalSeconds / lifetime * 100);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var hours = new TextBlock
        {
            Text = Str(AllStringKeys.Hours, UsageText.Hours(r.TotalSeconds)),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right
        };
        hours.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        hours.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");

        var shareLabel = new TextBlock
        {
            Text = Str(AppDetailStringKeys.LifetimeShare, share),
            FontSize = 10,
            TextAlignment = TextAlignment.Right
        };
        shareLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        shareLabel.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");

        right.Children.Add(hours);
        right.Children.Add(shareLabel);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(tile, 0);
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);

        grid.Children.Add(tile);
        grid.Children.Add(center);
        grid.Children.Add(right);
        return grid;
    }

    private FrameworkElement StatTiles(AppDetailResult r)
    {
        var grid = new Grid();
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (lastVal, isRunning) = LastOpenedText(r.Last);
        var tiles = new[]
        {
            StatCard(Str(AppDetailStringKeys.LastOpened), lastVal, isRunning ? "Brush.Accent" : "Brush.Text"),
            StatCard(Str(AppDetailStringKeys.AvgSession), r.SessionCount > 0 ? UsageText.HoursMinutes(r.AverageSessionSeconds) : Str(AppDetailStringKeys.Never), "Brush.Text"),
            StatCard(Str(AppDetailStringKeys.TimesOpened), r.SessionCount.ToString("N0", En), "Brush.Text"),
        };
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
            Grid.SetColumn(tiles[i], i);
            grid.Children.Add(tiles[i]);
        }
        return grid;
    }

    private (string Text, bool IsRunning) LastOpenedText(AppDetailLast? last)
    {
        if (last is null) return (Str(AppDetailStringKeys.Never), false);
        if (last.Running) return (Str(AppDetailStringKeys.RunningNow), true);
        var bucket = UsageText.DayOf(last.StartUtc, _loader.Now.ToUnixTimeSeconds(), _loader.Zone);
        var when = bucket.Kind switch
        {
            DayKind.Today => Str(AppDetailStringKeys.Today),
            DayKind.Yesterday => Str(AppDetailStringKeys.Yesterday),
            _ => bucket.InCurrentYear ? UsageText.WeekdayDayMonth(bucket.Date) : UsageText.ShortDate(bucket.Date, withYear: true),
        };
        return (when, false);
    }

    private static FrameworkElement StatCard(string label, string value, string colorBrushKey)
    {
        var l = new TextBlock { Text = label, FontSize = 10 };
        l.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        l.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");

        var v = new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        v.SetResourceReference(TextBlock.ForegroundProperty, colorBrushKey);
        v.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");

        var stack = new StackPanel();
        stack.Children.Add(l);
        stack.Children.Add(v);

        var box = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
            Child = stack
        };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return box;
    }

    private FrameworkElement DeviceSplitBar(AppDetailResult r)
    {
        var box = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            BorderThickness = new Thickness(1),
        };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var devInfo = DeviceInfoService.Load(_dbPath);
        var localDevId = _live?.LocalDeviceId;

        var ordered = r.DeviceSeconds!.OrderByDescending(kv => DeviceInfoService.IsLocal(devInfo, kv.Key, localDevId)).ToList();
        var devA = ordered[0];
        var devB = ordered.Count > 1 ? ordered[1] : new KeyValuePair<string, long>("", 0L);

        var isLocalA = DeviceInfoService.IsLocal(devInfo, devA.Key, localDevId);
        var nameA = DeviceInfoService.GetName(devInfo, devA.Key, isLocalA);
        var isLocalB = DeviceInfoService.IsLocal(devInfo, devB.Key, localDevId);
        var nameB = DeviceInfoService.GetName(devInfo, devB.Key, isLocalB);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var markerA = new DeviceMarker { IsLocal = isLocalA, Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var textA = new TextBlock { Text = $"{nameA} {UsageText.Hours(devA.Value)} h", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        textA.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        left.Children.Add(markerA);
        left.Children.Add(textA);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var textB = new TextBlock { Text = $"{nameB} {UsageText.Hours(devB.Value)} h", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        textB.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        var markerB = new DeviceMarker { IsLocal = isLocalB, Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(textB);
        right.Children.Add(markerB);

        header.Children.Add(left);
        header.Children.Add(right);

        var bar = new Grid { Height = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(devA.Value, GridUnitType.Star) });
        if (devB.Value > 0)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Pixel) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(devB.Value, GridUnitType.Star) });
        }

        var segA = new Border { CornerRadius = new CornerRadius(3, devB.Value > 0 ? 0 : 3, devB.Value > 0 ? 0 : 3, 3) };
        segA.SetResourceReference(Border.BackgroundProperty, TimeSink.Core.Devices.DeviceTokens.BrushKeyFor(isLocalA));
        Grid.SetColumn(segA, 0);
        bar.Children.Add(segA);

        if (devB.Value > 0)
        {
            var segB = new Border { CornerRadius = new CornerRadius(0, 3, 3, 0) };
            segB.SetResourceReference(Border.BackgroundProperty, TimeSink.Core.Devices.DeviceTokens.BrushKeyFor(isLocalB));
            Grid.SetColumn(segB, 2);
            bar.Children.Add(segB);
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(bar);
        box.Child = stack;
        return box;
    }

    private DateOnly CurrentAnchor() => _anchor;

    private AppDetailChartData ChartData(AppDetailResult r) =>
        AppDetailChart.Build(_range, _range == AppDetailRange.Year ? new DateOnly(r.Year, 1, 1) : CurrentAnchor(), _loader.Today, r.FirstTracked, _weekStart,
            r.DaySeconds ?? new Dictionary<DateOnly, long>(), r.Opens ?? []);

    private string PeriodLabel(AppDetailResult r, AppDetailChartData d) => _range switch
    {
        AppDetailRange.Matrix or AppDetailRange.Year => r.Year.ToString(En),
        AppDetailRange.Month => UsageText.MonthYear(d.From),
        AppDetailRange.Week => $"{UsageText.ShortDate(d.From)} – {UsageText.ShortDate(d.To)}",
        _ => "",
    };

    private FrameworkElement RangeRow(AppDetailResult r, AppDetailChartData d)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (_range != AppDetailRange.All)
        {
            var canPrev = (_range == AppDetailRange.Matrix || _range == AppDetailRange.Year) ? r.Year > r.FirstYear : d.CanPrev;
            var canNext = (_range == AppDetailRange.Matrix || _range == AppDetailRange.Year) ? r.Year < r.LastYear : d.CanNext;
            var labelText = PeriodLabel(r, d);

            var prev = YearButton(left: true, enabled: canPrev, AppDetailStringKeys.PrevPeriod, () =>
            {
                if (_range == AppDetailRange.Matrix || _range == AppDetailRange.Year) SwitchYear(r.Year - 1);
                else { _anchor = AppDetailChart.Step(_range, d.From, -1); Redraw(); }
            });
            var next = YearButton(left: false, enabled: canNext, AppDetailStringKeys.NextPeriod, () =>
            {
                if (_range == AppDetailRange.Matrix || _range == AppDetailRange.Year) SwitchYear(r.Year + 1);
                else { _anchor = AppDetailChart.Step(_range, d.From, 1); Redraw(); }
            });

            var year = new TextBlock
            {
                Text = labelText,
                MinWidth = _range == AppDetailRange.Week ? 96 : 40,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            year.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            year.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            year.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            strip.Children.Add(prev);
            strip.Children.Add(year);
            strip.Children.Add(next);
            Grid.SetColumn(strip, 0);
            row.Children.Add(strip);
        }

        string[] rangeLabels =
        [
            Str(AppDetailStringKeys.RangeMatrix),
            Str(AppDetailStringKeys.RangeAll),
            Str(AppDetailStringKeys.RangeYear),
            Str(AppDetailStringKeys.RangeMonth),
            Str(AppDetailStringKeys.RangeWeek)
        ];
        var segmented = Segmented(rangeLabels, (int)_range, i =>
        {
            _range = (AppDetailRange)i;
            _anchor = _loader.Today;
            Redraw();
        });
        Grid.SetColumn(segmented, 2);
        row.Children.Add(segmented);

        return row;
    }

    private static Button YearButton(bool left, bool enabled, string labelKey, Action click)
    {
        var b = new Button
        {
            Width = 24,
            Height = 24,
            Content = Chevron(left, enabled ? "Brush.Text" : "Brush.TextMuted", 13),
            IsEnabled = enabled
        };
        b.SetResourceReference(StyleProperty, "LinkButton");
        b.Padding = new Thickness(0);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(b, Str(labelKey));
        b.Click += (_, _) => click();
        return b;
    }

    private static Path Chevron(bool left, string brushKey, double size)
    {
        var p = new Path
        {
            Data = Geometry.Parse(left ? "M10,3 L5,8 L10,13" : "M6,3 L11,8 L6,13"),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        p.SetResourceReference(Shape.StrokeProperty, brushKey);
        return p;
    }

    private static FrameworkElement Segmented(IReadOnlyList<string> labels, int selected, Action<int> pick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var on = i == selected;
            var text = new TextBlock
            {
                Text = labels[i],
                Margin = new Thickness(8, 3, 8, 3),
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Surface" : "Brush.TextSoft");
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            text.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");

            var seg = new Border { Child = text, Cursor = on ? Cursors.Arrow : Cursors.Hand };
            seg.MouseLeftButtonUp += (_, _) => { if (!on) pick(index); };
            seg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            if (on) seg.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            row.Children.Add(seg);
        }
        var pill = new Border
        {
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            Child = row
        };
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        pill.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        pill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return pill;
    }

    private FrameworkElement MatrixSection(AppDetailResult r)
    {
        var stack = new StackPanel();
        var months = Months(r);
        stack.Children.Add(months);

        var footnote = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footnote.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footnote.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _matrixReadout = new TextBlock
        {
            Text = DefaultMatrixNote(r),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _matrixReadout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _matrixReadout.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        Grid.SetColumn(_matrixReadout, 0);
        footnote.Children.Add(_matrixReadout);

        var legend = Legend();
        Grid.SetColumn(legend, 1);
        footnote.Children.Add(legend);

        stack.Children.Add(footnote);
        return stack;
    }

    private string DefaultMatrixNote(AppDetailResult r) => r.PeakDaySeconds > 0
        ? Str(AppDetailStringKeys.QuartersPeak, UsageText.HoursMinutes(r.PeakDaySeconds))
        : Str(AppDetailStringKeys.FootnoteNone, r.Year.ToString(En));

    private FrameworkElement Months(AppDetailResult r)
    {
        var heat = Enumerable.Range(0, 5).Select(i => (Brush)FindResource("Brush.Heat" + i)).ToList();
        var outline = (Brush)FindResource("Brush.Border");
        var highlight = (Brush)FindResource("Brush.Accent");
        var weekStart = _weekStart;
        var byDate = r.Days.ToDictionary(d => d.Date);

        var grid = new UniformGrid4();
        for (var m = 1; m <= 12; m++)
        {
            var month = MonthGrid.Layout(r.Year, m, weekStart);
            var first = month.First;
            var slots = new AppDetailDay?[42];
            long seconds = 0;
            for (var d = 0; d < month.Days; d++)
            {
                var day = byDate[first.AddDays(d)];
                slots[month.Lead + d] = day;
                seconds += day.Seconds;
            }

            var cells = new MonthCells
            {
                Days = slots,
                HeatBrushes = heat,
                OutlineBrush = outline,
                HighlightBrush = highlight
            };
            cells.HoverChanged += d =>
            {
                _matrixReadout.Text = d is null ? DefaultMatrixNote(r) : DayText(d);
                cells.ToolTip = d is null ? null : DayText(d);
            };

            var name = Label();
            name.Text = UsageText.MonthAbbr(first.Month).ToUpperInvariant();
            name.FontSize = 10;
            var total = Label();
            total.Text = seconds > 0 ? Str("matrixHours", UsageText.Hours(seconds)) : "";
            total.FontSize = 10;
            total.HorizontalAlignment = HorizontalAlignment.Right;
            var label = new Grid { Width = 7 * MonthCells.Cell + 6 * MonthCells.Gap, Margin = new Thickness(0, 0, 0, 5) };
            label.Children.Add(name);
            label.Children.Add(total);

            var block = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            block.Children.Add(label);
            block.Children.Add(cells);
            grid.Children.Add(block);
        }
        return grid;
    }

    private static string DayText(AppDetailDay d)
    {
        var date = UsageText.WeekdayDayMonth(d.Date);
        return d.Kind switch
        {
            AppDetailDayKind.Future => Str(AppDetailStringKeys.NotYet, date),
            AppDetailDayKind.BeforeTracking => Str(AppDetailStringKeys.BeforeTracking, date),
            _ when d.Seconds <= 0 => Str(AppDetailStringKeys.NotOpened, date),
            _ => $"{date} · {UsageText.HoursMinutes(d.Seconds)} · {(d.Sessions == 1 ? Str(AppDetailStringKeys.SessionOne) : Str(AppDetailStringKeys.SessionMany, d.Sessions))}",
        };
    }

    private FrameworkElement Legend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 5; i++)
        {
            var sw = new Border { Width = 10, Height = 8, Margin = new Thickness(0, 0, i < 4 ? 2 : 0, 0) };
            sw.SetResourceReference(Border.BackgroundProperty, "Brush.Heat" + i);
            sw.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            row.Children.Add(sw);
        }
        return row;
    }

    private FrameworkElement BarsSection(AppDetailChartData d)
    {
        var stack = new StackPanel();
        var peak = d.Bars.Max(b => b.Seconds);

        _barsReadout = new TextBlock
        {
            Text = Str(AppDetailStringKeys.PeriodTotal, UsageText.HoursMinutes(d.TotalSeconds)),
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _barsReadout.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _barsReadout.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");

        var barStrip = BarStrip(
            d.Bars.Select(b => peak > 0 ? (double)b.Seconds / peak : 0).ToList(),
            LabelsFor(d.Bars.Select(b => b.Label).ToList()),
            d.Bars,
            hover: i => _barsReadout.Text = i is { } k ? BarHoverText(d.Bars[k]) : Str(AppDetailStringKeys.PeriodTotal, UsageText.HoursMinutes(d.TotalSeconds)),
            formatHover: BarHoverText,
            height: 120,
            maxBarWidth: 36);

        stack.Children.Add(barStrip);
        stack.Children.Add(_barsReadout);

        return stack;
    }

    private string BarHoverText(AppDetailBar b)
    {
        var label = BarName(b);
        if (b.Seconds <= 0)
            return Str(AppDetailStringKeys.NotOpened, label);
        var opens = b.Sessions == 1 ? Str(AppDetailStringKeys.SessionOne) : Str(AppDetailStringKeys.SessionMany, b.Sessions);
        return $"{label} · {UsageText.HoursMinutes(b.Seconds)} · {opens}";
    }

    private string BarName(AppDetailBar b) => _range switch
    {
        AppDetailRange.All => b.From.Year.ToString(En),
        AppDetailRange.Year => UsageText.MonthYear(b.From),
        _ => UsageText.WeekdayDayMonth(b.From),
    };

    private static List<string?> LabelsFor(IReadOnlyList<string> all) =>
        all.Select((l, i) => all.Count <= 12 || i % 5 == 0 ? l : null).ToList();

    private static FrameworkElement BarStrip(
        IReadOnlyList<double> values,
        IReadOnlyList<string?> labels,
        IReadOnlyList<AppDetailBar> barsData,
        Action<int?> hover,
        Func<AppDetailBar, string> formatHover,
        double? height = null,
        double maxBarWidth = double.PositiveInfinity)
    {
        var bars = new UniformGrid { Columns = values.Count, Rows = 1 };
        var names = new UniformGrid { Columns = values.Count, Rows = 1 };
        for (var i = 0; i < values.Count; i++)
        {
            var index = i;
            var barItem = barsData[i];
            var cell = new Grid { Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0), MaxWidth = maxBarWidth, Cursor = Cursors.Hand };
            cell.ToolTip = formatHover(barItem);
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - values[i], GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(values[i], GridUnitType.Star) });
            var fill = new Border { MinHeight = values[i] > 0 ? 2 : 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            fill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
            Grid.SetRow(fill, 1);
            cell.Children.Add(fill);

            cell.MouseEnter += (_, _) =>
            {
                fill.Opacity = 0.8;
                hover(index);
            };
            cell.MouseLeave += (_, _) =>
            {
                fill.Opacity = 1.0;
                hover(null);
            };

            bars.Children.Add(cell);

            var name = Label();
            name.Text = labels[i] ?? "";
            name.FontSize = 9;
            name.TextAlignment = TextAlignment.Center;
            name.TextWrapping = TextWrapping.NoWrap;
            names.Children.Add(name);
        }
        var baseline = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bars };
        baseline.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        if (height is { } h) baseline.Height = h;

        var strip = new Grid();
        strip.RowDefinitions.Add(new RowDefinition { Height = height is null ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        strip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        names.Margin = new Thickness(0, 3, 0, 0);
        Grid.SetRow(names, 1);
        strip.Children.Add(baseline);
        strip.Children.Add(names);
        return strip;
    }

    private static TextBlock Label()
    {
        var t = new TextBlock();
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        return t;
    }

    private sealed class UniformGrid4 : UniformGrid
    {
        public UniformGrid4() { Columns = 4; VerticalAlignment = VerticalAlignment.Top; }
    }
}
