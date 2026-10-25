using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>
/// Tracked tab: every tracked app with its own logo, and per app Rename, Merge into another app, and Stop tracking.
/// Merged apps are listed as chips under the app that keeps the name (Unmerge on each chip).
/// </summary>
public sealed partial class AddAppView
{
    private enum BtnKind { Primary, Outline, Danger, DangerFill, Ghost }

    private IReadOnlyList<TrackedEntry> _tracked = [];
    private readonly Dictionary<long, byte[]?> _trackedIcons = new();
    private string _trackedQuery = "";
    private StackPanel _trackedList = null!;
    private ScrollViewer _trackedScroll = null!;
    private TextBlock _trackedHeader = null!;
    private long? _openId;
    private string _mode = ""; // "", "rename", "stop"
    private string _draft = "";
    private long? _mergeSource;
    private readonly HashSet<string> _mergePicked = new(); // keys of the ticked MergeCandidates
    private List<(Border Tile, string Name, string ExePath)> _mergeTiles = [];
    private double _listOffset;            // Tracked list scroll position when Merge… was opened
    private bool _restoreOffset;
    private string _mergeFilter = "Tracked";  // Tracked | Untracked | All

    /// <summary>One app the merge picker can offer: tracked, stopped (history kept) or merely installed.</summary>
    private sealed record MergeCandidate(string Key, string Name, string ExePath, string Sub, byte[]? Png, bool Tracked,
        TrackedEntry? Entry, LibraryApp? Stopped, InstalledApp? Installed, MergedApp? Merged = null);

    private void ShowTracked() => _body.Content = _mergeSource is null ? BuildTrackedTab() : BuildMergeScreen();

    private UIElement BuildTrackedTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = SearchBox("Str.AddTrackedSearch", AddAppStrings.TrackedSearch, _trackedQuery, text => { _trackedQuery = text; RenderTracked(); });
        root.Children.Add(search);

        _trackedHeader = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        _trackedHeader.Margin = new Thickness(8, 8, 0, 8);
        _trackedList = new StackPanel { Margin = new Thickness(12, 4, 12, 8) };
        _trackedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _trackedList };
        Grid.SetRow(_trackedScroll, 1);
        root.Children.Add(_trackedScroll);
        RenderTracked();
        if (_restoreOffset) // back from the merge screen: land where the list was
        {
            _restoreOffset = false;
            var offset = _listOffset;
            _trackedScroll.Loaded += (_, _) => Dispatcher.BeginInvoke(() => _trackedScroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        return root;
    }

    private Border SearchBox(string hintKey, string automationKey, string initial, Action<string> changed)
    {
        var box = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, Text = initial };
        System.Windows.Automation.AutomationProperties.SetName(box, TimeText.Str(automationKey));
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        box.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        var hint = Text(hintKey, "Double.FontSize.Body", "Brush.TextMuted");
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Visibility = initial.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            changed(box.Text);
        };
        var magnifier = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M11,4 A7,7 0 1 0 11,18 A7,7 0 1 0 11,4 M20,20 L16.5,16.5"), StrokeThickness = 2, Width = 16, Height = 16,
            Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        magnifier.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(box, 1);
        Grid.SetColumn(hint, 1);
        inner.Children.Add(magnifier);
        inner.Children.Add(box);
        inner.Children.Add(hint);
        var border = new Border { Margin = new Thickness(16, 12, 16, 4), Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Child = inner };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        border.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return border;
    }

    private void LoadTracked()
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            _tracked = lib.Tracked();
            foreach (var a in _tracked.Select(a => a.Id).Where(id => !_trackedIcons.ContainsKey(id)).ToList())
                _trackedIcons[a] = lib.GetIcon(a);
        }
        catch { /* database busy: keep what is on screen */ }
    }

    private void RenderTracked()
    {
        if (_trackedList is null) return;
        var offset = _trackedScroll.VerticalOffset;
        LoadTracked();
        _trackedList.Children.Clear();
        var q = _trackedQuery.Trim();
        var shown = _tracked.Where(a => q.Length == 0 || a.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.Merged.Any(m => m.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();

        _trackedHeader.Text = q.Length == 0 ? TimeText.Str(AddAppStrings.TrackedHeader).ToUpperInvariant() : TimeText.Str(AddAppStrings.TrackedCount, shown.Count);
        _trackedList.Children.Add(_trackedHeader);

        if (_tracked.Count == 0)
        {
            var empty = Text("Str.AddTrackedEmpty", "Double.FontSize.Body", "Brush.TextMuted");
            empty.Margin = new Thickness(8, 8, 8, 8);
            empty.TextWrapping = TextWrapping.Wrap;
            _trackedList.Children.Add(empty);
        }
        else if (shown.Count == 0)
        {
            var none = Note(null);
            none.Text = TimeText.Str(AddAppStrings.NoMatch, q);
            _trackedList.Children.Add(none);
        }
        foreach (var app in shown) _trackedList.Children.Add(TrackedRow(app));
        _trackedScroll.ScrollToVerticalOffset(offset);
    }

    private UIElement TrackedRow(TrackedEntry app)
    {
        var open = _openId == app.Id;
        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        if (open)
        {
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }
        else
        {
            card.Background = Brushes.Transparent;
            card.BorderBrush = Brushes.Transparent;
        }
        var stack = new StackPanel();
        card.Child = stack;

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = MakeTile(app.DisplayName, app.ExePath, _trackedIcons.GetValueOrDefault(app.Id)); // the app's own logo
        tile.Margin = new Thickness(0, 0, 12, 0);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = app.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        var sub = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        sub.Text = TimeText.Str(AddAppStrings.TrackedSub, DurationFormat.HoursMinutes(app.Seconds), TimeText.Local(app.AddedUtc).ToString("MMM yyyy"));
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(name);
        texts.Children.Add(sub);

        var manage = Button(AddAppStrings.Manage, open ? BtnKind.Primary : BtnKind.Outline, () => ToggleOpen(app));
        manage.Height = 30;
        manage.Padding = new Thickness(10, 0, 10, 0);
        if (open) { manage.Background = Brushes.Transparent; manage.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent"); manage.SetResourceReference(Control.ForegroundProperty, "Brush.Accent"); }
        else manage.SetResourceReference(Control.ForegroundProperty, "Brush.TextSoft");
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(manage, 2);
        top.Children.Add(tile);
        top.Children.Add(texts);
        top.Children.Add(manage);
        stack.Children.Add(top);

        if (app.Merged.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(52, 8, 0, 0) };
            foreach (var m in app.Merged) chips.Children.Add(Chip(m));
            stack.Children.Add(chips);
        }

        if (open && _mode == "") stack.Children.Add(ActionStrip(app));
        else if (open && _mode == "rename") stack.Children.Add(RenameEditor(app));
        else if (open && _mode == "stop") stack.Children.Add(StopConfirm(app));
        return card;
    }

    private UIElement Chip(MergedApp m)
    {
        var chip = new Border { Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(9, 2, 6, 2), BorderThickness = new Thickness(1) };
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        chip.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = Text(null, "Double.FontSize.Small", "Brush.TextSoft");
        label.Text = m.DisplayName;
        label.VerticalAlignment = VerticalAlignment.Center;
        var un = Text("Str.AddUnmerge", "Double.FontSize.Small", "Brush.TextMuted");
        un.Margin = new Thickness(8, 0, 0, 0);
        un.VerticalAlignment = VerticalAlignment.Center;
        un.TextDecorations = TextDecorations.Underline;
        un.Cursor = Cursors.Hand;
        un.MouseLeftButtonUp += (_, _) =>
        {
            try { using var lib = AppLibrary.Open(_dbPath); lib.Unmerge(m.Id); } catch { return; }
            RenderTracked();
        };
        row.Children.Add(label);
        row.Children.Add(un);
        chip.Child = row;
        return chip;
    }

    private void ToggleOpen(TrackedEntry app)
    {
        _openId = _openId == app.Id ? null : app.Id;
        _mode = "";
        _draft = app.DisplayName;
        RenderTracked();
    }

    private UIElement ActionStrip(TrackedEntry app)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 2) };
        for (var i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rename = Button(AddAppStrings.Rename, BtnKind.Outline, () => { _mode = "rename"; _draft = app.DisplayName; RenderTracked(); });
        var merge = Button(AddAppStrings.Merge, BtnKind.Outline, () => { _listOffset = _trackedScroll.VerticalOffset; _mergeSource = app.Id; _mergePicked.Clear(); foreach (var m in app.Merged) _mergePicked.Add("m" + m.Id); _mergeFilter = "Tracked"; ShowTracked(); });
        var stop = Button(AddAppStrings.Stop, BtnKind.Danger, () => { _mode = "stop"; RenderTracked(); });
        merge.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(merge, 1);
        Grid.SetColumn(stop, 2);
        g.Children.Add(rename);
        g.Children.Add(merge);
        g.Children.Add(stop);
        return g;
    }

    private UIElement RenameEditor(TrackedEntry app)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 2) };
        var label = Text("Str.AddRenameLabel", "Double.FontSize.Small", "Brush.TextMuted");
        label.Margin = new Thickness(0, 0, 0, 6);
        var box = new TextBox { Text = _draft, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Surface");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        box.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        box.TextChanged += (_, _) => _draft = box.Text;
        void Save()
        {
            try { using var lib = AppLibrary.Open(_dbPath); if (!lib.Rename(app.Id, _draft)) return; } catch { return; }
            _mode = "";
            RenderTracked();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Save();
            else if (e.Key == Key.Escape) { _mode = ""; RenderTracked(); }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = Button(AddAppStrings.Cancel, BtnKind.Outline, () => { _mode = ""; RenderTracked(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var save = Button(AddAppStrings.Save, BtnKind.Primary, Save);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(label);
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        return panel;
    }

    private UIElement StopConfirm(TrackedEntry app)
    {
        var box = new Border { Margin = new Thickness(0, 12, 0, 2), Padding = new Thickness(12), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        var panel = new StackPanel();
        var title = Text(null, "Double.FontSize.Body", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.StopTitle, app.DisplayName);
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        var body = Text("Str.AddStopBody", "Double.FontSize.Small", "Brush.TextMuted");
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 4, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var keep = Button(AddAppStrings.KeepTracking, BtnKind.Outline, () => { _mode = ""; RenderTracked(); });
        keep.Margin = new Thickness(0, 0, 8, 0);
        var stop = Button(AddAppStrings.StopTracking, BtnKind.DangerFill, () => StopTracking(app));
        buttons.Children.Add(keep);
        buttons.Children.Add(stop);
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        box.Child = panel;
        return box;
    }

    private void StopTracking(TrackedEntry app)
    {
        try { using var lib = AppLibrary.Open(_dbPath); lib.Retire(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch { return; }
        foreach (var path in app.Merged.Select(m => m.ExePath).Append(app.ExePath))
        {
            _addedElsewhere.Remove(path);
            _session.Remove(path);
        }
        _openId = null;
        _mode = "";
        UpdateCount();
        RenderTracked();
    }

    // ---- merge screen -------------------------------------------------------------------------------------

    private List<MergeCandidate> MergeCandidates(TrackedEntry keeper)
    {
        var list = _tracked.Where(a => a.Id != keeper.Id)
            .Select(a => new MergeCandidate("t" + a.Id, a.DisplayName, a.ExePath, DurationFormat.HoursMinutes(a.Seconds), _trackedIcons.GetValueOrDefault(a.Id), true, a, null, null))
            .ToList();
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            // already merged into the keeper: listed ticked, so unticking unmerges
            foreach (var m in keeper.Merged)
                list.Add(new MergeCandidate("m" + m.Id, m.DisplayName, m.ExePath, DurationFormat.HoursMinutes(m.Seconds), lib.GetIcon(m.Id), true, null, null, null, m));
            _library = lib.Library();
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + System.IO.Path.DirectorySeparatorChar;
            // stopped apps with history, minus Windows' own processes (explorer.exe and friends), which the Installed tab never lists
            foreach (var r in _library.Where(a => a.State == "retired" && !a.ExePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)))
                list.Add(new MergeCandidate("s" + r.Id, r.DisplayName, r.ExePath, TimeText.Str(AddAppStrings.SubStopped), lib.GetIcon(r.Id), false, null, r, null));
        }
        catch { _library = []; }
        if (_installed is null) LoadInstalled();
        foreach (var i in _installed ?? [])
        {
            if (StateOf(i.ExePath, i.PackageFamily) is not null) continue; // already tracked or stopped
            list.Add(new MergeCandidate("i" + i.ExePath.ToLowerInvariant(), i.Name, i.ExePath, i.IsSteam ? InstalledApp.SteamSource : i.Publisher is { Length: > 0 } pub ? pub : TimeText.Str(AddAppStrings.SubInstalled),
                _iconCache.GetValueOrDefault(i.ExePath), false, null, null, i));
        }
        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private UIElement BuildMergeScreen()
    {
        LoadTracked();
        var keeper = _tracked.FirstOrDefault(a => a.Id == _mergeSource);
        if (keeper is null) { _mergeSource = null; return BuildTrackedTab(); }
        var candidates = MergeCandidates(keeper);
        var query = "";
        _mergeTiles = [];

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Margin = new Thickness(20, 14, 20, 0) };
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.MergeTitle, keeper.DisplayName);
        title.FontWeight = FontWeights.Bold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = Text("Str.AddMergeSub", "Double.FontSize.Small", "Brush.TextMuted");
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 4, 0, 0);
        head.Children.Add(title);
        head.Children.Add(sub);
        root.Children.Add(head);

        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Margin = new Thickness(20, 12, 20, 0), Padding = new Thickness(3), BorderThickness = new Thickness(1), Child = segments };
        seg.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        seg.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        seg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        Grid.SetRow(seg, 1);
        root.Children.Add(seg);

        var list = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = list };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var note = Text(null, "Double.FontSize.Small", "Brush.Warning");
        note.Margin = new Thickness(20, 0, 20, 8);
        note.TextWrapping = TextWrapping.Wrap;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 12) };
        var cancel = Button(AddAppStrings.Cancel, BtnKind.Outline, () => { _mergeSource = null; _restoreOffset = true; ShowTracked(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var go = Button(AddAppStrings.Merge, BtnKind.Ghost, () => ConfirmMerge(keeper, candidates));
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        bool InFilter(MergeCandidate c) => _mergeFilter == "All" || (_mergeFilter == "Tracked") == c.Tracked;
        bool WasMerged(MergeCandidate c) => c.Merged is not null;

        void Render()
        {
            segments.Children.Clear();
            foreach (var (tag, key, count) in new[]
            {
                ("Tracked", AddAppStrings.TabTracked, candidates.Count(c => c.Tracked)),
                ("Untracked", AddAppStrings.FilterUntracked, candidates.Count(c => !c.Tracked)),
                ("All", AddAppStrings.FilterAll, candidates.Count),
            })
            {
                var on = _mergeFilter == tag;
                var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var name = Text("Str." + char.ToUpperInvariant(key[0]) + key[1..], "Double.FontSize.Body", on ? "Brush.Accent" : "Brush.TextMuted");
                name.FontWeight = FontWeights.SemiBold;
                var n = Text(null, "Double.FontSize.Small", on ? "Brush.Accent" : "Brush.TextMuted");
                n.Text = count.ToString();
                n.Margin = new Thickness(6, 0, 0, 0);
                n.VerticalAlignment = VerticalAlignment.Center;
                label.Children.Add(name);
                label.Children.Add(n);
                var segment = new Border { MinWidth = 110, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = label };
                segment.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
                if (on)
                {
                    segment.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                    segment.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                }
                else
                {
                    segment.Background = Brushes.Transparent;
                    segment.BorderBrush = Brushes.Transparent;
                }
                var t = tag;
                segment.MouseLeftButtonUp += (_, _) => { _mergeFilter = t; Render(); };
                segments.Children.Add(segment);
            }

            list.Children.Clear();
            _mergeTiles.Clear();
            var q = query.Trim();
            var shown = candidates.Where(c => InFilter(c) && (q.Length == 0 || c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (shown.Count == 0)
            {
                var none = Note(null);
                none.Text = TimeText.Str(AddAppStrings.NoMatch, q);
                list.Children.Add(none);
            }
            foreach (var c in shown)
                list.Children.Add(MergeTargetRow(c, _mergePicked.Contains(c.Key), () =>
                {
                    if (!_mergePicked.Remove(c.Key)) _mergePicked.Add(c.Key);
                    Render();
                }));

            var adds = candidates.Count(c => !WasMerged(c) && _mergePicked.Contains(c.Key));
            var removes = candidates.Count(c => WasMerged(c) && !_mergePicked.Contains(c.Key));
            var startsTracking = candidates.Any(c => !c.Tracked && _mergePicked.Contains(c.Key));
            note.Visibility = startsTracking ? Visibility.Visible : Visibility.Collapsed;
            note.Text = TimeText.Str(AddAppStrings.MergeStarts, keeper.DisplayName);
            var changed = adds + removes > 0;
            go.SetResourceReference(Control.BackgroundProperty, changed ? "Brush.Accent" : "Brush.SurfaceAlt");
            go.SetResourceReference(Control.ForegroundProperty, changed ? "Brush.OnAccent" : "Brush.TextMuted");
            go.SetResourceReference(Control.BorderBrushProperty, changed ? "Brush.Accent" : "Brush.Border");
            go.FontWeight = changed ? FontWeights.Bold : FontWeights.SemiBold;
            go.IsHitTestVisible = changed;
            if (!changed) go.SetResourceReference(ContentControl.ContentProperty, "Str.AddMerge");
            else if (removes == 0) go.Content = TimeText.Str(AddAppStrings.MergeCount, adds, keeper.DisplayName);
            else go.SetResourceReference(ContentControl.ContentProperty, "Str.AddSaveChanges");
        }
        Render();
        LoadMergeIcons(candidates, _mergeTiles);

        var search = SearchBox("Str.AddTrackedSearch", AddAppStrings.TrackedSearch, "", text => { query = text; Render(); });
        search.Margin = new Thickness(20, 0, 20, 10);
        var foot = new StackPanel();
        foot.Children.Add(note);
        foot.Children.Add(search);
        foot.Children.Add(buttons);
        Grid.SetRow(foot, 3);
        root.Children.Add(foot);
        return root;
    }

    /// <summary>Extracts the logos of installed candidates one by one, top of the list first; rows already on screen swap their letter for the logo.</summary>
    private void LoadMergeIcons(IReadOnlyList<MergeCandidate> candidates, List<(Border Tile, string Name, string ExePath)> tiles)
    {
        var todo = candidates.Where(c => c.Installed is not null && !_iconCache.ContainsKey(c.ExePath)).Select(c => c.Installed!).ToList();
        if (todo.Count == 0) return;
        Task.Run(() =>
        {
            foreach (var a in todo)
            {
                byte[]? png;
                try { png = IconService.ExtractPng(a.ExePath, a.Aumid, a.IconFile); } catch { png = null; }
                Dispatcher.BeginInvoke(() =>
                {
                    _iconCache[a.ExePath] = png;
                    if (png is null) return;
                    foreach (var (tile, name, path) in tiles.ToList())
                        if (string.Equals(path, a.ExePath, StringComparison.OrdinalIgnoreCase)) FillTile(tile, name, a.ExePath, png);
                }, DispatcherPriority.Background);
            }
        });
    }

    private UIElement MergeTargetRow(MergeCandidate t, bool picked, Action toggle)
    {
        var row = new Border { Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        row.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        if (picked)
        {
            row.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            row.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        }
        else
        {
            row.Background = Brushes.Transparent; // transparent, not null, so the whole row takes clicks
            row.BorderBrush = Brushes.Transparent;
        }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = MakeTile(t.Name, t.ExePath, t.Png);
        tile.Margin = new Thickness(0, 0, 12, 0);
        if (t.Installed is not null) _mergeTiles.Add((tile, t.Name, t.ExePath)); // icons arrive in the background and fill these
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var n = Text(null, "Double.FontSize.Body", "Brush.Text");
        n.Text = t.Name;
        n.FontWeight = FontWeights.SemiBold;
        n.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        s.Text = t.Sub;
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(n);
        texts.Children.Add(s);

        // checkbox: rounded square, ticked with the accent colour
        var box = new Border { Width = 18, Height = 18, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
        if (picked)
        {
            box.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            box.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            var tick = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4,9 L7.5,12.5 L14,5.5"), StrokeThickness = 2, Stretch = Stretch.None, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-2),
            };
            tick.SetResourceReference(Shape.StrokeProperty, "Brush.OnAccent");
            box.Child = tick;
        }
        else
        {
            box.Background = Brushes.Transparent;
            box.SetResourceReference(Border.BorderBrushProperty, "Brush.TextMuted");
        }
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(box, 2);
        g.Children.Add(tile);
        g.Children.Add(texts);
        g.Children.Add(box);
        row.Child = g;
        row.MouseLeftButtonUp += (_, _) => toggle();
        return row;
    }

    /// <summary>
    /// Folds every newly ticked app into <paramref name="keeper"/> (a stopped or merely installed one is tracked first)
    /// and unmerges every merged app that was unticked.
    /// </summary>
    private void ConfirmMerge(TrackedEntry keeper, IReadOnlyList<MergeCandidate> candidates)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            foreach (var c in candidates)
            {
                var ticked = _mergePicked.Contains(c.Key);
                if (c.Merged is not null)
                {
                    if (!ticked) lib.Unmerge(c.Merged.Id);
                    continue;
                }
                if (!ticked) continue;

                long id;
                if (c.Entry is not null) id = c.Entry.Id;
                else if (c.Stopped is not null) { lib.Restore(c.Stopped.Id, now); id = c.Stopped.Id; }
                else
                {
                    var app = c.Installed!;
                    var png = c.Png ?? IconService.ExtractPng(app.ExePath, app.Aumid, app.IconFile);
                    id = lib.AddTracked(app.ToNewApp(), now, png).AppId;
                }
                if (!lib.Merge(id, keeper.Id)) continue;
                if (!c.Tracked) _addedElsewhere.Add(c.ExePath);
            }
        }
        catch { return; }
        _mergeSource = null;
        _mergePicked.Clear();
        _restoreOffset = true;
        _openId = keeper.Id;
        _mode = "";
        UpdateCount();
        ShowTracked();
    }

    // ---- buttons ------------------------------------------------------------------------------------------

    private Button Button(string strKey, BtnKind kind, Action click)
    {
        var b = new Button { Template = Flat, Height = 34, Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand, Focusable = false, FontWeight = kind is BtnKind.Primary or BtnKind.DangerFill ? FontWeights.Bold : FontWeights.SemiBold };
        b.SetResourceReference(ContentControl.ContentProperty, "Str." + char.ToUpperInvariant(strKey[0]) + strKey[1..]);
        b.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        var (bg, fg, border) = kind switch
        {
            BtnKind.Primary => ("Brush.Accent", "Brush.OnAccent", "Brush.Accent"),
            BtnKind.DangerFill => ("Brush.Danger", "Brush.Surface", "Brush.Danger"),
            BtnKind.Danger => ("Brush.Surface", "Brush.Danger", "Brush.Border"),
            BtnKind.Ghost => ("Brush.SurfaceAlt", "Brush.TextMuted", "Brush.Border"),
            _ => ("Brush.Surface", "Brush.Text", "Brush.Border"),
        };
        b.SetResourceReference(Control.BackgroundProperty, bg);
        b.SetResourceReference(Control.ForegroundProperty, fg);
        b.SetResourceReference(Control.BorderBrushProperty, border);
        b.Click += (_, _) => click();
        return b;
    }
}
