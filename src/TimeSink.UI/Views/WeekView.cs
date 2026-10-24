using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Views;

/// <summary>
/// Week tab ("C · Pulse" board): total, seven columns, 20-week heat grid with the streak, and per-app rows with
/// sparklines. Built lazily on first visit; data loads only after <see cref="Start"/> (so first paint is never delayed)
/// and refreshes every 30 s and on every pipe state change. All colours, fonts and text come from theme resources.
/// </summary>
public sealed class WeekView : UserControl
{
    private const int RefreshEverySeconds = 30;

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly LiveState _live;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(RefreshEverySeconds) };

    private readonly TextBlock _caption = Text("Soft", "Double.FontSize.Body", "Brush.TextMuted");
    private readonly TextBlock _total = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _streak = new() { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _streakSub = Text("Small", "Double.FontSize.Body", "Brush.TextMuted");
    private readonly WeekColumns _columns = new() { Height = 64 };
    private readonly TextBlock _firstDay = Text("Small", "Double.FontSize.Small", "Brush.TextMuted");
    private readonly TextBlock _lastDay = Text("Small", "Double.FontSize.Small", "Brush.TextMuted");
    private readonly TextBlock _gridCaption = Text("Small", "Double.FontSize.Small", "Brush.TextMuted");
    private readonly HeatGrid _grid = new();
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _empty = Text("Small", "Double.FontSize.Body", "Brush.TextMuted");

    private WeekResult? _result;
    private int _seq;
    private bool _started;

    public WeekView(string dbPath, ThemeService themes, LiveState live)
    {
        _dbPath = dbPath;
        _themes = themes;
        _live = live;

        _total.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        _total.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        _total.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Display");
        _streak.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _streak.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        _streak.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        _streakSub.HorizontalAlignment = HorizontalAlignment.Right;
        _firstDay.HorizontalAlignment = HorizontalAlignment.Left;
        _lastDay.HorizontalAlignment = HorizontalAlignment.Right;
        _gridCaption.Margin = new Thickness(0, 14, 0, 6);
        _empty.Margin = new Thickness(0, 14, 0, 0);
        _empty.SetResourceReference(TextBlock.TextProperty, "Str.WeekEmpty");
        _empty.Visibility = Visibility.Collapsed;
        _caption.SetResourceReference(TextBlock.TextProperty, "Str.WeekCaption");
        _streakSub.SetResourceReference(TextBlock.TextProperty, "Str.WeekStreakSub");
        _gridCaption.SetResourceReference(TextBlock.TextProperty, "Str.WeekGridCaption");

        _columns.SetResourceReference(WeekColumns.ColumnBrushProperty, "Brush.Heat2");
        _columns.SetResourceReference(WeekColumns.CurrentBrushProperty, "Brush.Accent");
        _columns.SetResourceReference(WeekColumns.FutureBrushProperty, "Brush.BorderSoft");
        _grid.SetResourceReference(HeatGrid.Level0BrushProperty, "Brush.Heat0");
        _grid.SetResourceReference(HeatGrid.Level1BrushProperty, "Brush.Heat1");
        _grid.SetResourceReference(HeatGrid.Level2BrushProperty, "Brush.Heat2");
        _grid.SetResourceReference(HeatGrid.Level3BrushProperty, "Brush.Heat3");
        _grid.SetResourceReference(HeatGrid.Level4BrushProperty, "Brush.Heat4");
        _grid.SetResourceReference(HeatGrid.FutureBrushProperty, "Brush.BorderSoft");

        var header = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        header.Children.Add(_total);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_streak);
        right.Children.Add(_streakSub);
        header.Children.Add(right);

        var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        axis.Children.Add(_firstDay);
        axis.Children.Add(_lastDay);

        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(_caption);
        root.Children.Add(header);
        root.Children.Add(_columns);
        root.Children.Add(axis);
        root.Children.Add(_gridCaption);
        root.Children.Add(_grid);
        root.Children.Add(_rows);
        root.Children.Add(_empty);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = root };

        _timer.Tick += (_, _) =>
        {
            if (Window.GetWindow(this) is FlyoutWindow { IsRevealed: false }) return; // warm standby: stay idle
            Refresh();
        };
        _live.Changed += Refresh;
        _themes.Applied += Redraw;
        AppIcons.Updated += Redraw;
        Unloaded += (_, _) => Stop();
    }

    private static TextBlock Text(string style, string size, string brush)
    {
        var t = new TextBlock();
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }

    /// <summary>Begin loading and refreshing. Called once the window has rendered.</summary>
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
        var now = DateTimeOffset.UtcNow;
        var running = _live.RunningIds;
        var path = _dbPath;
        WeekResult result;
        try
        {
            result = await Task.Run(() =>
                WeekQuery.Run(path, now, TimeZoneInfo.Local, WeekStarts.Parse(SettingsStore.Get(path, WeekStarts.SettingKey)), running));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException) { return; } // no DB yet
        if (seq != _seq || !_started) return;
        _result = result;
        Redraw();
    }

    private void Redraw()
    {
        if (_result is not { } r) return;
        _total.Text = DurationFormat.HoursMinutes(r.TotalSeconds);
        _streak.Text = Str(StringKeys.WeekStreak, r.StreakDays);
        _columns.Days = r.Days;
        _firstDay.Text = DayName(r.Days[0].Date);
        _lastDay.Text = DayName(r.Days[^1].Date);
        _grid.Cells = r.Heat;
        _empty.Visibility = r.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppIcons.Load(_dbPath, r.Apps.Select(a => a.AppId));
        _rows.Children.Clear();
        var paletteSize = Math.Max(1, _themes.Current.Apps.Count);
        foreach (var app in r.Apps) _rows.Children.Add(BuildRow(app, (int)(app.AppId % paletteSize)));
    }

    private static string DayName(DateOnly d) => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);

    private UIElement BuildRow(WeekAppRow app, int paletteIndex)
    {
        var appBrush = "Brush.App" + paletteIndex;

        var tile = new Border();
        tile.SetResourceReference(FrameworkElement.WidthProperty, "Double.Row.TileSmall");
        tile.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.TileSmall");
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        tile.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        var letter = new TextBlock
        {
            Text = app.Name.Length > 0 ? char.ToUpperInvariant(app.Name[0]).ToString() : "?",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        letter.SetResourceReference(TextBlock.ForegroundProperty, appBrush);
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        letter.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        if (!AppIcons.TryFill(tile, app.AppId, 3)) tile.Child = letter;

        var name = new TextBlock { Text = app.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

        var spark = new SparkBars { Values = app.Spark, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        spark.SetResourceReference(SparkBars.BarBrushProperty, appBrush);

        var time = new TextBlock { Text = DurationFormat.HoursMinutes(app.Seconds), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, MinWidth = 56, TextAlignment = TextAlignment.Right };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        time.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
        time.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");

        var grid = new Grid();
        grid.SetResourceReference(FrameworkElement.MarginProperty, "Thickness.RowList");
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(spark, 2);
        Grid.SetColumn(time, 3);
        grid.Children.Add(tile);
        grid.Children.Add(name);
        grid.Children.Add(spark);
        grid.Children.Add(time);
        return grid;
    }
}
