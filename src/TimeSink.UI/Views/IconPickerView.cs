using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimeSink.Core.Icons;
using TimeSink.Core.Ipc;
using TimeSink.Core.Settings;

namespace TimeSink.UI.Views;

/// <summary>
/// The "App icon" page: a preview card, a style filter and a grid of every icon. Tapping a tile only previews it;
/// Apply saves <c>ui.icon</c> and tells the Agent to swap the tray icon, Reset puts the default back in the preview.
/// </summary>
public sealed class IconPickerView : UserControl
{
    private static readonly ControlTemplate Flat = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
        "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' " +
        "CornerRadius='{DynamicResource CornerRadius.Small}' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}'/>" +
        "</Border></ControlTemplate>");

    private static readonly Dictionary<(string, int), BitmapSource> Frames = new();

    private readonly string _dbPath, _pipeName;
    private readonly Action<string> _applied;
    private readonly SettingRow _row = SettingsCatalogue.Find("appIcon");
    private string _current, _preview;
    private IconStyle _style;

    private readonly Image _big = new() { Width = 72, Height = 72 };
    private readonly TextBlock _name = new(), _hint = new();
    private readonly Border _badge = new();
    private readonly TextBlock _badgeText = new();
    private readonly StackPanel _darkRow = new() { Orientation = Orientation.Horizontal }, _lightRow = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 8) };
    private readonly WrapPanel _grid = new();
    private readonly Button _apply = new() { Template = Flat, Padding = new Thickness(14, 9, 14, 9), Cursor = Cursors.Hand, Focusable = false, };

    public IconPickerView(string dbPath, string pipeName, Action<string> applied)
    {
        _dbPath = dbPath;
        _pipeName = pipeName;
        _applied = applied;
        _current = _preview = SettingsCatalogue.Read(dbPath, _row);
        _style = IconCatalogue.Find(_current).Style;
        SetResourceReference(BackgroundProperty, "Brush.Surface");

        var root = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };

        root.Children.Add(Text(SettingsStrings.IconTitle, "Double.FontSize.Title", "Brush.Text", FontWeights.SemiBold));
        root.Children.Add(Text(SettingsStrings.IconSubtitle, "Double.FontSize.Small", "Brush.TextMuted"));

        root.Children.Add(BuildPreviewCard());
        root.Children.Add(_chips);
        root.Children.Add(_grid);

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var reset = new Button { Template = Flat, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Focusable = false, };
        reset.SetResourceReference(ContentControl.ContentProperty, "Str." + Pascal(SettingsStrings.IconReset));
        Skin(reset, "Brush.Surface", "Brush.Text", "Brush.Border");
        reset.Click += (_, _) => { _preview = IconCatalogue.DefaultId; _style = IconCatalogue.Find(_preview).Style; Refresh(); };
        _apply.SetResourceReference(ContentControl.ContentProperty, "Str." + Pascal(SettingsStrings.IconApply));
        Skin(_apply, "Brush.Accent", "Brush.OnAccent", "Brush.Accent");
        _apply.Click += (_, _) => Apply();
        Grid.SetColumn(_apply, 1);
        buttons.Children.Add(reset);
        buttons.Children.Add(_apply);
        root.Children.Add(buttons);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Refresh();
    }

    private Border BuildPreviewCard()
    {
        var card = new Border { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_big);

        var info = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        _name.FontWeight = FontWeights.SemiBold;
        _name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _name.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _badge.Margin = new Thickness(8, 0, 0, 0);
        _badge.Padding = new Thickness(7, 1, 7, 1);
        _badge.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        _badge.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        _badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
        _badgeText.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        _badgeText.FontWeight = FontWeights.SemiBold;
        _badge.Child = _badgeText;
        head.Children.Add(_name);
        head.Children.Add(_badge);
        info.Children.Add(head);

        // The two strips simulate a dark and a light taskbar, so these colours are deliberately theme-independent.
        var darkBox = new Border { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0), Child = _darkRow };
        var lightBox = new Border { Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0), Child = _lightRow };
        darkBox.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        lightBox.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        info.Children.Add(darkBox);
        info.Children.Add(lightBox);
        row.Children.Add(info);
        card.Child = row;
        return card;
    }

    private void Refresh()
    {
        var entry = IconCatalogue.Find(_preview);
        _big.Source = Frame(_preview, 256);
        RenderOptions.SetBitmapScalingMode(_big, BitmapScalingMode.HighQuality);
        _name.Text = entry.Name;
        _badgeText.Text = entry.Style.ToString();
        foreach (var strip in new[] { _darkRow, _lightRow })
        {
            strip.Children.Clear();
            foreach (var size in new[] { 16, 24, 32 })
                strip.Children.Add(Thumb(_preview, size, new Thickness(0, 0, 10, 0)));
        }

        _chips.Children.Clear();
        foreach (var style in Enum.GetValues<IconStyle>())
        {
            var chip = new RadioButton { Content = style.ToString(), GroupName = "iconStyle", IsChecked = style == _style, Style = (Style)FindResource("SegButton") };
            var s = style;
            chip.Checked += (_, _) => { _style = s; RefreshGrid(); };
            _chips.Children.Add(chip);
        }
        RefreshGrid();

        _apply.IsEnabled = _preview != _current;
        if (_apply.IsEnabled) _apply.ClearValue(OpacityProperty);
        else _apply.SetResourceReference(OpacityProperty, "Double.Opacity.Disabled");
    }

    private void RefreshGrid()
    {
        _grid.Children.Clear();
        foreach (var icon in IconCatalogue.InStyle(_style)) _grid.Children.Add(Tile(icon));
    }

    private FrameworkElement Tile(IconEntry icon)
    {
        var selected = icon.Id == _preview;
        var holder = new Grid { Width = 72, Height = 72 };
        var box = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(5) };
        box.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
        box.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush.Accent" : "Brush.Border");
        box.Child = Thumb(icon.Id, 48, new Thickness(0));
        holder.Children.Add(box);
        if (icon.Id == _current)
        {
            var check = new Border { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0) };
            check.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            check.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");
            var tick = new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tick.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
            check.Child = tick;
            holder.Children.Add(check);
        }

        var label = new TextBlock { Text = icon.Name, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Brush.Accent" : "Brush.TextMuted");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");

        var cell = new StackPanel { Width = 82, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        cell.Children.Add(holder);
        cell.Children.Add(label);
        cell.MouseLeftButtonUp += (_, _) => { _preview = icon.Id; Refresh(); };
        return cell;
    }

    private void Apply()
    {
        var id = _preview;
        var (path, pipe, row) = (_dbPath, _pipeName, _row);
        _apply.IsEnabled = false;
        // Show it as applied only once it is saved; a failed write leaves the picker as it was.
        Task.Run(async () =>
        {
            SettingsCatalogue.Write(path, row, id);
            await IpcClient.SendAsync(pipe, new SetIconMessage(id)).ConfigureAwait(false);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) { _current = id; _applied(id); }
            Refresh();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>A small image of an icon (also used for the Settings row thumbnail).</summary>
    public static Image Thumb(string id, int size, Thickness margin)
    {
        var img = new Image { Width = size, Height = size, Margin = margin, Source = Frame(id, size) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private static BitmapSource Frame(string id, int size)
    {
        var key = (id, size);
        if (Frames.TryGetValue(key, out var cached)) return cached;
        using var ms = new MemoryStream(IconCatalogue.ReadFrame(id, size));
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return Frames[key] = frame;
    }

    /// <summary>Theme string resources are registered with an upper-case first letter (ThemeService).</summary>
    private static string Pascal(string key) => char.ToUpperInvariant(key[0]) + key[1..];

    private TextBlock Text(string key, string size, string brush, FontWeight? weight = null)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        t.SetResourceReference(TextBlock.TextProperty, "Str." + Pascal(key));
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        return t;
    }

    private static void Skin(Button b, string background, string foreground, string border)
    {
        b.SetResourceReference(Control.BackgroundProperty, background);
        b.SetResourceReference(Control.ForegroundProperty, foreground);
        b.SetResourceReference(Control.BorderBrushProperty, border);
        b.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        b.SetResourceReference(Control.FontSizeProperty, "Double.FontSize.Body");
    }
}
