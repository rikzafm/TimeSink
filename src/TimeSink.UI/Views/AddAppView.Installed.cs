using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private IReadOnlyList<MergedAlias> _aliases = [];

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
        // Typing waits a beat before the list is rebuilt (every row is rebuilt, so per-key rebuilds queue up).
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += (_, _) => { debounce.Stop(); RenderInstalled(reload: false); };
        box.TextChanged += (_, _) =>
        {
            _query = box.Text;
            hint.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            debounce.Stop();
            debounce.Start();
        };
        box.Unloaded += (_, _) => debounce.Stop();
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
            // a re-scan on reopen: an empty result (scan failed) or an identical one leaves the cached list and its rows alone
            if (_installed is not null && (t.Result.Count == 0 || _installed.SequenceEqual(t.Result))) return;
            _installed = t.Result;
            if (_tab == "Installed") RenderInstalled();
            else if (_tab == "Tracked" && _mergeSource is not null) ShowTracked(); // merge picker was waiting for the list
            else if (_tab == "Tracked") RenderTracked(); // rows were waiting for publishers
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
                try { png = IconService.ExtractPng(a.ExePath, a.Aumid, a.IconFile, TileIconPx); } catch { png = null; }
                Dispatcher.BeginInvoke(() =>
                {
                    _iconCache[a.ExePath] = png;
                    if (png is null || !_tiles.TryGetValue(a.ExePath, out var tiles)) return;
                    foreach (var (tile, name) in tiles) FillTile(tile, name, a.ExePath, png);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        });
    }

    private bool _libraryLoaded;
    private HashSet<long> _trivialRetired = [];

    private void RenderInstalled() => RenderInstalled(reload: true);

    /// <param name="reload">Re-read the library from the database; a search keystroke reuses what the last render read.</param>
    private void RenderInstalled(bool reload)
    {
        if (_instList is null) return;
        var offset = _instScroll.VerticalOffset;
        _instList.Children.Clear();
        _rail.Children.Clear();
        _groupHeaders.Clear();
        _tiles.Clear();
        if (reload || !_libraryLoaded)
        {
            try
            {
                using var lib = AppLibrary.Open(_dbPath);
                _library = lib.Library();
                _aliases = lib.MergedAliases();
                _trivialRetired = lib.TrivialRetired();
            }
            catch { _library = []; _aliases = []; _trivialRetired = []; }
            _libraryLoaded = true;
        }

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

        var ranked = _query.Trim().Length > 0; // a search lists best match first, without A-Z groups
        var groups = ranked ? [] : InstalledCatalog.Group(shown);
        if (ranked) foreach (var app in shown) _instList.Children.Add(Cached(InstalledRow(app)));
        foreach (var g in groups)
        {
            var head = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
            head.Text = g.Letter;
            head.FontWeight = FontWeights.Bold;
            head.Margin = new Thickness(8, 8, 0, 4);
            _groupHeaders[g.Letter] = head;
            _instList.Children.Add(head);
            foreach (var app in g.Apps) _instList.Children.Add(Cached(InstalledRow(app)));
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
        var retired = _library.Where(a => a.State == "retired" && !_trivialRetired.Contains(a.Id)).OrderByDescending(a => a.RetiredUtc).ToList();
        if (retired.Count > 0 && _query.Trim().Length == 0)
        {
            var head = Text("Str.AddRetiredHeader", "Double.FontSize.Small", "Brush.TextMuted");
            head.Margin = new Thickness(8, 16, 0, 4);
            _instList.Children.Add(head);
            foreach (var r in retired)
                _instList.Children.Add(Cached(AppRow(r.DisplayName, r.ExePath, null, StateOf(r.ExePath, r.PackageFamily))));
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

    // a merged app is the app it counts under, so it reads as tracked (or stopped) with that app
    private LibraryApp? StateOf(string exePath, string? family) =>
        _library.FirstOrDefault(a => string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase))
        ?? KeeperOf(_aliases.FirstOrDefault(m => string.Equals(m.ExePath, exePath, StringComparison.OrdinalIgnoreCase)))
        ?? (family is null ? null : _library.FirstOrDefault(a => a.State == "tracked" && string.Equals(a.PackageFamily, family, StringComparison.OrdinalIgnoreCase)))
        ?? (family is null ? null : KeeperOf(_aliases.FirstOrDefault(m => string.Equals(m.PackageFamily, family, StringComparison.OrdinalIgnoreCase))));

    private LibraryApp? KeeperOf(MergedAlias? alias) => alias is null ? null : _library.FirstOrDefault(a => a.Id == alias.CanonicalId);

    private UIElement InstalledRow(InstalledApp app) => AppRow(app.Name, app.ExePath, app, StateOf(app.ExePath, app.PackageFamily));

    private UIElement AppRow(string name, string exePath, InstalledApp? app, LibraryApp? lib)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = MakeTile(name, exePath, _iconCache.GetValueOrDefault(exePath));
        tile.SetResourceReference(FrameworkElement.MarginProperty, "Thickness.RowTileTight");
        if (!_tiles.TryGetValue(exePath, out var list)) _tiles[exePath] = list = [];
        list.Add((tile, name));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var n = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        n.SetResourceReference(TextBlock.ForegroundProperty, lib is { State: "retired" } ? "Brush.TextMuted" : "Brush.Text");
        n.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        n.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        texts.Children.Add(n);
        var sub = app is { IsSteam: true } ? InstalledApp.SteamSource : app?.Publisher;
        if (sub is { Length: > 0 } pub)
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
            actions.Children.Add(KindSwitch(lib.Kind, () => SetKind(lib.Id, AppKinds.Next(lib.Kind), RenderInstalled)));
            actions.Children.Add(BackgroundSwitch(lib.Background, () => SetBackground(lib.Id, !lib.Background, RenderInstalled)));
            var stop = ActionIcon(IconEyeOpen, PillStyle.Done, AddAppStrings.StopTip, AddAppStrings.StopTracking); // open eye = tracked; click closes it
            stop.Click += (_, _) => Retire(lib);
            actions.Children.Add(stop);
        }
        else if (lib is { State: "retired" })
        {
            var restore = ActionIcon(IconEyeClosed, PillStyle.Filled, AddAppStrings.Restore, AddAppStrings.Restore);
            restore.Click += (_, _) => Restore(lib);
            actions.Children.Add(restore);
        }
        else if (app is not null)
        {
            var track = ActionIcon(IconEyeClosed, PillStyle.Filled, AddAppStrings.Track, AddAppStrings.Track);
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
        var b = new Button { Template = Flat, MinWidth = 64, Padding = new Thickness(12, 6, 12, 6), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, FontWeight = FontWeights.Bold };
        b.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.Button");
        b.SetResourceReference(ContentControl.ContentProperty, strKey);
        b.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        b.SetResourceReference(Control.BackgroundProperty, style == PillStyle.Filled ? "Brush.Accent" : "Brush.Surface");
        b.SetResourceReference(Control.ForegroundProperty, style switch { PillStyle.Filled => "Brush.OnAccent", PillStyle.Done => "Brush.Accent", _ => "Brush.TextMuted" });
        b.SetResourceReference(Control.BorderBrushProperty, style == PillStyle.Filled ? "Brush.Accent" : "Brush.Border");
        if (style == PillStyle.Quiet) b.FontWeight = FontWeights.Normal;
        return b;
    }

    // 24 x 24 icons for the row buttons: all stroked with round caps
    private const string IconEyeOpen = "M2.5,12 C5,7.5 8.5,5.5 12,5.5 C15.5,5.5 19,7.5 21.5,12 C19,16.5 15.5,18.5 12,18.5 C8.5,18.5 5,16.5 2.5,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z";
    private const string IconEyeClosed = "M3,10 C5.5,14 9,16 12,16 C15,16 18.5,14 21,10 M6,14 L4.2,16.4 M12,16.5 L12,19.5 M18,14 L19.8,16.4";
    private const string IconLayers = "M12,4 L20,8.5 L12,13 L4,8.5 Z M4,12.5 L12,17 L20,12.5 M4,16 L12,20.5 L20,16";
    private const string IconSliders = "M4,8 L20,8 M4,16 L20,16 M9,5 L9,11 M15,13 L15,19";

    /// <summary>
    /// An icon-only row button: <paramref name="tipKey"/> is its tooltip and <paramref name="nameKey"/> its accessible
    /// name. <paramref name="lit"/> marks a switched-on state with the accent colour.
    /// </summary>
    private Button ActionIcon(string pathData, PillStyle style, string tipKey, string nameKey, bool lit = false)
    {
        var ink = lit ? "Brush.Accent" : style switch { PillStyle.Filled => "Brush.OnAccent", PillStyle.Done => "Brush.Accent", _ => "Brush.TextMuted" };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData), StrokeThickness = style == PillStyle.Filled ? 2.4 : 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, ink);
        // drawn on a 24 x 24 canvas and scaled to 18 px, so each icon keeps its own size inside the button
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        var b = new Button { Template = Flat, Content = new Viewbox { Width = 18, Height = 18, Child = canvas }, Width = 38, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false };
        b.SetResourceReference(FrameworkElement.HeightProperty, "Double.Row.Button");
        b.SetResourceReference(Control.BackgroundProperty, style == PillStyle.Filled ? "Brush.Accent" : "Brush.Surface");
        b.SetResourceReference(Control.BorderBrushProperty, style == PillStyle.Filled || lit ? "Brush.Accent" : "Brush.Border");
        b.ToolTip = TimeText.Str(tipKey);
        System.Windows.Automation.AutomationProperties.SetName(b, TimeText.Str(nameKey));
        return b;
    }

    /// <summary>The per-row "Background" switch on tracked apps (Installed and Tracked tabs): lit when the app ranks after the others.</summary>
    private Button BackgroundSwitch(bool on, Action toggle)
    {
        var b = ActionIcon(IconLayers, on ? PillStyle.Done : PillStyle.Quiet, AddAppStrings.BackgroundTip, AddAppStrings.Background, lit: on);
        b.Margin = new Thickness(0, 0, 6, 0);
        b.Click += (_, _) => toggle();
        return b;
    }

    private const string IconGamepad = "M7,8 L17,8 C20,8 21.5,11 21,15 C20.7,17.5 18.5,18 17.5,16.5 L16,15 L8,15 L6.5,16.5 C5.5,18 3.3,17.5 3,15 C2.5,11 4,8 7,8 Z M8,10.5 L8,13 M6.8,11.7 L9.2,11.7 M16.5,11 L16.6,11 M18,12.5 L18.1,12.5";
    private const string IconApps = "M5,5 L10,5 L10,10 L5,10 Z M14,5 L19,5 L19,10 L14,10 Z M5,14 L10,14 L10,19 L5,19 Z M14,14 L19,14 L19,19 L14,19 Z";
    private const string IconSparkle = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z";

    /// <summary>The per-row game-type button: cycles Other, Game, Gacha (what Year > Wrapped counts the app as); lit unless Other.</summary>
    private Button KindSwitch(AppKind kind, Action toggle)
    {
        var name = TimeText.Str(kind switch { AppKind.Game => AddAppStrings.KindGame, AppKind.Gacha => AddAppStrings.KindGacha, _ => AddAppStrings.KindOther });
        var on = kind != AppKind.Other;
        var b = ActionIcon(kind == AppKind.Gacha ? IconSparkle : IconGamepad, on ? PillStyle.Done : PillStyle.Quiet, AddAppStrings.Kind, AddAppStrings.Kind, lit: on);
        b.ToolTip = TimeText.Str(AddAppStrings.KindTip, name);
        b.Margin = new Thickness(0, 0, 6, 0);
        b.Click += (_, _) => toggle();
        return b;
    }

    private void SetKind(long appId, AppKind kind, Action rerender)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.SetKind(appId, kind); }
        catch { return; }
        rerender();
    }

    private void SetBackground(long appId, bool background, Action rerender)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.SetBackground(appId, background); }
        catch { return; }
        rerender();
    }

    // ---- actions ------------------------------------------------------------------------------------------

    private void TrackInstalled(InstalledApp app)
    {
        var cached = _iconCache.GetValueOrDefault(app.ExePath);
        Task.Run(() => cached ?? IconService.ExtractPng(app.ExePath, app.Aumid, app.IconFile, TileIconPx)).ContinueWith(t =>
        {
            try
            {
                using var lib = AppLibrary.Open(_dbPath);
                lib.AddTracked(app.ToNewApp(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t.Result);
            }
            catch { return; }
            _iconCache[app.ExePath] = t.Result;
            _catalogue.NoteAddedElsewhere(app.ExePath);
            UpdateCount();
            RenderInstalled();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Retire(LibraryApp app)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.Retire(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch { return; }
        _catalogue.NoteStopped(app.ExePath);
        UpdateCount();
        RenderInstalled();
    }

    private void Restore(LibraryApp app)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.Restore(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch { return; }
        _catalogue.NoteAddedElsewhere(app.ExePath);
        UpdateCount();
        RenderInstalled();
    }
}
