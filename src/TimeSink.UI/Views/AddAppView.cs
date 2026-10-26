using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Views;

/// <summary>
/// Add app screen (board PulseAdd): a full-flyout overlay with the Running tab. Built in code; every colour, font and
/// text comes from theme resources (<c>Brush.*</c>, <c>Str.*</c>). Tracking writes through <see cref="AppLibrary"/>;
/// the Agent picks the new app up on its next tick (decision 39).
/// </summary>
public sealed partial class AddAppView : UserControl
{
    private sealed class Row
    {
        public required RunningApp App;
        public required string? Aumid;
        public byte[]? Png;
        public bool Done;
        public Border Tile = null!;
        public Button Button = null!;
        public TextBlock? Open;
    }

    private static readonly ControlTemplate Flat = (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
        "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' " +
        "CornerRadius='{DynamicResource CornerRadius.Small}' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}'/>" +
        "</Border></ControlTemplate>");

    private readonly ThemeService _themes;
    private readonly string _dbPath;
    private readonly Action _close;
    private readonly StackPanel _list = new();
    private readonly TextBlock _count = new();
    private readonly AppCatalogue _catalogue;
    private readonly Dictionary<string, byte[]?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private List<Row> _rows = [];
    private bool _loading;
    private ContentControl _body = null!;
    private UIElement _shell = null!;
    private string _tab = "Running";
    private readonly Dictionary<string, RadioButton> _tabButtons = new();

    public AddAppView(ThemeService themes, string dbPath, Action close)
    {
        _themes = themes;
        _dbPath = dbPath;
        _close = close;
        _catalogue = new AppCatalogue(dbPath, Win32Desktop.Instance);
        SetResourceReference(BackgroundProperty, "Brush.Surface");
        Content = _shell = BuildShell();
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _themes.Applied += Redraw; Redraw(); Refresh(); _timer.Start(); };
        Unloaded += (_, _) => { _themes.Applied -= Redraw; _timer.Stop(); };
    }

    /// <summary>
    /// The view is kept between visits (icons, the installed list and the built Running tab survive); this drops everything
    /// that belonged to the previous visit and refreshes what may have changed while it was closed.
    /// </summary>
    public void OnShown()
    {
        _catalogue.NewVisit();
        _query = "";
        _trackedQuery = "";
        _openId = null;
        _mode = "";
        _mergePlan = null;
        _mergeOpening = null;
        _restoreOffset = false;
        _settingsId = null;
        _settingsReturn = null;
        _settingsScroll = null;
        _settingsAsk = "";
        _clearStep = 0;
        _libraryLoaded = false;
        Content = _shell;
        UpdateCount();
        _rows.RemoveAll(r => r.Done);
        if (_tab != "Running") _tabButtons["Running"].IsChecked = true; // Checked rebuilds the Running body
        else Render();
        LoadInstalled(); // quiet re-scan: swaps the list only when it differs
    }

    // ---- shell: header, tabs, body, footer ----------------------------------------------------------------

    private UIElement BuildShell()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // header: back, title, close
        var header = new Grid { Margin = new Thickness(10, 12, 14, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = IconButton("M15,6 L9,12 L15,18", "Brush.TextSoft");
        back.Click += (_, _) => _close();
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
        title.SetResourceReference(TextBlock.TextProperty, "Str.AddAppTitle");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        var close = IconButton("M6,6 L18,18 M18,6 L6,18", "Brush.TextMuted");
        close.Click += (_, _) => _close();
        Grid.SetColumn(title, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(back);
        header.Children.Add(title);
        header.Children.Add(close);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // tabs
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 8, 20, 0) };
        var strip = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tabs };
        strip.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        foreach (var (key, tag) in new[] { (AddAppStrings.TabRunning, "Running"), (AddAppStrings.TabInstalled, "Installed"), (AddAppStrings.TabTracked, "Tracked"), (AddAppStrings.TabBrowse, "Browse") })
        {
            var rb = new RadioButton { Tag = tag, IsChecked = tag == "Running", Margin = new Thickness(0, 0, 18, 0), Style = (Style)FindResource("TabButton") };
            rb.SetResourceReference(ContentControl.ContentProperty, "Str." + char.ToUpperInvariant(key[0]) + key[1..]);
            rb.Checked += (_, _) => ShowTab(tag);
            _tabButtons[tag] = rb;
            tabs.Children.Add(rb);
        }
        Grid.SetRow(strip, 1);
        root.Children.Add(strip);

        _body = new ContentControl();
        Grid.SetRow(_body, 2);
        root.Children.Add(_body);
        _body.Content = BuildRunningTab();

        // footer: "N added this session"
        var footer = new Grid { Margin = new Thickness(14, 8, 14, 10) };
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _count.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        _count.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        footer.Children.Add(_count);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        UpdateCount();
        return root;
    }

    private void ShowTab(string tag)
    {
        _tab = tag;
        switch (tag)
        {
            case "Running": _body.Content = BuildRunningTab(); Render(); Refresh(); break;
            case "Installed": _body.Content = BuildInstalledTab(); break;
            case "Tracked": _mergePlan = null; _mergeOpening = null; _openId = null; _mode = ""; ShowTracked(); break;
            default: _body.Content = BuildBrowseTab(); break;
        }
    }

    // built once: _list is a single shared panel, so a fresh host per visit would add it to a second parent and throw
    private UIElement? _runningTab;

    private UIElement BuildRunningTab()
    {
        if (_runningTab is not null) return _runningTab;
        var host = new StackPanel { Margin = new Thickness(12, 12, 12, 0) };
        host.Children.Add(_list);
        return _runningTab = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = host };
    }

    // ---- data ---------------------------------------------------------------------------------------------

    private void Refresh()
    {
        if (_loading || _tab != "Running") return;
        _loading = true;
        Task.Run(() =>
        {
            try { return _catalogue.Running(TileIconPx); }
            catch { return null; } // window capture failed: keep what is on screen
        }).ContinueWith(t =>
        {
            _loading = false;
            if (t.Result is not { Outcome: CatalogueOutcome.Done } running) return;
            // rows tracked in this visit stay on screen as "Tracking ✓": the catalogue keeps them in the list
            var merged = running.Candidates
                .Select(c => new Row { App = c.App, Aumid = c.Aumid, Png = c.Png, Done = c.Tracking }).ToList();
            if (SameRows(_rows, merged))
            {
                // nothing opened or closed: keep the rows (and their decoded icons) and only move the "open for" clocks
                var tickNow = DateTimeOffset.UtcNow;
                foreach (var row in _rows) if (row.Open is not null) row.Open.Text = OpenForText(row, tickNow);
                return;
            }
            _rows = merged;
            Render();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static bool SameRows(List<Row> old, List<Row> now)
    {
        if (old.Count != now.Count) return false;
        for (var i = 0; i < old.Count; i++)
            if (!string.Equals(old[i].App.ExePath, now[i].App.ExePath, StringComparison.OrdinalIgnoreCase) || old[i].Done != now[i].Done)
                return false;
        return true;
    }

    private static string OpenForText(Row row, DateTimeOffset now) =>
        TimeText.Str(AddAppStrings.OpenFor, DurationFormat.HoursMinutes((long)(now - row.App.StartUtc).TotalSeconds));

    private void Track(Row row)
    {
        if (row.Done) return;
        if (_catalogue.Track(Candidate(row)) == CatalogueOutcome.Unavailable) return; // the button stays "Track" so the user can retry
        row.Done = true;
        StyleButton(row);
        UpdateCount();
    }

    /// <summary>"Not this one": the app joins the ignore list and leaves the list.</summary>
    private void Dismiss(Row row)
    {
        if (row.Done) return;
        if (_catalogue.Dismiss(Candidate(row)) == CatalogueOutcome.Unavailable) return;
        _rows.Remove(row);
        Render();
    }

    private static RunningCandidate Candidate(Row row) => new(row.App, row.Aumid, row.Png, row.Done);

    // ---- rendering ----------------------------------------------------------------------------------------

    private void Redraw() { UpdateCount(); Render(); }

    private void UpdateCount() => _count.Text = TimeText.Str(AddAppStrings.AddedCount, _catalogue.AddedCount);

    private void Render()
    {
        _list.Children.Clear();
        var header = Text("Str.AddRunningHeader", "Double.FontSize.Small", "Brush.TextMuted");
        header.Margin = new Thickness(8, 0, 0, 8);
        _list.Children.Add(header);

        if (_rows.Count == 0)
        {
            var empty = Text("Str.AddRunningEmpty", "Double.FontSize.Body", "Brush.TextMuted");
            empty.Margin = new Thickness(8, 8, 8, 8);
            _list.Children.Add(empty);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _rows) _list.Children.Add(Cached(BuildRow(row, now)));
        _list.Children.Add(BuildHint());
    }

    private UIElement BuildRow(Row row, DateTimeOffset now)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.Margin = new Thickness(0, 0, 0, 2);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = IconTile(row);
        tile.SetResourceReference(FrameworkElement.MarginProperty, "Thickness.RowTile");
        row.Tile = tile;

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = row.App.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var open = new TextBlock { Text = OpenForText(row, now) };
        row.Open = open;
        open.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        open.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        open.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        var sub = new StackPanel { Orientation = Orientation.Horizontal };
        sub.Children.Add(open);
        var notThis = Text("Str.AddNotThisOne", "Double.FontSize.Small", "Brush.TextMuted");
        notThis.Margin = new Thickness(10, 0, 0, 0);
        notThis.TextDecorations = TextDecorations.Underline;
        notThis.Cursor = System.Windows.Input.Cursors.Hand;
        notThis.MouseLeftButtonUp += (_, _) => Dismiss(row);
        sub.Children.Add(notThis);
        texts.Children.Add(name);
        texts.Children.Add(sub);

        var button = new Button { Template = Flat, VerticalAlignment = VerticalAlignment.Center, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, FontWeight = FontWeights.Bold };
        button.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.Button");
        button.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        button.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        button.Click += (_, _) => Track(row);
        row.Button = button;
        StyleButton(row);

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(button, 2);
        g.Children.Add(tile);
        g.Children.Add(texts);
        g.Children.Add(button);
        return g;
    }

    private static void StyleButton(Row row)
    {
        var b = row.Button;
        b.SetResourceReference(ContentControl.ContentProperty, row.Done ? "Str.AddTracking" : "Str.AddTrack");
        b.SetResourceReference(Control.BackgroundProperty, row.Done ? "Brush.Surface" : "Brush.Accent");
        b.SetResourceReference(Control.ForegroundProperty, row.Done ? "Brush.Accent" : "Brush.OnAccent");
        b.SetResourceReference(Control.BorderBrushProperty, row.Done ? "Brush.Border" : "Brush.Accent");
        b.IsHitTestVisible = !row.Done;
    }

    /// <summary>Extraction size for tab tiles: well above the on-screen tile so logos stay sharp on high-DPI displays.</summary>
    private const int TileIconPx = 256;

    private Border IconTile(Row row) => MakeTile(row.App.DisplayName, row.App.ExePath, row.Png);

    /// <summary>Cached/extracted icon when there is one, else a letter tile in a palette colour.</summary>
    private Border MakeTile(string name, string exePath, byte[]? png)
    {
        var tile = new Border { BorderThickness = new Thickness(1) };
        tile.SetResourceReference(FrameworkElement.WidthProperty, "Double.Row.TileLarge");
        tile.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.TileLarge");
        tile.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        FillTile(tile, name, exePath, png);
        return tile;
    }

    /// <summary>
    /// Scrolling a long list moves already-drawn rows, so each row is kept as a GPU bitmap (at the screen's pixel density,
    /// so text stays sharp) instead of being re-rasterised every frame.
    /// </summary>
    private T Cached<T>(T row) where T : UIElement
    {
        row.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).DpiScaleX) { SnapsToDevicePixels = true };
        return row;
    }

    private void FillTile(Border tile, string name, string exePath, byte[]? png)
    {
        var bmp = IconService.Decode(png);
        if (bmp is not null)
        {
            // a real logo sits bare on the panel: no box, no outline, and it fills the whole tile
            tile.ClearValue(Border.BackgroundProperty);
            tile.BorderThickness = new Thickness(0);
            tile.Padding = new Thickness(0);
            var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
            tile.Child = img;
            return;
        }
        tile.BorderThickness = new Thickness(1); // letter fallback keeps its box
        tile.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        var letter = new TextBlock
        {
            Text = LetterTile.Letter(name), FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var slot = LetterTile.PaletteIndex(LetterTile.KeyFor(exePath), Math.Max(1, _themes.Current.Apps.Count));
        letter.SetResourceReference(TextBlock.ForegroundProperty, "Brush.App" + slot);
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        letter.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Title");
        tile.Child = letter;
    }

    private UIElement BuildHint()
    {
        var wrap = new Grid { Margin = new Thickness(8, 12, 8, 12) };
        var dashed = new Rectangle { StrokeDashArray = [4, 3], StrokeThickness = 1, Fill = Brushes.Transparent };
        dashed.SetResourceReference(Rectangle.RadiusXProperty, "Double.Radius.Medium");
        dashed.SetResourceReference(Rectangle.RadiusYProperty, "Double.Radius.Medium");
        dashed.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
        var line = new TextBlock { Margin = new Thickness(12, 10, 12, 10), TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        line.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        line.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        line.Inlines.Add(new Run(TimeText.Str(AddAppStrings.HintBefore)));
        var link = new Run(TimeText.Str(AddAppStrings.HintLink)) { FontWeight = FontWeights.Bold, TextDecorations = TextDecorations.Underline };
        link.SetResourceReference(TextElement.ForegroundProperty, "Brush.Accent");
        link.Cursor = System.Windows.Input.Cursors.Hand;
        link.MouseLeftButtonUp += (_, _) => _tabButtons["Installed"].IsChecked = true;
        line.Inlines.Add(link);
        line.Inlines.Add(new Run(TimeText.Str(AddAppStrings.HintAfter)));
        wrap.Children.Add(dashed);
        wrap.Children.Add(line);
        return wrap;
    }

    private TextBlock Text(string? strKey, string sizeKey, string brushKey)
    {
        var t = new TextBlock();
        if (strKey is not null) t.SetResourceReference(TextBlock.TextProperty, strKey);
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return t;
    }

    /// <summary>A flat glyph button. A <paramref name="subtle"/> one is small and dimmed until hovered (the close X, the unmerge X).</summary>
    private Button IconButton(string pathData, string brushKey, bool subtle = false)
    {
        var size = subtle ? 11 : 18;
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData), StrokeThickness = subtle ? 1.8 : 2.2, Width = size, Height = size, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        var b = new Button { Template = Flat, Content = path, Width = 34, Height = 34, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        if (subtle)
        {
            path.Opacity = 0.55;
            b.MouseEnter += (_, _) => path.Opacity = 1;
            b.MouseLeave += (_, _) => path.Opacity = 0.55;
        }
        return b;
    }
}
