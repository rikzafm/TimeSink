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
    private IReadOnlySet<long> _chosenIcons = new HashSet<long>();
    private string _trackedQuery = "";
    private StackPanel _trackedList = null!;
    private ScrollViewer _trackedScroll = null!;
    private TextBlock _trackedHeader = null!;
    private static readonly object TrackedRowTag = new(); // marks a row's card, so a click can tell a row from empty list space
    private string _draft = "";
    private MergePlan? _mergePlan;      // the open merge screen's plan (Candidates, ticks, confirm) lives in AppCatalogue; which screen shows is in _nav
    private double _listOffset;            // Tracked list scroll position when Merge… was opened

    public void ShowTracked()
    {
        if (_nav.State.Merge == MergeStage.Ready && _mergePlan is { } plan)
            Content = BuildMergeScreen(plan);
        else
            _body.Content = BuildTrackedTab();
    }

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
        _trackedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = _trackedList, Background = Brushes.Transparent };
        _trackedScroll.PreviewMouseLeftButtonDown += (_, e) => // a click on empty list space deselects the open row
        {
            if (_nav.State.OpenRowId is null || IsInTrackedRow(e.OriginalSource as DependencyObject)) return;
            _nav.CloseRow();
            RenderTracked();
        };
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

    private Border SearchBox(string hintKey, string automationKey, string initial, Action<string> changed) =>
        SearchBoxWithHint(hintKey, automationKey, initial, changed).Border;

    private (Border Border, Action<string> UpdateHint) SearchBoxWithHint(string hintKey, string automationKey, string initial, Action<string> changed)
    {
        var box = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, Text = initial };
        System.Windows.Automation.AutomationProperties.SetName(box, TimeText.Str(automationKey));
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Brush.Text");
        box.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
        var hint = Text(hintKey.StartsWith("Str.") ? hintKey : null, "Double.FontSize.Body", "Brush.TextMuted");
        if (!hintKey.StartsWith("Str.")) hint.Text = hintKey;
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Visibility = initial.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            changed(box.Text);
        };
        var updateHint = (Action<string>)(newHint =>
        {
            hint.ClearValue(TextBlock.TextProperty);
            hint.Text = newHint;
        });
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
        return (border, updateHint);
    }

    private void LoadTracked()
    {
        var snapshot = _catalogue.Tracked(_trackedIcons.Keys.ToHashSet());
        if (snapshot.Outcome == CatalogueOutcome.Done) // otherwise the database is busy: keep what is on screen
        {
            _tracked = snapshot.Apps;
            foreach (var a in _tracked.Where(a => !_trackedIcons.ContainsKey(a.Id)).ToList())
                _trackedIcons[a.Id] = CachedIcon(a.ExePath) ?? snapshot.Icons.GetValueOrDefault(a.Id);
            _chosenIcons = _catalogue.ChosenIcons();
            foreach (var id in _chosenIcons) _trackedIcons[id] = _catalogue.StoredIcon(id); // a picked icon beats the exe's own
        }
        UpgradeTrackedIcons();
    }

    /// <summary>Stored icons are 64 px; asks for each tracked app's logo at tile resolution (once per visit), then redraws when one arrived.</summary>
    private void UpgradeTrackedIcons()
    {
        var todo = _tracked.Where(a => !_chosenIcons.Contains(a.Id)).ToList();
        if (todo.Count == 0) return;
        Task.Run(() => _icons.UpgradeTracked(todo, _catalogue.InstalledBy, TileIconPx)).ContinueWith(t =>
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
        if (_catalogue.LastInstalled is null) LoadInstalled(); // publishers come from the installed-apps scan
        var shown = TrackedSearch.Apply(_tracked, q, path => _catalogue.InstalledBy(path)?.Byline);

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

    private bool IsInTrackedRow(DependencyObject? d)
    {
        for (; d is not null && !ReferenceEquals(d, _trackedScroll); d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && ReferenceEquals(fe.Tag, TrackedRowTag)) return true;
        return false;
    }

    /// <summary>Esc closes the open row (its stop question or rename box too), when nothing else on the Tracked tab owns the key.</summary>
    private void OnTrackedPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Keyboard.FocusedElement is TextBox) return;
        if (_nav.State is not { Tab: AddAppTab.Tracked, Merge: MergeStage.None, SettingsId: null, OpenRowId: not null }) return;
        e.Handled = true;
        _nav.CloseRow();
        RenderTracked();
    }

    private UIElement TrackedRow(TrackedEntry app)
    {
        var open = _nav.State.OpenRowId == app.Id;
        var mode = _nav.State.RowMode;
        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10), BorderThickness = new Thickness(1), Tag = TrackedRowTag };
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

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(name);

        var devs = new List<string>();
        if (!string.IsNullOrEmpty(app.DeviceId)) devs.Add(app.DeviceId);
        devs.AddRange(app.Merged.Select(m => m.DeviceId).Where(d => !string.IsNullOrEmpty(d)));
        var distinctDevs = devs.Distinct().ToList();
        if (distinctDevs.Count > 1)
        {
            var devInfo = DeviceInfoService.Load(_dbPath);
            var localDev = devInfo.FirstOrDefault(kv => kv.Value.IsLocal).Key;
            var ordered = distinctDevs.OrderByDescending(d => DeviceInfoService.IsLocal(devInfo, d, localDev)).ToList();
            foreach (var d in ordered)
            {
                var isLocal = DeviceInfoService.IsLocal(devInfo, d, localDev);
                var marker = new DeviceMarker
                {
                    IsLocal = isLocal,
                    Width = 6,
                    Height = 6,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                nameRow.Children.Add(marker);
            }
        }

        var sub = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        sub.Text = PublisherOf(app) ?? TimeText.Str(AddAppStrings.TrackedNoPublisher);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(open && mode == TrackedRowMode.Rename ? RenameInline(app) : nameRow);
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
            if (e.ClickCount == 1) // a click selects the row; Esc then works from here
            {
                if (_nav.SelectRow(app.Id)) RenderTracked();
                if (_nav.State.RowMode != TrackedRowMode.Rename) Focus();
                return;
            }
            if (e.ClickCount != 2 || _nav.State.RowMode == TrackedRowMode.Rename) return;
            e.Handled = true;
            if (ReferenceEquals(e.OriginalSource, name)) { _nav.OpenRowRename(app.Id); _draft = app.DisplayName; RenderTracked(); }
            else ShowAppSettings(app.Id);
        };
        return card;
    }

    /// <summary>The publisher from the installed-apps scan (matched by exe path); null until the scan has loaded or when the app is not in it.</summary>
    private string? PublisherOf(TrackedEntry app) => _catalogue.InstalledBy(app.ExePath)?.Byline;

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
    private void OpenMergeScreen(long mainId, string? appName = null)
    {
        _mergePlan = null;
        _nav.BeginMergeOpen(mainId);

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12, 16, 6), VerticalAlignment = VerticalAlignment.Center };
        var back = IconButton("M15,6 L9,12 L15,18", "Brush.TextSoft");
        back.Margin = new Thickness(0, 0, 8, 0);
        back.Click += (_, _) => { _mergePlan = null; _nav.CancelMerge(); ReturnToSettings(); };
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = appName != null ? TimeText.Str(AddAppStrings.MergeTitle, appName) : TimeText.Str(AddAppStrings.MergeTitle, "");
        title.FontWeight = FontWeights.Bold;
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(back);
        head.Children.Add(title);
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        var note = Note("Str.AddInstalledLoading");
        Grid.SetRow(note, 1);
        root.Children.Add(note);
        Content = root;

        Task.Run(() => _catalogue.OpenMergePlan(mainId, TileIconPx)).ContinueWith(t =>
        {
            var plan = t.IsCompletedSuccessfully ? t.Result : null;
            if (!_nav.CompleteMergeOpen(mainId, plan is not null)) return;
            _mergePlan = plan;
            if (plan != null)
                Content = BuildMergeScreen(plan);
            else
                ReturnToSettings();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The line under a Candidate's name.</summary>
    private static string SubOf(MergeCandidate c) => c.Kind switch
    {
        CandidateKind.Merged => "Merged",
        CandidateKind.Tracked => "Tracked",
        CandidateKind.Retired => TimeText.Str(AddAppStrings.SubStopped),
        _ => c.Installed?.Byline ?? TimeText.Str(AddAppStrings.SubInstalled),
    };

    /// <summary>A tracked Candidate shows the tile-resolution logo when this process already extracted it.</summary>
    private byte[]? PngOf(MergeCandidate c) => c.Kind == CandidateKind.Tracked ? _trackedIcons.GetValueOrDefault(c.AppId) : c.Png;

    private UIElement BuildMergeScreen(MergePlan plan)
    {
        var candidates = plan.Candidates;
        _mergeTiles.Clear();

        Action<string> updateSearchHint = _ => { };

        var devInfo = DeviceInfoService.Load(_dbPath);
        var localDev = devInfo.FirstOrDefault(kv => kv.Value.IsLocal).Key;
        var devices = plan.Devices;
        var orderedDevices = devices.OrderByDescending(d => DeviceInfoService.IsLocal(devInfo, d, localDev)).ToList();
        if (orderedDevices.Count == 0)
            orderedDevices.Add(localDev ?? plan.MainDeviceId ?? "local");
        if (plan.DeviceFilter == null && orderedDevices.Count > 0)
            plan.DeviceFilter = orderedDevices[0];

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Focusable = true;
        root.Loaded += (_, _) => root.Focus();
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            _mergePlan = null;
            _nav.CancelMerge();
            ReturnToSettings();
        };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12, 16, 6), VerticalAlignment = VerticalAlignment.Center };
        var back = IconButton("M15,6 L9,12 L15,18", "Brush.TextSoft");
        back.Margin = new Thickness(0, 0, 8, 0);
        back.Click += (_, _) => { _mergePlan = null; _nav.CancelMerge(); ReturnToSettings(); };
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.MergeTitle, plan.MainName);
        title.FontWeight = FontWeights.Bold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(back);
        head.Children.Add(title);

        var devTabs = new StackPanel { Orientation = Orientation.Horizontal };
        var devTabsBar = new Border
        {
            Margin = new Thickness(16, 0, 16, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = devTabs
        };
        devTabsBar.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        var headerArea = new StackPanel();
        headerArea.Children.Add(head);
        if (orderedDevices.Count > 0) headerArea.Children.Add(devTabsBar);
        Grid.SetRow(headerArea, 0);
        root.Children.Add(headerArea);

        var list = new StackPanel { Margin = new Thickness(16, 8, 16, 8) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = list };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // Foot: preview card, note, buttons
        var previewCard = new Border
        {
            Margin = new Thickness(20, 6, 20, 6),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
        };
        previewCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        previewCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        previewCard.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        var previewText = Text(null, "Double.FontSize.Small", "Brush.Text");
        previewText.TextWrapping = TextWrapping.Wrap;
        previewCard.Child = previewText;

        var note = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        note.Margin = new Thickness(20, 0, 20, 8);
        note.TextWrapping = TextWrapping.Wrap;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 12) };
        var cancel = Button(AddAppStrings.Cancel, BtnKind.Outline, () => { _mergePlan = null; _nav.CancelMerge(); ReturnToSettings(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var go = Button(AddAppStrings.Merge, BtnKind.Ghost, () => ConfirmMerge(plan));
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        var foot = new StackPanel();
        foot.Children.Add(previewCard);
        foot.Children.Add(note);
        foot.Children.Add(buttons);
        Grid.SetRow(foot, 3);
        root.Children.Add(foot);

        var topStack = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
        var sub = Text("Str.AddMergeSub", "Double.FontSize.Small", "Brush.TextMuted");
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, 0, 0);
        topStack.Children.Add(sub);

        // 1. Filter segments (Tracked / Untracked / All)
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = segments };
        seg.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        seg.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        seg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        // 2. Suggested match banner
        var suggestedBanner = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
        };
        suggestedBanner.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        suggestedBanner.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        suggestedBanner.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var bannerGrid = new Grid();
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bannerText = Text("Str.AddSuggestedMatch", "Double.FontSize.Small", "Brush.Text");
        bannerText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(bannerText, 0);
        bannerGrid.Children.Add(bannerText);

        var tickMatchBtn = Button(AddAppStrings.TickMatch, BtnKind.Primary, () =>
        {
            foreach (var match in plan.SuggestedMatches)
            {
                if (!plan.IsTicked(match.Key))
                    plan.Toggle(match.Key);
            }
            Render();
        });
        tickMatchBtn.Height = 26;
        tickMatchBtn.Padding = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(tickMatchBtn, 1);
        bannerGrid.Children.Add(tickMatchBtn);
        suggestedBanner.Child = bannerGrid;

        // 3. Search box
        var searchBoxRes = SearchBoxWithHint("Str.AddTrackedSearch", AddAppStrings.TrackedSearch, plan.Query, text => { plan.Query = text; Render(); });
        var search = searchBoxRes.Border;
        updateSearchHint = searchBoxRes.UpdateHint;

        var controlsGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        search.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(search, 0);
        controlsGrid.Children.Add(search);
        Grid.SetColumn(seg, 1);
        controlsGrid.Children.Add(seg);

        topStack.Children.Add(controlsGrid);
        topStack.Children.Add(suggestedBanner);

        Grid.SetRow(topStack, 1);
        root.Children.Add(topStack);

        void Render()
        {
            // Update device tabs
            devTabs.Children.Clear();
            if (orderedDevices.Count > 0)
            {
                foreach (var d in orderedDevices)
                {
                    var isLocal = DeviceInfoService.IsLocal(devInfo, d, localDev);
                    var devName = DeviceInfoService.GetName(devInfo, d, isLocal);
                    var labelText = isLocal ? "This PC" : devName;
                    var isSelected = string.Equals(plan.DeviceFilter, d, StringComparison.OrdinalIgnoreCase);
                    var tickedCount = plan.TickedCountForDevice(d);

                    var tabInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    tabInner.Children.Add(new DeviceMarker
                    {
                        IsLocal = isLocal,
                        Width = 7,
                        Height = 7,
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var tabLabel = Text(null, "Double.FontSize.Body", isSelected ? "Brush.Text" : "Brush.TextMuted");
                    tabLabel.Text = labelText;
                    tabLabel.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                    tabLabel.VerticalAlignment = VerticalAlignment.Center;
                    tabInner.Children.Add(tabLabel);

                    if (tickedCount > 0)
                    {
                        var badge = new Border
                        {
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(6, 1, 6, 1),
                            Margin = new Thickness(6, 0, 0, 0),
                            Height = 16,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        badge.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
                        var badgeText = new TextBlock
                        {
                            Text = tickedCount.ToString(),
                            FontSize = 10,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
                        badgeText.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
                        badge.Child = badgeText;
                        tabInner.Children.Add(badge);
                    }

                    var tabItem = new Border
                    {
                        Height = 32,
                        Padding = new Thickness(0, 0, 0, isSelected ? 4 : 6),
                        Margin = new Thickness(0, 0, 16, -1),
                        BorderThickness = new Thickness(0, 0, 0, isSelected ? 2 : 0),
                        Cursor = Cursors.Hand,
                        Background = Brushes.Transparent,
                        Child = tabInner
                    };
                    if (isSelected) tabItem.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
                    tabItem.MouseEnter += (_, _) =>
                    {
                        if (!isSelected) tabLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                    };
                    tabItem.MouseLeave += (_, _) =>
                    {
                        if (!isSelected) tabLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                    };
                    var targetDev = d;
                    tabItem.MouseLeftButtonUp += (_, _) =>
                    {
                        plan.DeviceFilter = targetDev;
                        Render();
                    };
                    devTabs.Children.Add(tabItem);
                }
            }

            // Update search placeholder
            var activeDeviceName = plan.DeviceFilter != null ? (DeviceInfoService.IsLocal(devInfo, plan.DeviceFilter, localDev) ? "This PC" : DeviceInfoService.GetName(devInfo, plan.DeviceFilter, false)) : "";
            updateSearchHint?.Invoke(orderedDevices.Count > 1 && !string.IsNullOrEmpty(activeDeviceName) ? $"Search apps on {activeDeviceName}" : TimeText.Str(AddAppStrings.TrackedSearch));

            // Update suggestions banner
            var untickedSuggestions = plan.SuggestedMatches.Where(s => !plan.IsTicked(s.Key)).ToList();
            suggestedBanner.Visibility = untickedSuggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Update filter segments
            segments.Children.Clear();
            foreach (var (filter, key) in new[]
            {
                (MergeFilter.Tracked, AddAppStrings.TabTracked),
                (MergeFilter.Untracked, AddAppStrings.FilterUntracked),
                (MergeFilter.All, AddAppStrings.FilterAll),
            })
            {
                var on = plan.Filter == filter;
                var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var name = Text("Str." + char.ToUpperInvariant(key[0]) + key[1..], "Double.FontSize.Small", on ? "Brush.Text" : "Brush.TextMuted");
                name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                label.Children.Add(name);
                var segment = new Border { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = label };
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
                var f = filter;
                segment.MouseLeftButtonUp += (_, _) => { plan.Filter = f; Render(); };
                segments.Children.Add(segment);
            }

            // Update candidate list
            list.Children.Clear();
            _mergeTiles.Clear();
            var q = plan.Query.Trim();
            var shown = plan.Shown();
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

            // Update preview card and note
            var tickedCandidates = plan.Candidates.Where(c => plan.IsTicked(c.Key)).ToList();
            var mainH = Math.Max(0, plan.MainSeconds / 3600);
            var totalH = Math.Max(0, (plan.MainSeconds + tickedCandidates.Sum(c => c.Seconds)) / 3600);

            if (tickedCandidates.Count == 0)
            {
                previewText.Text = $"{plan.MainName} ({mainH} h) under “{plan.MainName}”.";
            }
            else if (tickedCandidates.Count <= 2)
            {
                var parts = string.Join(" + ", tickedCandidates.Select(c => $"{c.Name} ({Math.Max(0, c.Seconds / 3600)} h)"));
                previewText.Text = $"{plan.MainName} ({mainH} h) + {parts} = {totalH} h under “{plan.MainName}”.";
            }
            else
            {
                var tickedH = Math.Max(0, tickedCandidates.Sum(c => c.Seconds) / 3600);
                previewText.Text = $"{plan.MainName} ({mainH} h) + {tickedCandidates.Count} apps ({tickedH} h) = {totalH} h under “{plan.MainName}”.";
            }

            var (adds, removes, startsTracking) = plan.Summary;
            var changed = plan.Summary.Changed;

            var otherDevs = orderedDevices.Where(d => !string.Equals(d, plan.DeviceFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            var otherNotes = otherDevs.Count > 0
                ? " Ticks on other devices stay: " + string.Join(" · ", otherDevs.Select(d => $"{(DeviceInfoService.IsLocal(devInfo, d, localDev) ? "This PC" : DeviceInfoService.GetName(devInfo, d, false))} {plan.TickedCountForDevice(d)}")) + "."
                : "";
            note.Text = $"Ticked untracked apps start being tracked now and count under {plan.MainName}.{otherNotes}";

            // Update buttons
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
        LoadMergeIcons(candidates);

        return root;
    }

    /// <summary>Extracts the logos of installed candidates one by one, top of the list first; rows already on screen swap their letter for the logo.</summary>
    private void LoadMergeIcons(IReadOnlyList<MergeCandidate> candidates)
    {
        var todo = candidates.Where(c => c.Kind == CandidateKind.Installed).Select(c => c.Installed!).ToList();
        LoadTileIcons(todo, (a, png) => _mergeTiles.Fill(a.ExePath, png));
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = AppTile.Candidate(_themes, t.ExePath, t.Name, PngOf(t), TileStyle.CandidateRow);
        tile.Margin = new Thickness(0, 0, 12, 0);
        if (t.Kind == CandidateKind.Installed) _mergeTiles.Register(t.ExePath, tile, t.Name); // icons arrive in the background and fill these
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

        var time = Text(null, "Double.FontSize.Body", "Brush.TextMuted");
        if (t.Seconds > 0)
        {
            time.Text = UsageText.Hours(t.Seconds) + " h";
            time.VerticalAlignment = VerticalAlignment.Center;
            time.Margin = new Thickness(8, 0, 12, 0);
        }

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
        Grid.SetColumn(time, 2);
        Grid.SetColumn(box, 3);
        g.Children.Add(tile);
        g.Children.Add(texts);
        if (t.Seconds > 0) g.Children.Add(time);
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
