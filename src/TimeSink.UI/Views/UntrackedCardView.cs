using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Views;

/// <summary>
/// The Today "N running apps aren't tracked" card (board PulseTodayEarly). Rules live in Core
/// (<see cref="TodayEarly"/>); this is the Win32 capture, icons and rendering. Track adds the app straight away and the
/// card redraws from the last capture; "Not this one" writes the ignore list. Every colour, font and text is a theme token.
/// </summary>
internal sealed class UntrackedCardView : StackPanel
{
    private sealed record Snapshot(bool On, IReadOnlyList<OpenWindow> Windows, ExcludedApps Excluded, IReadOnlyDictionary<string, string> Aumids, Dictionary<string, byte[]?> Icons);

    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly Action _changed;
    private readonly Dictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase); // tracked/dismissed here, until the next capture
    private Snapshot? _last;
    private bool _loading;

    public UntrackedCardView(string dbPath, ThemeService themes, Action changed)
    {
        _dbPath = dbPath;
        _themes = themes;
        _changed = changed;
        Visibility = Visibility.Collapsed;
        _themes.Applied += Render;
    }

    /// <summary>True while the card is on screen (the "Tracked today" header follows it).</summary>
    public bool HasCard => Visibility == Visibility.Visible;

    /// <summary>Raised after a render so the owner can show or hide the header below the card.</summary>
    public event Action? Rendered;

    public void Reload()
    {
        if (_loading) return;
        _loading = true;
        var known = _icons.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var path = _dbPath;
        Task.Run(() =>
        {
            try
            {
                var on = TodayEarly.SuggestionsEnabled(SettingsStore.Get(path, "tracking.suggestApps"));
                if (!on) return new Snapshot(false, [], new ExcludedApps([], []), new Dictionary<string, string>(), []);
                ExcludedApps excluded;
                using (var lib = AppLibrary.Open(path)) excluded = lib.ExcludedFromRunning();
                var snap = RunningWindows.Capture();
                var icons = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
                var card = TodayEarly.UntrackedCard(true, snap.Windows, excluded, TodayEarly.DefaultMaxShown + 2);
                if (card is not null)
                    foreach (var a in card.Shown.Where(a => !known.Contains(a.ExePath)))
                        icons[a.ExePath] = IconService.ExtractPng(a.ExePath, snap.AumidByPath.GetValueOrDefault(a.ExePath));
                return new Snapshot(true, snap.Windows, excluded, snap.AumidByPath, icons);
            }
            catch { return null; } // database missing/busy: keep what is on screen
        }).ContinueWith(t =>
        {
            _loading = false;
            if (t.Result is not { } snapshot) return;
            foreach (var (p, png) in snapshot.Icons) _icons[p] = png;
            _handled.Clear();
            _last = snapshot;
            Render();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Render()
    {
        Children.Clear();
        var card = _last is { On: true } l
            ? TodayEarly.UntrackedCard(true, l.Windows, new ExcludedApps(l.Excluded.Paths.Concat(_handled), l.Excluded.PackageFamilies))
            : null;
        Visibility = card is null ? Visibility.Collapsed : Visibility.Visible;
        if (card is not null) Children.Add(BuildCard(card));
        Rendered?.Invoke();
    }

    private UIElement BuildCard(UntrackedCard card)
    {
        var frame = new Rectangle { StrokeDashArray = [4, 3], StrokeThickness = 1, Fill = Brushes.Transparent, IsHitTestVisible = false };
        frame.SetResourceReference(Rectangle.RadiusXProperty, "Double.Radius.Large");
        frame.SetResourceReference(Rectangle.RadiusYProperty, "Double.Radius.Large");
        frame.SetResourceReference(Shape.StrokeProperty, "Brush.Border");

        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 6) };
        var dot = new Ellipse { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(FrameworkElement.WidthProperty, "Double.Size.Dot");
        dot.SetResourceReference(FrameworkElement.HeightProperty, "Double.Size.Dot");
        dot.SetResourceReference(Shape.FillProperty, "Brush.Warning");
        var title = Label(card.Count == 1 ? TodayEarlyStrings.UntrackedOne : TodayEarlyStrings.UntrackedMany, card.Count, "Double.FontSize.Small", "Brush.Text", FontWeights.Bold);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        head.Children.Add(dot);
        head.Children.Add(title);
        body.Children.Add(head);
        foreach (var app in card.Shown) body.Children.Add(BuildRow(app));

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.Children.Add(frame);
        grid.Children.Add(body);
        return grid;
    }

    private UIElement BuildRow(RunningApp app)
    {
        var tile = new Border { Width = 26, Height = 26, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        if (IconService.Decode(_icons.GetValueOrDefault(app.ExePath)) is { } bmp)
        {
            tile.Padding = new Thickness(2);
            tile.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
        }
        else
        {
            tile.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            var letter = new TextBlock { Text = LetterTile.Letter(app.DisplayName), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            letter.SetResourceReference(TextBlock.ForegroundProperty, "Brush.App" + LetterTile.PaletteIndex(LetterTile.KeyFor(app.ExePath), Math.Max(1, _themes.Current.Apps.Count)));
            letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            letter.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
            tile.Child = letter;
        }

        var name = new TextBlock { Text = app.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var since = Label(TodayEarlyStrings.OpenSince, TimeText.Clock(app.StartUtc.ToUnixTimeSeconds()), "Double.FontSize.Small", "Brush.TextMuted", FontWeights.Normal);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        texts.Children.Add(name);
        texts.Children.Add(since);

        var track = Button("Str.AddTrack", "Brush.OnAccent", "Brush.Accent", "Brush.Accent", FontWeights.Bold, new Thickness(12, 4, 12, 4));
        track.Click += (_, _) => Track(app);
        var skip = Button("Str." + char.ToUpperInvariant(TodayEarlyStrings.NotThisOne[0]) + TodayEarlyStrings.NotThisOne[1..], "Brush.TextMuted", "Brush.Panel", "Brush.Border", FontWeights.Normal, new Thickness(8, 4, 8, 4));
        skip.Margin = new Thickness(0, 0, 6, 0);
        skip.Click += (_, _) => Dismiss(app);

        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(skip, 2);
        Grid.SetColumn(track, 3);
        row.Children.Add(tile);
        row.Children.Add(texts);
        row.Children.Add(skip);
        row.Children.Add(track);

        var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Margin = new Thickness(0, 6, 0, 0), Child = row };
        row.Margin = new Thickness(0);
        line.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        return line;
    }

    private void Track(RunningApp app)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.AddTracked(new NewApp(app.ExePath, app.DisplayName, app.PackageFamily), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), _icons.GetValueOrDefault(app.ExePath));
        }
        catch { return; } // busy or missing database: the button stays so the user can retry
        Handled(app);
    }

    private void Dismiss(RunningApp app)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.Ignore(app.ExePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch { return; }
        Handled(app);
    }

    private void Handled(RunningApp app)
    {
        _handled.Add(app.ExePath);
        Render();   // the card updates at once from the last capture
        Reload();   // then refills (icons for the next suggestion) from a fresh capture
        _changed(); // Today picks the new app up as soon as the Agent records it
    }

    private static Button Button(string contentKey, string fg, string bg, string border, FontWeight weight, Thickness padding)
    {
        var b = new Button { Template = FlatTemplate, Height = 28, Padding = padding, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(ContentControl.ContentProperty, contentKey);
        b.SetResourceReference(Control.ForegroundProperty, fg);
        b.SetResourceReference(Control.BackgroundProperty, bg);
        b.SetResourceReference(Control.BorderBrushProperty, border);
        b.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Small");
        return b;
    }

    private static TextBlock Label(string key, object arg, string sizeKey, string brushKey, FontWeight weight)
    {
        var t = new TextBlock { Text = TimeText.Str(key, arg), FontWeight = weight };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return t;
    }

    private static readonly ControlTemplate FlatTemplate = MakeTemplate();

    private static ControlTemplate MakeTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
