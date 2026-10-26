using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>
/// The per-app settings screen, opened by double-clicking a row on the Tracked tab. It fills the whole Add app view and
/// every change applies at once: name, game type, Background flag, merged apps, and the stop / clear history actions.
/// </summary>
public sealed partial class AddAppView
{
    private long? _settingsId;
    private long? _settingsReturn;   // set while the merge screen is open on behalf of the settings screen
    private double _settingsOffset;
    private ScrollViewer? _settingsScroll;
    private string _settingsAsk = ""; // "", "stop": the confirmation open in the danger zone
    private int _clearStep;           // 0 = no modal, 1..3 = which of the three Clear history questions is showing

    private void ShowAppSettings(long id)
    {
        _settingsId = id;
        _settingsAsk = "";
        _clearStep = 0;
        _settingsOffset = 0;
        Content = BuildAppSettings();
    }

    private void RefreshSettings()
    {
        _settingsOffset = _settingsScroll?.VerticalOffset ?? 0;
        Content = BuildAppSettings();
    }

    private void CloseSettings()
    {
        _settingsId = null;
        _settingsReturn = null;
        _settingsScroll = null;
        _clearStep = 0;
        Content = _shell;
        if (_trackedList is not null && _tab == "Tracked" && _mergePlan is null) RenderTracked();
    }

    /// <summary>Back to the settings screen after the merge screen it opened is done.</summary>
    private void ReturnToSettings()
    {
        if (_settingsReturn is not { } id) return;
        _settingsReturn = null;
        ShowAppSettings(id);
    }

    private UIElement BuildAppSettings()
    {
        LoadTracked();
        var app = _tracked.FirstOrDefault(a => a.Id == _settingsId);
        if (app is null) { CloseSettings(); return _shell; }

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(SettingsHeader(app));

        var sections = new StackPanel { Margin = new Thickness(18, 4, 18, 14) };
        var bg = BackgroundSwitch(app.Background, () => SetBackground(app.Id, !app.Background, RefreshSettings));
        bg.Margin = new Thickness(0);
        var group = new StackPanel();
        group.Children.Add(SettingsRow(GroupRow(first: true), AddAppStrings.SettingsType, AddAppStrings.SettingsTypeSub, KindDropdown(app)));
        group.Children.Add(SettingsRow(GroupRow(first: false), AddAppStrings.Background, AddAppStrings.SettingsBackgroundSub, bg));
        var card = Card();
        card.Margin = new Thickness(0, 0, 0, 16);
        card.Child = group;
        sections.Children.Add(card);
        sections.Children.Add(MergedSection(app));
        sections.Children.Add(AboutSection(app));
        // the danger zone stays pinned under the scroll, unless a confirmation is open and needs the room
        var danger = DangerSection(app);
        if (_settingsAsk != "") { danger.Margin = new Thickness(-18, 0, -18, -14); sections.Children.Add(danger); }
        else { Grid.SetRow(danger, 2); root.Children.Add(danger); }
        _settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = sections };
        var offset = _settingsOffset;
        _settingsScroll.Loaded += (_, _) => Dispatcher.BeginInvoke(() => _settingsScroll?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        Grid.SetRow(_settingsScroll, 1);
        root.Children.Add(_settingsScroll);

        root.Focusable = true;
        if (_clearStep > 0) root.Children.Add(ClearModal(app));
        else root.Loaded += (_, _) => root.Focus(); // so Esc works before anything is clicked
        // Esc leaves the screen; while a text box has focus it only cancels that edit, and an open modal answers No itself
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _clearStep > 0 || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            CloseSettings();
        };
        return root;
    }

    private UIElement SettingsHeader(TrackedEntry app)
    {
        var head = new Grid { Margin = new Thickness(18, 16, 10, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = MakeTile(app.DisplayName, app.ExePath, _trackedIcons.GetValueOrDefault(app.Id));
        tile.Margin = new Thickness(0, 0, 14, 0);
        tile.VerticalAlignment = VerticalAlignment.Center;

        var name = new TextBox { Text = app.DisplayName, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, VerticalContentAlignment = VerticalAlignment.Center };
        name.Background = Brushes.Transparent;
        name.BorderBrush = Brushes.Transparent; // reads as plain text at rest; the box shows on focus
        name.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBoxBase.CaretBrushProperty, "Brush.Text");
        name.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        name.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Title");
        System.Windows.Automation.AutomationProperties.SetName(name, TimeText.Str(AddAppStrings.RenameLabel));
        void Commit()
        {
            if (name.Text.Trim() == app.DisplayName) return;
            if (_catalogue.ApplyChange(AppChange.Rename(app.Id, name.Text)) != CatalogueOutcome.Done) { name.Text = app.DisplayName; return; }
            if (_settingsId == app.Id) RefreshSettings(); // not when the screen was closed and this is its focus loss
        }
        name.LostKeyboardFocus += (_, _) => Commit();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Keyboard.ClearFocus(); }
            else if (e.Key == Key.Escape) { e.Handled = true; name.Text = app.DisplayName; Keyboard.ClearFocus(); }
        };
        name.GotKeyboardFocus += (_, _) => name.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent");
        name.LostKeyboardFocus += (_, _) => name.BorderBrush = Brushes.Transparent;
        var meta = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        meta.Text = TimeText.Str(AddAppStrings.SettingsMeta, UsageText.HoursMinutes(app.Seconds), TimeText.Local(app.AddedUtc).ToString("MMM yyyy"));
        meta.Margin = new Thickness(0, 4, 0, 0);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(meta);

        var close = IconButton("M6,6 L18,18 M18,6 L6,18", "Brush.TextMuted", subtle: true); // flush: no box
        close.Click += (_, _) => CloseSettings();
        System.Windows.Automation.AutomationProperties.SetName(close, TimeText.Str(AddAppStrings.Close));
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(close, 2);
        head.Children.Add(tile);
        head.Children.Add(texts);
        head.Children.Add(close);
        return head;
    }

    /// <summary>The danger zone: full width, a hairline above it.</summary>
    private Border Section()
    {
        var s = new Border { Padding = new Thickness(18, 12, 18, 14), BorderThickness = new Thickness(0, 1, 0, 0) };
        s.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        return s;
    }

    /// <summary>A rounded panel that groups related rows.</summary>
    private Border Card()
    {
        var c = new Border { BorderThickness = new Thickness(1) };
        c.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        c.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        c.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        return c;
    }

    /// <summary>One row inside a Card; every row but the first has a hairline above it.</summary>
    private Border GroupRow(bool first)
    {
        var r = new Border { Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
        r.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        return r;
    }

    /// <summary>A section with a title and a muted line on the left and one control on the right.</summary>
    private UIElement SettingsRow(Border section, string titleKey, string subKey, UIElement control)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        var title = Text(StrKey(titleKey), "Double.FontSize.Body", "Brush.Text");
        title.FontWeight = FontWeights.SemiBold;
        var sub = Text(StrKey(subKey), "Double.FontSize.Small", "Brush.TextMuted");
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, 0, 0);
        texts.Children.Add(title);
        texts.Children.Add(sub);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(texts);
        g.Children.Add(control);
        section.Child = g;
        return section;
    }

    private static string StrKey(string key) => "Str." + char.ToUpperInvariant(key[0]) + key[1..];

    private UIElement Glyph(string pathData, string brushKey)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, brushKey);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = 18, Height = 18, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>The game type as a dropdown: Other, Game or Gacha (what Year > Wrapped counts the app as).</summary>
    private UIElement KindDropdown(TrackedEntry app)
    {
        // a boxed control; the border turns accent on hover or while open
        var face = new Border { Width = 132, Height = 36, Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        face.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        face.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        face.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        const string ink = "Brush.TextMuted";
        var row = new Grid { Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Glyph(KindIcon(app.Kind), ink);
        var label = Text(null, "Double.FontSize.Body", "Brush.Text");
        label.Text = KindName(app.Kind);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(8, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        var chevron = new System.Windows.Shapes.Path { Data = Geometry.Parse("M6,9 L12,15 L18,9"), StrokeThickness = 2, Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        chevron.SetResourceReference(Shape.StrokeProperty, ink);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(chevron);
        face.Child = row;
        System.Windows.Automation.AutomationProperties.SetName(face, TimeText.Str(AddAppStrings.Kind));

        var menu = new StackPanel();
        var popup = new Popup { PlacementTarget = face, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Width = 132 };
        foreach (var kind in new[] { AppKind.Other, AppKind.Game, AppKind.Gacha })
        {
            var item = new Border { Padding = new Thickness(10, 8, 10, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var picked = kind == app.Kind;
            var itemRow = new StackPanel { Orientation = Orientation.Horizontal };
            var itemLabel = Text(null, "Double.FontSize.Body", picked ? "Brush.Accent" : "Brush.Text");
            itemLabel.Text = KindName(kind);
            itemLabel.Margin = new Thickness(8, 0, 0, 0);
            itemLabel.VerticalAlignment = VerticalAlignment.Center;
            itemRow.Children.Add(Glyph(KindIcon(kind), picked ? "Brush.Accent" : "Brush.TextMuted"));
            itemRow.Children.Add(itemLabel);
            item.Child = itemRow;
            item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            var k = kind;
            item.MouseLeftButtonUp += (_, _) => { popup.IsOpen = false; if (k != app.Kind) SetKind(app.Id, k, RefreshSettings); };
            menu.Children.Add(item);
        }
        var frame = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 8), Child = menu };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        popup.Child = frame;
        // an open popup closes itself on the press, before the release lands here: a release right after a close is the toggle-off click
        var closedAt = DateTime.MinValue;
        popup.Closed += (_, _) => closedAt = DateTime.UtcNow;
        face.MouseLeftButtonUp += (_, _) =>
        {
            if (DateTime.UtcNow - closedAt < TimeSpan.FromMilliseconds(300)) return;
            popup.IsOpen = true;
        };
        void Lit()
        {
            var lit = face.IsMouseOver || popup.IsOpen;
            face.SetResourceReference(Border.BorderBrushProperty, lit ? "Brush.Accent" : "Brush.Border");
            label.SetResourceReference(TextBlock.ForegroundProperty, lit ? "Brush.Accent" : "Brush.Text");
            chevron.SetResourceReference(Shape.StrokeProperty, lit ? "Brush.Accent" : ink);
        }
        face.MouseEnter += (_, _) => Lit();
        face.MouseLeave += (_, _) => Lit();
        popup.Opened += (_, _) => Lit();
        popup.Closed += (_, _) => Lit();
        var host = new Grid();
        host.Children.Add(face);
        host.Children.Add(popup);
        return host;
    }

    private static string KindIcon(AppKind kind) => kind switch { AppKind.Gacha => IconSparkle, AppKind.Game => IconGamepad, _ => IconApps };

    private static string KindName(AppKind kind) =>
        TimeText.Str(kind switch { AppKind.Game => AddAppStrings.KindGame, AppKind.Gacha => AddAppStrings.KindGacha, _ => AddAppStrings.KindOther });

    private UIElement MergedSection(TrackedEntry app)
    {
        var s = new Border { Margin = new Thickness(0, 0, 0, 16) };
        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        label.Text = TimeText.Str(AddAppStrings.SettingsMerged, app.Merged.Count);
        label.VerticalAlignment = VerticalAlignment.Center;
        var merge = Button(AddAppStrings.Merge, BtnKind.Outline, () =>
        {
            _listOffset = _trackedScroll?.VerticalOffset ?? 0;
            _settingsReturn = app.Id;
            _settingsId = null;
            Content = _shell;
            _mergeFilter = "Tracked";
            OpenMergeScreen(app.Id);
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (app.Merged.Count > 0)
        {
            var change = Button(AddAppStrings.ChangeMain, BtnKind.Outline, () => { });
            change.Margin = new Thickness(0, 0, 8, 0);
            var popup = MainPicker(app, change);
            change.Click += (_, _) => popup.IsOpen = true;
            buttons.Children.Add(change);
            buttons.Children.Add(popup);
        }
        buttons.Children.Add(merge);
        Grid.SetColumn(buttons, 1);
        head.Children.Add(label);
        head.Children.Add(buttons);
        stack.Children.Add(head);
        if (_settingsAsk.StartsWith("main:") && long.TryParse(_settingsAsk.AsSpan(5), out var newMainId) && app.Merged.FirstOrDefault(m => m.Id == newMainId) is { } newMain)
        {
            var confirm = ConfirmBox(TimeText.Str(AddAppStrings.ChangeMainTitle, newMain.DisplayName), "Str.AddChangeMainBody", AddAppStrings.Cancel, AddAppStrings.ChangeMainConfirm, () =>
            {
                if (_catalogue.ApplyChange(AppChange.MakeMain(newMain.Id)) != CatalogueOutcome.Done) return;
                var keep = newMain.Id;
                _settingsAsk = "";
                _settingsId = keep;
                RefreshSettings();
            });
            if (confirm is FrameworkElement box) box.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(confirm);
        }

        if (app.Merged.Count == 0)
        {
            var none = Text("Str.AddSettingsMergedNone", "Double.FontSize.Small", "Brush.TextMuted");
            stack.Children.Add(none);
        }
        var list = new StackPanel();
        foreach (var m in app.Merged)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tile = MakeTile(m.DisplayName, m.ExePath, MergedIcon(m.Id));
            tile.Width = 28;
            tile.Height = 28;
            tile.Margin = new Thickness(0, 0, 10, 0);
            tile.VerticalAlignment = VerticalAlignment.Center;
            var name = Text(null, "Double.FontSize.Body", "Brush.Text");
            name.Text = m.DisplayName;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            var time = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
            time.Text = UsageText.HoursMinutes(m.Seconds);
            time.Margin = new Thickness(10, 0, 4, 0);
            time.VerticalAlignment = VerticalAlignment.Center;
            var un = IconButton("M6,6 L18,18 M18,6 L6,18", "Brush.TextMuted", subtle: true);
            un.Width = 28;
            un.Height = 28;
            un.ToolTip = "Unmerge";
            var id = m.Id;
            un.Click += (_, _) =>
            {
                if (_catalogue.ApplyChange(AppChange.Unmerge(id)) != CatalogueOutcome.Done) return;
                RefreshSettings();
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(time, 2);
            Grid.SetColumn(un, 3);
            row.Children.Add(tile);
            row.Children.Add(name);
            row.Children.Add(time);
            row.Children.Add(un);
            var last = m == app.Merged[^1];
            var line = new Border { Padding = new Thickness(14, 4, 8, 4), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = row };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
            list.Children.Add(line);
        }
        if (app.Merged.Count > 0)
        {
            var card = Card();
            card.Child = list;
            stack.Children.Add(card);
        }
        s.Child = stack;
        return s;
    }

    /// <summary>The list of merged apps under the Change main button; picking one opens the confirmation.</summary>
    private Popup MainPicker(TrackedEntry app, UIElement target)
    {
        var menu = new StackPanel();
        var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, MinWidth = 200, MaxWidth = 320 };
        foreach (var m in app.Merged)
        {
            var item = new Border { Padding = new Thickness(10, 8, 10, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var label = Text(null, "Double.FontSize.Body", "Brush.Text");
            label.Text = m.DisplayName;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            item.Child = label;
            item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            var id = m.Id;
            item.MouseLeftButtonUp += (_, _) => { popup.IsOpen = false; _settingsAsk = "main:" + id; RefreshSettings(); };
            menu.Children.Add(item);
        }
        var frame = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 8), Child = menu };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        popup.Child = frame;
        return popup;
    }

    private readonly Dictionary<long, byte[]?> _mergedIcons = new();

    /// <summary>The stored logo of a merged app, read once.</summary>
    private byte[]? MergedIcon(long id)
    {
        if (_mergedIcons.TryGetValue(id, out var png)) return png;
        return _mergedIcons[id] = _catalogue.StoredIcon(id);
    }

    private UIElement AboutSection(TrackedEntry app)
    {
        var s = new Border { Margin = new Thickness(0, 0, 0, 16) };
        var stack = new StackPanel();
        var label = Text("Str.AddSettingsAbout", "Double.FontSize.Small", "Brush.TextMuted");
        label.Margin = new Thickness(0, 0, 0, 8);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var path = Text(null, "Double.FontSize.Small", "Brush.TextSoft");
        path.Text = app.ExePath;
        path.FontFamily = new FontFamily("Consolas");
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.VerticalAlignment = VerticalAlignment.Center;
        path.ToolTip = app.ExePath;
        var pathBox = new Border { Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(10, 9, 10, 9), BorderThickness = new Thickness(1), Child = path };
        pathBox.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        pathBox.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        pathBox.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        var show = Button(AddAppStrings.ShowInFolder, BtnKind.Outline, () =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.ExePath}\"") { UseShellExecute = true }); } catch { /* explorer unavailable */ }
        });
        Grid.SetColumn(show, 1);
        row.Children.Add(pathBox);
        row.Children.Add(show);
        stack.Children.Add(label);
        stack.Children.Add(row);
        s.Child = stack;
        return s;
    }

    private Border DangerSection(TrackedEntry app)
    {
        var s = Section();
        var stack = new StackPanel();
        var label = Text("Str.AddSettingsDanger", "Double.FontSize.Small", "Brush.Danger");
        label.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(label);

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var stop = Button(AddAppStrings.StopTracking, BtnKind.Danger, () => { _settingsAsk = "stop"; RefreshSettings(); });
        var clear = Button(AddAppStrings.ClearHistory, BtnKind.Danger, () => { _clearStep = 1; RefreshSettings(); });
        clear.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(clear, 1);
        buttons.Children.Add(stop);
        buttons.Children.Add(clear);
        stack.Children.Add(buttons);

        if (_settingsAsk == "stop")
            stack.Children.Add(ConfirmBox(TimeText.Str(AddAppStrings.StopTitle, app.DisplayName), "Str.AddStopBody", AddAppStrings.KeepTracking, AddAppStrings.StopTracking, () => { StopTracking(app); CloseSettings(); }));

        var note = Text("Str.AddDangerNote", "Double.FontSize.Small", "Brush.TextMuted");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);
        s.Child = stack;
        return s;
    }

    /// <summary>
    /// Clearing history asks three times, each question louder than the last. Every one opens with keyboard focus on No,
    /// so Enter, Space, Esc or a click outside all back out; only a deliberate Tab or click on Yes moves forward.
    /// </summary>
    private UIElement ClearModal(TrackedEntry app)
    {
        var step = _clearStep;
        var (titleKey, bodyKey, yesKey) = step switch
        {
            1 => (AddAppStrings.ClearTitle, AddAppStrings.ClearBody, AddAppStrings.ClearYes),
            2 => (AddAppStrings.ClearTitle2, AddAppStrings.ClearBody2, AddAppStrings.ClearYes),
            _ => (AddAppStrings.ClearTitle3, AddAppStrings.ClearBody3, AddAppStrings.ClearConfirm),
        };
        void Dismiss() { _clearStep = 0; RefreshSettings(); }
        void Yes()
        {
            if (step < 3) { _clearStep = step + 1; RefreshSettings(); return; }
            if (_catalogue.ApplyChange(AppChange.ClearHistory(app.Id)) != CatalogueOutcome.Done) return;
            _clearStep = 0;
            RefreshSettings();
        }

        var stepText = Text(null, "Double.FontSize.Small", "Brush.Danger");
        stepText.Text = TimeText.Str(AddAppStrings.ClearStep, step);
        stepText.FontWeight = FontWeights.SemiBold;
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = TimeText.Str(titleKey, app.DisplayName);
        title.FontWeight = FontWeights.Bold;
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 6, 0, 0);
        var body = Text(StrKey(bodyKey), "Double.FontSize.Body", "Brush.TextSoft");
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 8, 0, 0);

        var no = Button(AddAppStrings.ClearNo, BtnKind.Primary, Dismiss);
        var yes = Button(yesKey, step < 3 ? BtnKind.Danger : BtnKind.DangerFill, Yes);
        foreach (var b in new[] { no, yes }) { b.Focusable = true; b.MinWidth = 96; b.FocusVisualStyle = null; }
        yes.Margin = new Thickness(8, 0, 0, 0);
        // the focused button wears the accent edge, since the flat template draws no focus ring of its own
        void Ring(Button b) { b.GotKeyboardFocus += (_, _) => b.SetResourceReference(Control.BorderBrushProperty, "Brush.Accent"); }
        Ring(no);
        Ring(yes);
        yes.GotKeyboardFocus += (_, _) => yes.BorderThickness = new Thickness(2);
        yes.LostKeyboardFocus += (_, _) => { yes.BorderThickness = new Thickness(1); yes.SetResourceReference(Control.BorderBrushProperty, step < 3 ? "Brush.Border" : "Brush.Danger"); };
        no.GotKeyboardFocus += (_, _) => no.BorderThickness = new Thickness(2);
        no.LostKeyboardFocus += (_, _) => no.BorderThickness = new Thickness(1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(no);
        buttons.Children.Add(yes);

        var panel = new StackPanel();
        panel.Children.Add(stepText);
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        var card = new Border { Margin = new Thickness(24), Padding = new Thickness(20), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");

        var scrim = new Grid { Focusable = false };
        scrim.SetResourceReference(Panel.BackgroundProperty, "Brush.Scrim");
        Grid.SetRowSpan(scrim, 3);
        scrim.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == scrim) { e.Handled = true; Dismiss(); } };
        scrim.Children.Add(card);
        scrim.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: e.Handled = true; Dismiss(); break;
                case Key.Tab or Key.Left or Key.Right: // only two stops, so any of these just swaps them
                    e.Handled = true;
                    (Keyboard.FocusedElement == no ? yes : no).Focus();
                    break;
            }
        };
        scrim.Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            no.Focus();
            // park the pointer on No too, so a reflex click backs out; the card moves between steps, so it is redone each time
            if (no.IsVisible && no.ActualWidth > 0)
            {
                var p = no.PointToScreen(new Point(no.ActualWidth / 2, no.ActualHeight / 2));
                SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
            }
        }, DispatcherPriority.Input);
        System.Windows.Automation.AutomationProperties.SetName(card, title.Text);
        return scrim;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private UIElement ConfirmBox(string title, string bodyStrKey, string cancelKey, string confirmKey, Action confirm)
    {
        var box = new Border { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        box.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        var panel = new StackPanel();
        var t = Text(null, "Double.FontSize.Body", "Brush.Text");
        t.Text = title;
        t.FontWeight = FontWeights.SemiBold;
        t.TextWrapping = TextWrapping.Wrap;
        var body = Text(bodyStrKey, "Double.FontSize.Small", "Brush.TextMuted");
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 4, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = Button(cancelKey, BtnKind.Outline, () => { _settingsAsk = ""; RefreshSettings(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(Button(confirmKey, BtnKind.DangerFill, confirm));
        panel.Children.Add(t);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        box.Child = panel;
        return box;
    }
}
