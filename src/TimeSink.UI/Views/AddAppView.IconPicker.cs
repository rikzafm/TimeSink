using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using TimeSink.Core.Apps;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>
/// The icon picker (decision 153): an Icon row on the app settings screen, shown only when more than one icon is found for
/// the app, opens a full-flyout grid of them. Tapping a tile previews it; Apply stores it as the app's icon.
/// </summary>
public sealed partial class AddAppView
{
    private const int PickedIconPx = 64;
    private readonly Dictionary<long, IReadOnlyList<IconChoice>> _iconChoices = new();

    /// <summary>The slot on the settings screen for the Icon row; it fills once the icons are found, when there is more than one.</summary>
    private UIElement IconRowSlot(TrackedEntry app)
    {
        var slot = new StackPanel();
        void Show(IReadOnlyList<IconChoice> found)
        {
            if (found.Count > 1 && _nav.State.SettingsId == app.Id && slot.Children.Count == 0) slot.Children.Add(IconRow(app, found));
        }
        if (_iconChoices.TryGetValue(app.Id, out var known)) Show(known);
        else
            Task.Run(() => IconChoices.Find(app.ExePath, Win32Desktop.Instance, PickedIconPx)).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                _iconChoices[app.Id] = t.Result;
                Show(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        return slot;
    }

    private UIElement IconRow(TrackedEntry app, IReadOnlyList<IconChoice> found)
    {
        var card = Card();
        card.Margin = new Thickness(0, 0, 0, 16);
        var row = GroupRow(first: true);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        var title = Text(null, "Double.FontSize.Body", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.IconTitle);
        title.FontWeight = FontWeights.SemiBold;
        var sub = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        sub.Text = TimeText.Str(AddAppStrings.IconSub, found.Count);
        sub.Margin = new Thickness(0, 2, 0, 0);
        texts.Children.Add(title);
        texts.Children.Add(sub);

        var face = new Border { Height = 36, Cursor = Cursors.Hand, BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 10, 0) };
        face.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        face.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        face.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(IconSquare(_trackedIcons.GetValueOrDefault(app.Id), 24, 6, selected: false));
        var change = Text(null, "Double.FontSize.Body", "Brush.Text");
        change.Text = TimeText.Str(AddAppStrings.IconChange);
        change.FontWeight = FontWeights.SemiBold;
        change.Margin = new Thickness(8, 0, 4, 0);
        change.VerticalAlignment = VerticalAlignment.Center;
        inner.Children.Add(change);
        inner.Children.Add(Glyph("M9,6 L15,12 L9,18", "Brush.TextMuted"));
        face.Child = inner;
        System.Windows.Automation.AutomationProperties.SetName(face, TimeText.Str(AddAppStrings.IconChange));
        face.MouseEnter += (_, _) => face.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
        face.MouseLeave += (_, _) => face.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        face.MouseLeftButtonUp += (_, _) => ShowIconPicker(app, found, []);
        Grid.SetColumn(face, 1);
        g.Children.Add(texts);
        g.Children.Add(face);
        row.Child = g;
        card.Child = row;
        return card;
    }

    /// <summary>A square of an icon (or of the plain surface colour when there is none), with a border that turns accent when selected.</summary>
    private UIElement IconSquare(byte[]? png, double size, double radius, bool selected)
    {
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(selected ? 2 : 1), ClipToBounds = true };
        box.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush.Accent" : "Brush.Border");
        box.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        if (IconService.Decode(png) is { } bmp) box.Child = new Image { Source = bmp, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        return box;
    }

    private void ShowIconPicker(TrackedEntry app, IReadOnlyList<IconChoice> found, IReadOnlyList<IconChoice> picked, int? selected = null)
    {
        var all = found.Concat(picked).ToList();
        var current = _catalogue.StoredIcon(app.Id) ?? _trackedIcons.GetValueOrDefault(app.Id);
        var inUse = all.FindIndex(c => current is not null && c.Png.AsSpan().SequenceEqual(current));
        Content = BuildIconPicker(app, found, picked, all, selected ?? Math.Max(inUse, 0), inUse);
    }

    private UIElement BuildIconPicker(TrackedEntry app, IReadOnlyList<IconChoice> found, IReadOnlyList<IconChoice> picked, List<IconChoice> all, int selected, int inUse)
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Back() => ReturnToSettings();
        var head = new Grid { Margin = new Thickness(10, 14, 10, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = IconButton("M15,6 L9,12 L15,18", "Brush.TextSoft");
        back.Click += (_, _) => Back();
        System.Windows.Automation.AutomationProperties.SetName(back, TimeText.Str(AddAppStrings.Cancel));
        var title = Text(null, "Double.FontSize.Title", "Brush.Text");
        title.Text = TimeText.Str(AddAppStrings.IconChooseTitle);
        title.FontWeight = FontWeights.Bold;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(6, 0, 0, 0);
        var close = IconButton("M6,6 L18,18 M18,6 L6,18", "Brush.TextMuted", subtle: true);
        close.Click += (_, _) => Back();
        System.Windows.Automation.AutomationProperties.SetName(close, TimeText.Str(AddAppStrings.Close));
        Grid.SetColumn(title, 1);
        Grid.SetColumn(close, 2);
        head.Children.Add(back);
        head.Children.Add(title);
        head.Children.Add(close);

        var body = new StackPanel { Margin = new Thickness(20, 8, 20, 12) };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var tile = IconSquare(_trackedIcons.GetValueOrDefault(app.Id), 30, 8, selected: false);
        ((FrameworkElement)tile).Margin = new Thickness(0, 0, 11, 0);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Text(null, "Double.FontSize.Body", "Brush.Text");
        name.Text = app.DisplayName;
        name.FontWeight = FontWeights.SemiBold;
        var count = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        count.Text = TimeText.Str(AddAppStrings.IconSub, all.Count);
        names.Children.Add(name);
        names.Children.Add(count);
        who.Children.Add(tile);
        who.Children.Add(names);
        body.Children.Add(who);
        var hint = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        hint.Text = TimeText.Str(AddAppStrings.IconChooseHint);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(hint);

        var grid = new UniformGrid { Columns = 3 };
        for (var i = 0; i < all.Count; i++)
        {
            var index = i;
            grid.Children.Add(IconCell(all[i], i == selected, i == inUse, () => ShowIconPicker(app, found, picked, index)));
        }
        grid.Children.Add(PickFileCell(app, found, picked));
        body.Children.Add(grid);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = body };
        Grid.SetRow(scroll, 1);

        var foot = new Grid { Margin = new Thickness(20, 10, 20, 10) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var changed = selected != inUse;
        var note = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        note.Text = changed ? TimeText.Str(AddAppStrings.IconPreviewing, IconLabel(all[selected])) : TimeText.Str(AddAppStrings.IconNoChange);
        note.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = Button(AddAppStrings.Cancel, BtnKind.Outline, Back);
        var apply = Button(AddAppStrings.IconApply, changed ? BtnKind.Primary : BtnKind.Ghost, () =>
        {
            if (!changed) return;
            var png = all[selected].Png;
            if (_catalogue.ApplyChange(AppChange.SetIcon(app.Id, png)) != CatalogueOutcome.Done) return;
            _trackedIcons[app.Id] = png;
            _chosenIcons = new HashSet<long>(_chosenIcons) { app.Id };
            Back();
        });
        apply.IsEnabled = changed;
        apply.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetColumn(buttons, 1);
        foot.Children.Add(note);
        foot.Children.Add(buttons);
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = foot };
        footer.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        Grid.SetRow(footer, 2);

        root.Children.Add(head);
        root.Children.Add(scroll);
        root.Children.Add(footer);
        root.Focusable = true;
        root.Loaded += (_, _) => root.Focus();
        root.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Back(); } };
        return root;
    }

    private UIElement IconCell(IconChoice choice, bool selected, bool inUse, Action pick)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 14), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var square = (FrameworkElement)IconSquare(choice.Png, 88, 12, selected);
        square.HorizontalAlignment = HorizontalAlignment.Left;
        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        holder.Children.Add(square);
        if (selected)
        {
            var badge = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) };
            badge.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            var tick = new System.Windows.Shapes.Path { Data = Geometry.Parse("M5,12.5 L9.5,17 L19,7.5"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            tick.SetResourceReference(Shape.StrokeProperty, "Brush.OnAccent");
            badge.Child = new Viewbox { Width = 12, Height = 12, Child = new Canvas { Width = 24, Height = 24, Children = { tick } } };
            holder.Children.Add(badge);
        }
        cell.Children.Add(holder);
        var label = Text(null, "Double.FontSize.Small", "Brush.Text");
        label.Text = IconLabel(choice);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 6, 0, 0);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        detail.Text = choice.Detail;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        cell.Children.Add(label);
        cell.Children.Add(detail);
        if (inUse)
        {
            var tag = Text(null, "Double.FontSize.Small", "Brush.Accent");
            tag.Text = TimeText.Str(AddAppStrings.IconInUse);
            tag.FontWeight = FontWeights.Bold;
            cell.Children.Add(tag);
        }
        System.Windows.Automation.AutomationProperties.SetName(cell, IconLabel(choice));
        cell.MouseLeftButtonUp += (_, _) => pick();
        return cell;
    }

    private UIElement PickFileCell(TrackedEntry app, IReadOnlyList<IconChoice> found, IReadOnlyList<IconChoice> picked)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 14), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var frame = new Grid { Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Left };
        var dashes = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1, StrokeDashArray = [4, 3], Fill = Brushes.Transparent };
        dashes.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
        frame.Children.Add(dashes);
        var plus = (FrameworkElement)Glyph("M12,5 L12,19 M5,12 L19,12", "Brush.TextMuted");
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        frame.Children.Add(plus);
        cell.Children.Add(frame);
        var label = Text(null, "Double.FontSize.Small", "Brush.Text");
        label.Text = TimeText.Str(AddAppStrings.IconPickFile);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 6, 0, 0);
        var sub = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        sub.Text = TimeText.Str(AddAppStrings.IconPickFileSub);
        cell.Children.Add(label);
        cell.Children.Add(sub);
        System.Windows.Automation.AutomationProperties.SetName(cell, TimeText.Str(AddAppStrings.IconPickFile));
        cell.MouseLeftButtonUp += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico" };
            var window = Window.GetWindow(this) as FlyoutWindow;
            if (window is not null) window.KeepOpen = true; // the dialog deactivates the flyout, which would close it
            bool ok;
            try { ok = dlg.ShowDialog(window) == true; }
            finally { if (window is not null) { window.KeepOpen = false; window.Activate(); } }
            if (!ok || IconChoices.FromFile(dlg.FileName, Win32Desktop.Instance, PickedIconPx) is not { } choice) return;
            var next = picked.Where(c => !c.Png.AsSpan().SequenceEqual(choice.Png)).Append(choice).ToList();
            ShowIconPicker(app, found, next, found.Count + next.Count - 1);
        };
        return cell;
    }

    private static string IconLabel(IconChoice c) => TimeText.Str(c.Kind switch
    {
        IconChoiceKind.App => AddAppStrings.IconSrcApp,
        IconChoiceKind.Exe => AddAppStrings.IconSrcExe,
        IconChoiceKind.SteamCover => AddAppStrings.IconSrcCover,
        IconChoiceKind.SteamHeader => AddAppStrings.IconSrcHeader,
        IconChoiceKind.SteamLogo => AddAppStrings.IconSrcLogo,
        IconChoiceKind.SteamArt => AddAppStrings.IconSrcArt,
        _ => AddAppStrings.IconSrcFile,
    });
}
