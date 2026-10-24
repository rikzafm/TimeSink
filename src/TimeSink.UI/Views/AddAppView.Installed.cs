using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TimeSink.Core.Apps;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>Installed tab: A-Z list of Start Menu and packaged apps with search, letter rail, and stop/restore.</summary>
public sealed partial class AddAppView
{
    private IReadOnlyList<InstalledApp>? _installed;
    private bool _installedLoading;
    private string _query = "";
    private ScrollViewer _instScroll = null!;
    private StackPanel _instList = null!;
    private StackPanel _rail = null!;
    private readonly Dictionary<string, UIElement> _groupHeaders = new();
    private readonly Dictionary<string, List<(Border Tile, string Name)>> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LibraryApp> _library = [];

    private UIElement BuildInstalledTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // search box
        var box = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, Text = _query };
        System.Windows.Automation.AutomationProperties.SetName(box, TimeText.Str(AddAppStrings.Search));
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        box.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        var hint = Text("Str.AddSearch", "Double.FontSize.Body", "Brush.TextMuted");
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            _query = box.Text;
            hint.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderInstalled();
        };
        var magnifier = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M11,4 A7,7 0 1 0 11,18 A7,7 0 1 0 11,4 M20,20 L16.5,16.5"), StrokeThickness = 2, Width = 16, Height = 16,
            Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        magnifier.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        magnifier.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(hint, 1);
        inner.Children.Add(magnifier);
        inner.Children.Add(box);
        inner.Children.Add(hint);
        var search = new Border { Margin = new Thickness(16, 12, 16, 4), Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Child = inner };
        search.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        search.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        search.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        root.Children.Add(search);

        // list + rail
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _instList = new StackPanel { Margin = new Thickness(12, 4, 4, 8) };
        _instScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _instList };
        _rail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(_rail, 1);
        body.Children.Add(_instScroll);
        body.Children.Add(_rail);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        if (_installed is null) LoadInstalled();
        RenderInstalled();
        return root;
    }

    private void LoadInstalled()
    {
        if (_installedLoading) return;
        _installedLoading = true;
        Task.Run(() =>
        {
            try { return InstalledAppsService.Load(); } catch { return (IReadOnlyList<InstalledApp>)[]; }
        }).ContinueWith(t =>
        {
            _installedLoading = false;
            _installed = t.Result;
            if (_tab == "Installed") RenderInstalled();
            LoadInstalledIcons(t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Extracts icons one by one on a pool thread; tiles already on screen swap their letter for the icon.</summary>
    private void LoadInstalledIcons(IReadOnlyList<InstalledApp> apps)
    {
        var todo = apps.Where(a => !_iconCache.ContainsKey(a.ExePath)).ToList();
        if (todo.Count == 0) return;
        Task.Run(() =>
        {
            foreach (var a in todo)
            {
                byte[]? png;
                try { png = IconService.ExtractPng(a.ExePath, a.Aumid); } catch { png = null; }
                Dispatcher.BeginInvoke(() =>
                {
                    _iconCache[a.ExePath] = png;
                    if (png is null || !_tiles.TryGetValue(a.ExePath, out var tiles)) return;
                    foreach (var (tile, name) in tiles) FillTile(tile, name, a.ExePath, png);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        });
    }

    private void RenderInstalled()
    {
        if (_instList is null) return;
        var offset = _instScroll.VerticalOffset;
        _instList.Children.Clear();
        _rail.Children.Clear();
        _groupHeaders.Clear();
        _tiles.Clear();
        try { using var lib = AppLibrary.Open(_dbPath); _library = lib.Library(); } catch { _library = []; }

        if (_installed is null)
        {
            _instList.Children.Add(Note("Str.AddInstalledLoading"));
            return;
        }
        var shown = InstalledCatalog.Search(_installed, _query);
        if (_installed.Count == 0) _instList.Children.Add(Note("Str.AddInstalledEmpty"));
        else if (shown.Count == 0)
        {
            var none = Note(null);
            none.Text = TimeText.Str(AddAppStrings.NoMatch, _query.Trim());
            _instList.Children.Add(none);
        }

        var groups = InstalledCatalog.Group(shown);
        foreach (var g in groups)
        {
            var head = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
            head.Text = g.Letter;
            head.FontWeight = FontWeights.Bold;
            head.Margin = new Thickness(8, 8, 0, 4);
            _groupHeaders[g.Letter] = head;
            _instList.Children.Add(head);
            foreach (var app in g.Apps) _instList.Children.Add(InstalledRow(app));
        }

        foreach (var letter in InstalledCatalog.Rail(groups))
        {
            var l = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
            l.Text = letter;
            l.FontWeight = FontWeights.SemiBold;
            l.Padding = new Thickness(4, 1, 4, 1);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Cursor = System.Windows.Input.Cursors.Hand;
            var target = letter;
            l.MouseLeftButtonUp += (_, _) => ScrollTo(target);
            _rail.Children.Add(l);
        }

        // apps stopped earlier (history kept): restorable from here, whether or not they are installed
        var retired = _library.Where(a => a.State == "retired").OrderByDescending(a => a.RetiredUtc).ToList();
        if (retired.Count > 0 && _query.Trim().Length == 0)
        {
            var head = Text("Str.AddRetiredHeader", "Double.FontSize.Small", "Brush.TextMuted");
            head.Margin = new Thickness(8, 16, 0, 4);
            _instList.Children.Add(head);
            foreach (var r in retired)
                _instList.Children.Add(AppRow(r.DisplayName, r.ExePath, null, StateOf(r.ExePath, r.PackageFamily)));
        }

        _instScroll.ScrollToVerticalOffset(offset);
    }

    private void ScrollTo(string letter)
    {
        if (!_groupHeaders.TryGetValue(letter, out var head)) return;
        var y = head.TransformToAncestor(_instList).Transform(new Point(0, 0)).Y;
        _instScroll.ScrollToVerticalOffset(Math.Max(0, y - 4));
    }

    private TextBlock Note(string? strKey)
    {
        var t = Text(strKey, "Double.FontSize.Body", "Brush.TextMuted");
        t.Margin = new Thickness(8, 16, 8, 8);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    // ---- rows ---------------------------------------------------------------------------------------------

    private LibraryApp? StateOf(string exePath, string? family) =>
        _library.FirstOrDefault(a => string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase))
        ?? (family is null ? null : _library.FirstOrDefault(a => a.State == "tracked" && string.Equals(a.PackageFamily, family, StringComparison.OrdinalIgnoreCase)));

    private UIElement InstalledRow(InstalledApp app) => AppRow(app.Name, app.ExePath, app, StateOf(app.ExePath, app.PackageFamily));

    private UIElement AppRow(string name, string exePath, InstalledApp? app, LibraryApp? lib)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = MakeTile(name, exePath, _iconCache.GetValueOrDefault(exePath));
        tile.Margin = new Thickness(8, 6, 12, 6);
        if (!_tiles.TryGetValue(exePath, out var list)) _tiles[exePath] = list = [];
        list.Add((tile, name));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var n = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        n.SetResourceReference(TextBlock.ForegroundProperty, lib is { State: "retired" } ? "Brush.TextMuted" : "Brush.Text");
        n.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        n.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        texts.Children.Add(n);
        if (app?.Publisher is { Length: > 0 } pub)
        {
            var p = new TextBlock { Text = pub, TextTrimming = TextTrimming.CharacterEllipsis };
            p.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            p.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            p.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            texts.Children.Add(p);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        if (lib is { State: "tracked" })
        {
            var stop = Pill("Str.AddStopTracking", PillStyle.Quiet);
            stop.ToolTip = TimeText.Str(AddAppStrings.StopTip);
            stop.Margin = new Thickness(0, 0, 6, 0);
            stop.Click += (_, _) => Retire(lib);
            actions.Children.Add(stop);
            var on = Pill("Str.AddTracking", PillStyle.Done);
            on.IsHitTestVisible = false;
            actions.Children.Add(on);
        }
        else if (lib is { State: "retired" })
        {
            var restore = Pill("Str.AddRestore", PillStyle.Filled);
            restore.Click += (_, _) => Restore(lib);
            actions.Children.Add(restore);
        }
        else if (app is not null)
        {
            var track = Pill("Str.AddTrack", PillStyle.Filled);
            track.Click += (_, _) => TrackInstalled(app);
            actions.Children.Add(track);
        }

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(actions, 2);
        g.Children.Add(tile);
        g.Children.Add(texts);
        g.Children.Add(actions);
        return g;
    }

    private enum PillStyle { Filled, Done, Quiet }

    private Button Pill(string strKey, PillStyle style)
    {
        var b = new Button { Template = Flat, Height = 32, MinWidth = 64, Padding = new Thickness(12, 6, 12, 6), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, FontWeight = FontWeights.Bold };
        b.SetResourceReference(ContentControl.ContentProperty, strKey);
        b.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        b.SetResourceReference(Control.BackgroundProperty, style == PillStyle.Filled ? "Brush.Accent" : "Brush.Surface");
        b.SetResourceReference(Control.ForegroundProperty, style switch { PillStyle.Filled => "Brush.OnAccent", PillStyle.Done => "Brush.Accent", _ => "Brush.TextMuted" });
        b.SetResourceReference(Control.BorderBrushProperty, style == PillStyle.Filled ? "Brush.Accent" : "Brush.Border");
        if (style == PillStyle.Quiet) b.FontWeight = FontWeights.Normal;
        return b;
    }

    // ---- actions ------------------------------------------------------------------------------------------

    private void TrackInstalled(InstalledApp app)
    {
        var cached = _iconCache.GetValueOrDefault(app.ExePath);
        Task.Run(() => cached ?? IconService.ExtractPng(app.ExePath, app.Aumid)).ContinueWith(t =>
        {
            try
            {
                using var lib = AppLibrary.Open(_dbPath);
                lib.AddTracked(app.ToNewApp(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t.Result);
            }
            catch { return; }
            _iconCache[app.ExePath] = t.Result;
            _addedElsewhere.Add(app.ExePath);
            UpdateCount();
            RenderInstalled();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Retire(LibraryApp app)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.Retire(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch { return; }
        _addedElsewhere.Remove(app.ExePath);
        _session.Remove(app.ExePath);
        UpdateCount();
        RenderInstalled();
    }

    private void Restore(LibraryApp app)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.Restore(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch { return; }
        _addedElsewhere.Add(app.ExePath);
        UpdateCount();
        RenderInstalled();
    }
}
