using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>
/// Tracked tab: every tracked app with its own logo, and per app Rename, Merge into another app, and Stop tracking.
/// Merged apps are listed as chips under the app that keeps the name (an "x" on each chip unmerges).
/// </summary>
public sealed partial class AddAppView
{
    private enum BtnKind { Primary, Outline, Danger, DangerFill, Ghost }

    private IReadOnlyList<TrackedEntry> _tracked = [];
    private readonly Dictionary<long, byte[]?> _trackedIcons = new();
    private readonly HashSet<long> _logoTried = new(); // tracked apps whose tile-size logo was asked for in this view
    private string _trackedQuery = "";
    private StackPanel _trackedList = null!;
    private ScrollViewer _trackedScroll = null!;
    private TextBlock _trackedHeader = null!;
    private string _draft = "";
    private MergePlan? _mergePlan;      // the open merge screen's plan (Candidates, ticks, confirm) lives in AppCatalogue; which screen shows is in _nav
    private List<(Border Tile, string Name, string ExePath)> _mergeTiles = [];
    private double _listOffset;            // Tracked list scroll position when Merge… was opened
    private string _mergeFilter = "Tracked";  // Tracked | Untracked | All

    private void ShowTracked() => _body.Content = _nav.State.Merge == MergeStage.Ready && _mergePlan is { } plan ? BuildMergeScreen(plan) : BuildTrackedTab();

    private UIElement BuildTrackedTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var search = SearchBox("Str.AddTrackedSearch", AddAppStrings.TrackedSearch, _trackedQuery, text => { _trackedQuery = text; RenderTracked(reload: false); });
        root.Children.Add(search);

        _trackedHeader = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        _trackedHeader.Margin = new Thickness(8, 8, 0, 8);
        _trackedList = new StackPanel { Margin = new Thickness(12, 4, 12, 8) };
        _trackedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _trackedList };
        Grid.SetRow(_trackedScroll, 1);
        root.Children.Add(_trackedScroll);
        var hint = Text("Str.AddTrackedHint", "Double.FontSize.Small", "Brush.TextMuted");
        hint.Margin = new Thickness(20, 8, 20, 4);
        Grid.SetRow(hint, 2);
        root.Children.Add(hint);
        RenderTracked();
        if (_nav.TakeScrollRestore()) // back from the merge screen: land where the list was
        {
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
        var snapshot = _catalogue.Tracked(_trackedIcons.Keys.ToHashSet());
        if (snapshot.Outcome == CatalogueOutcome.Done) // otherwise the database is busy: keep what is on screen
        {
            _tracked = snapshot.Apps;
            foreach (var a in _tracked.Where(a => !_trackedIcons.ContainsKey(a.Id)).ToList())
                _trackedIcons[a.Id] = CachedIcon(a.ExePath) ?? snapshot.Icons.GetValueOrDefault(a.Id);
        }
        UpgradeTrackedIcons();
    }

    /// <summary>Stored icons are 64 px; asks for each tracked app's logo at tile resolution once per view, then redraws when one arrived.</summary>
    private void UpgradeTrackedIcons()
    {
        var todo = _tracked.Where(a => _logoTried.Add(a.Id)).ToList();
        if (todo.Count == 0) return;
        var installed = (_catalogue.LastInstalled ?? Array.Empty<InstalledApp>()).GroupBy(i => i.ExePath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            var found = new Dictionary<long, byte[]>();
            foreach (var a in todo)
            {
                installed.TryGetValue(a.ExePath, out var inst);
                try
                {
                    if (_icons.ForPath(a.ExePath, inst?.Aumid, inst?.IconFile, TileIconPx, cachedOnly: false, out var extracted) is { } png && extracted) found[a.Id] = png;
                }
                catch { /* keep the stored icon */ }
            }
            return found;
        }).ContinueWith(t =>
        {
            if (t.Result.Count == 0) return;
            foreach (var (id, png) in t.Result) _trackedIcons[id] = png;
            if (_trackedList is not null) RenderTracked();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RenderTracked() => RenderTracked(reload: true);

    /// <param name="reload">Re-read the tracked apps from the database; a search keystroke reuses the last read.</param>
    private void RenderTracked(bool reload)
    {
        if (_trackedList is null) return;
        var offset = _trackedScroll.VerticalOffset;
        if (reload) LoadTracked();
        _trackedList.Children.Clear();
        var q = _trackedQuery.Trim();
        var shown = q.Length == 0 ? _tracked.ToList() : _tracked
            .Select(a => (App: a, Score: FuzzySearch.Score(q, a.Merged.Select(m => m.DisplayName).Prepend(a.DisplayName), PublisherOf(a))))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => x.App).ToList();

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
        foreach (var app in shown) _trackedList.Children.Add(Cached(TrackedRow(app)));
        _trackedScroll.ScrollToVerticalOffset(offset);
    }

    private UIElement TrackedRow(TrackedEntry app)
    {
        var open = _nav.State.OpenRowId == app.Id;
        var mode = _nav.State.RowMode;
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
        var tile = AppTile.Candidate(_themes, app.ExePath, app.DisplayName, _trackedIcons.GetValueOrDefault(app.Id), TileStyle.CandidateRow); // the app's own logo
        var tileHost = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        tileHost.Children.Add(tile);
        if (app.Merged.Count > 0) tileHost.Children.Add(MergedBadge(app.Merged.Count));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = app.DisplayName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        name.Cursor = Cursors.IBeam; // double-click the name to rename
        card.Cursor = Cursors.Hand;  // double-click the rest of the row for settings
        if (!open)
        {
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        }
        var sub = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        sub.Text = PublisherOf(app) ?? TimeText.Str(AddAppStrings.TrackedNoPublisher);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(open && mode == TrackedRowMode.Rename ? RenameInline(app) : name);
        texts.Children.Add(sub);

        Grid.SetColumn(texts, 1);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(KindSwitch(app.Kind, () => SetKind(app.Id, AppKinds.Next(app.Kind), RenderTracked)));
        right.Children.Add(BackgroundSwitch(app.Background, () => SetBackground(app.Id, !app.Background, RenderTracked)));
        var stopBtn = ActionIcon(IconEyeOpen, PillStyle.Quiet, AddAppStrings.StopTip, AddAppStrings.Stop, lit: open && mode == TrackedRowMode.Stop);
        stopBtn.Click += (_, _) => { _nav.OpenRowStop(app.Id); RenderTracked(); };
        right.Children.Add(stopBtn);
        Grid.SetColumn(right, 2);
        top.Children.Add(tileHost);
        top.Children.Add(texts);
        top.Children.Add(right);
        stack.Children.Add(top);

        if (open && mode == TrackedRowMode.Stop) stack.Children.Add(StopConfirm(app));

        // double-click the name to rename it in place; double-click anywhere else on the row for the app's settings
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || _nav.State.RowMode == TrackedRowMode.Rename) return;
            e.Handled = true;
            if (ReferenceEquals(e.OriginalSource, name)) { _nav.OpenRowRename(app.Id); _draft = app.DisplayName; RenderTracked(); }
            else ShowAppSettings(app.Id);
        };
        return card;
    }

    /// <summary>The publisher from the installed-apps scan (matched by exe path); null until the scan has loaded or when the app is not in it.</summary>
    private string? PublisherOf(TrackedEntry app)
    {
        if (_catalogue.LastInstalled is not { } installed) { LoadInstalled(); return null; }
        var inst = installed.FirstOrDefault(i => string.Equals(i.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase));
        if (inst is null) return null;
        return inst.IsSteam ? InstalledApp.SteamSource : inst.Publisher is { Length: > 0 } pub ? pub : null;
    }

    /// <summary>Number of merged apps, pinned to the logo's bottom-right corner.</summary>
    private static Border MergedBadge(int count)
    {
        var text = new TextBlock { Text = count.ToString(), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        var badge = new Border
        {
            Child = text, MinWidth = 18, Height = 18, Padding = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -6),
            BorderThickness = new Thickness(2),
        };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Panel");
        return badge;
    }

    /// <summary>The name as a text box: Enter saves, Esc or leaving the box cancels.</summary>
    private UIElement RenameInline(TrackedEntry app)
    {
        var box = new TextBox { Text = _draft, Padding = new Thickness(6, 1, 6, 1), BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, VerticalContentAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.Surface");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        box.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        box.TextChanged += (_, _) => _draft = box.Text;
        var done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            if (save)
            {
                _catalogue.ApplyChange(AppChange.Rename(app.Id, _draft)); // blank or database busy: keep the old name
            }
            _nav.EndRowMode();
            RenderTracked();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
            else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
        };
        box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => Finish(false)); // clicking elsewhere cancels, like Esc
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return box;
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
        var keep = Button(AddAppStrings.KeepTracking, BtnKind.Outline, () => { _nav.EndRowMode(); RenderTracked(); });
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
        if (_catalogue.ApplyChange(AppChange.Retire(app)) != CatalogueOutcome.Done) return;
        _nav.CloseRow();
        UpdateCount();
        RenderTracked();
    }

    // ---- merge screen -------------------------------------------------------------------------------------

    /// <summary>
    /// Opens the merge screen for a Main app. The plan (and the first installed-apps scan) is built on a pool thread, so a
    /// loading note shows meanwhile; if the app is gone or the database is busy the Tracked tab shows instead.
    /// </summary>
    private void OpenMergeScreen(long mainId)
    {
        _mergePlan = null;
        _nav.BeginMergeOpen(mainId);
        _body.Content = Note("Str.AddInstalledLoading");
        Task.Run(() => _catalogue.OpenMergePlan(mainId, TileIconPx)).ContinueWith(t =>
        {
            var plan = t.IsCompletedSuccessfully ? t.Result : null;
            if (!_nav.CompleteMergeOpen(mainId, plan is not null)) return; // the user went elsewhere meanwhile
            _mergePlan = plan;
            if (_nav.State.Tab == AddAppTab.Tracked) ShowTracked();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The line under a Candidate's name.</summary>
    private static string SubOf(MergeCandidate c) => c.Kind switch
    {
        CandidateKind.Tracked or CandidateKind.Merged => UsageText.HoursMinutes(c.Seconds),
        CandidateKind.Retired => TimeText.Str(AddAppStrings.SubStopped),
        _ => c.Installed!.IsSteam ? InstalledApp.SteamSource : c.Installed.Publisher is { Length: > 0 } pub ? pub : TimeText.Str(AddAppStrings.SubInstalled),
    };

    /// <summary>A tracked Candidate shows the tile-resolution logo when this process already extracted it.</summary>
    private byte[]? PngOf(MergeCandidate c) => c.Kind == CandidateKind.Tracked ? _trackedIcons.GetValueOrDefault(c.AppId) : c.Png;

    private UIElement BuildMergeScreen(MergePlan plan)
    {
        var candidates = plan.Candidates;
        var query = "";
        _mergeTiles = [];

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Margin = new Thickness(20, 14, 20, 0) };
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.MergeTitle, plan.MainName);
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
        var cancel = Button(AddAppStrings.Cancel, BtnKind.Outline, () => { _mergePlan = null; _nav.CancelMerge(); ShowTracked(); ReturnToSettings(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var go = Button(AddAppStrings.Merge, BtnKind.Ghost, () => ConfirmMerge(plan));
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        bool InFilter(MergeCandidate c) => _mergeFilter == "All" || (_mergeFilter == "Tracked") == c.IsTracked;

        void Render()
        {
            segments.Children.Clear();
            foreach (var (tag, key, count) in new[]
            {
                ("Tracked", AddAppStrings.TabTracked, candidates.Count(c => c.IsTracked)),
                ("Untracked", AddAppStrings.FilterUntracked, candidates.Count(c => !c.IsTracked)),
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
                list.Children.Add(MergeTargetRow(c, plan.IsTicked(c.Key), () =>
                {
                    plan.Toggle(c.Key);
                    Render();
                }));

            var (adds, removes, startsTracking) = plan.Summary;
            var changed = plan.Summary.Changed;
            note.Visibility = startsTracking ? Visibility.Visible : Visibility.Collapsed;
            note.Text = TimeText.Str(AddAppStrings.MergeStarts, plan.MainName);
            go.SetResourceReference(Control.BackgroundProperty, changed ? "Brush.Accent" : "Brush.SurfaceAlt");
            go.SetResourceReference(Control.ForegroundProperty, changed ? "Brush.OnAccent" : "Brush.TextMuted");
            go.SetResourceReference(Control.BorderBrushProperty, changed ? "Brush.Accent" : "Brush.Border");
            go.FontWeight = changed ? FontWeights.Bold : FontWeights.SemiBold;
            go.IsHitTestVisible = changed;
            if (!changed) go.SetResourceReference(ContentControl.ContentProperty, "Str.AddMerge");
            else if (removes == 0) go.Content = TimeText.Str(AddAppStrings.MergeCount, adds, plan.MainName.Length > 20 ? plan.MainName[..19] + "…" : plan.MainName);
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
        var todo = candidates.Where(c => c.Kind == CandidateKind.Installed).Select(c => c.Installed!).ToList();
        LoadTileIcons(todo, (a, png) =>
        {
            foreach (var (tile, name, path) in tiles.ToList())
                if (string.Equals(path, a.ExePath, StringComparison.OrdinalIgnoreCase)) AppTile.Fill(tile, _themes, a.ExePath, name, png, TileStyle.CandidateRow);
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
        var tile = AppTile.Candidate(_themes, t.ExePath, t.Name, PngOf(t), TileStyle.CandidateRow);
        tile.Margin = new Thickness(0, 0, 12, 0);
        if (t.Kind == CandidateKind.Installed) _mergeTiles.Add((tile, t.Name, t.ExePath)); // icons arrive in the background and fill these
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var n = Text(null, "Double.FontSize.Body", "Brush.Text");
        n.Text = t.Name;
        n.FontWeight = FontWeights.SemiBold;
        n.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        s.Text = SubOf(t);
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

    /// <summary>Confirms the plan; if the database is busy the screen stays as it is so the user can retry.</summary>
    private void ConfirmMerge(MergePlan plan)
    {
        if (plan.Confirm() == CatalogueOutcome.Unavailable) return;
        _mergePlan = null;
        _nav.MergeConfirmed(plan.MainId);
        UpdateCount();
        ShowTracked();
        ReturnToSettings();
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
