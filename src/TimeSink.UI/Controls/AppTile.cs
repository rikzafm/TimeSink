using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimeSink.Core.Apps;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Controls;

/// <summary>Where an App tile is drawn. Each style keeps the size, corners, font and icon treatment of that place.</summary>
internal enum TileStyle { TodayRow, WeekRow, Mini, Table, Header, CandidateRow, Untracked }

/// <summary>
/// The App tile (see the glossary): the app's icon, or its first letter on its Palette slot colour when it has none.
/// Callers say which app and where the tile is drawn; the letter, the slot and the icon-or-letter choice are made here.
/// </summary>
internal static class AppTile
{
    private enum Frame { Outlined, Filled, Candidate }

    private sealed record Spec(
        Frame Frame, string? SizeKey, double SizePx, VerticalAlignment? Align, string? RadiusKey, double RadiusPx,
        string? FontKey, double FontPx, FontWeight Weight,
        string? LetterBrushKey = null, Color? LetterColor = null, bool Boxed = false, bool Fant = false);

    private static readonly Dictionary<TileStyle, Spec> Specs = new()
    {
        [TileStyle.TodayRow] = new(Frame.Outlined, "Double.Row.Tile", 0, VerticalAlignment.Top, "CornerRadius.Medium", 0, "Double.FontSize.Title", 0, FontWeights.SemiBold),
        [TileStyle.WeekRow] = new(Frame.Outlined, "Double.Row.TileSmall", 0, null, "CornerRadius.Medium", 0, "Double.FontSize.Body", 0, FontWeights.SemiBold),
        [TileStyle.Mini] = new(Frame.Outlined, null, 26, VerticalAlignment.Top, "CornerRadius.Small", 0, "Double.FontSize.Body", 0, FontWeights.Bold),
        [TileStyle.Table] = new(Frame.Filled, null, 32, VerticalAlignment.Center, null, 32 / 4.0, null, 32 * 0.42, FontWeights.Bold,
            LetterColor: Color.FromRgb(0x14, 0x18, 0x1A)),
        [TileStyle.Header] = new(Frame.Filled, null, 40, VerticalAlignment.Center, "CornerRadius.Medium", 0, null, 20, FontWeights.Bold,
            LetterBrushKey: "Brush.Surface"),
        [TileStyle.CandidateRow] = new(Frame.Candidate, "Double.Row.TileLarge", 0, null, "CornerRadius.Medium", 0, "Double.FontSize.Title", 0, FontWeights.Bold,
            Fant: true),
        [TileStyle.Untracked] = new(Frame.Candidate, null, 26, VerticalAlignment.Center, "CornerRadius.Medium", 0, "Double.FontSize.Body", 0, FontWeights.Bold,
            Boxed: true),
    };

    /// <summary>The tile of a tracked app: its cached icon, else its letter on the colour <see cref="ThemeService.AppSlot"/> gives it.</summary>
    public static Border Tracked(ThemeService themes, long appId, string name, TileStyle style)
    {
        var spec = Specs[style];
        var tile = Frame0(spec);
        if (AppIcons.Get(appId) is { } bmp) Icon(tile, spec, bmp);
        else Letter(tile, spec, name, themes.AppBrushKey(appId));
        return tile;
    }

    /// <summary>The tile of an app that has no id yet (a Candidate): its PNG, else its letter on the colour its exe path hashes to.</summary>
    public static Border Candidate(ThemeService themes, string exePath, string name, byte[]? png, TileStyle style)
    {
        var tile = Frame0(Specs[style]);
        Fill(tile, themes, exePath, name, png, style);
        return tile;
    }

    /// <summary>Redraws a tile made by <see cref="Candidate"/> when its icon arrives or is upgraded.</summary>
    public static void Fill(Border tile, ThemeService themes, string exePath, string name, byte[]? png, TileStyle style)
    {
        var spec = Specs[style];
        if (IconService.Decode(png) is { } bmp) Icon(tile, spec, bmp);
        else Letter(tile, spec, name, "Brush.App" + LetterTile.PaletteIndex(LetterTile.KeyFor(exePath), Math.Max(1, themes.Current.Apps.Count)));
    }

    private static Border Frame0(Spec s)
    {
        var tile = new Border();
        if (s.SizeKey is { } key)
        {
            tile.SetResourceReference(FrameworkElement.WidthProperty, key);
            tile.SetResourceReference(FrameworkElement.HeightProperty, key);
        }
        else { tile.Width = s.SizePx; tile.Height = s.SizePx; }
        if (s.Align is { } a) tile.VerticalAlignment = a;
        if (s.RadiusKey is { } r) tile.SetResourceReference(Border.CornerRadiusProperty, r);
        else tile.CornerRadius = new CornerRadius(s.RadiusPx);
        return tile;
    }

    /// <summary>The icon: bare on the panel with no box, or (Boxed) inside its outline with a little padding.</summary>
    private static void Icon(Border tile, Spec s, BitmapSource bmp)
    {
        tile.ClearValue(Border.BackgroundProperty);
        if (s.Boxed)
        {
            tile.BorderThickness = new Thickness(1);
            tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            tile.Padding = new Thickness(2);
        }
        else
        {
            tile.ClearValue(Border.BorderBrushProperty);
            tile.BorderThickness = new Thickness(0);
            tile.Padding = new Thickness(0);
        }
        var img = new Image { Source = bmp, Stretch = Stretch.Uniform };
        if (s.Fant) RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
        tile.Child = img;
    }

    private static void Letter(Border tile, Spec s, string name, string appBrushKey)
    {
        tile.Padding = new Thickness(0);
        switch (s.Frame)
        {
            case Frame.Outlined:
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                tile.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
                break;
            case Frame.Filled:
                tile.SetResourceReference(Border.BackgroundProperty, appBrushKey);
                break;
            case Frame.Candidate:
                tile.BorderThickness = new Thickness(1);
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                break;
        }

        var letter = new TextBlock
        {
            Text = LetterTile.Letter(name), FontWeight = s.Weight,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (s.LetterColor is { } c) letter.Foreground = new SolidColorBrush(c);
        else letter.SetResourceReference(TextBlock.ForegroundProperty, s.LetterBrushKey ?? appBrushKey);
        letter.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        if (s.FontKey is { } f) letter.SetResourceReference(TextBlock.FontSizeProperty, f);
        else letter.FontSize = s.FontPx;
        tile.Child = letter;
    }
}
