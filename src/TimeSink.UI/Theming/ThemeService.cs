using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TimeSink.Core.Theming;

namespace TimeSink.UI.Theming;

/// <summary>
/// Turns a resolved <see cref="Theme"/> into one ResourceDictionary (Brush.*, CornerRadius.*, Thickness.*, FontFamily.*,
/// Double.*, Str.*) and swaps it into the application resources. Views use DynamicResource, so a swap is instant.
/// </summary>
public sealed class ThemeService
{
    private static readonly ThemeLoader Loader = new(new FolderThemeSource(Path.Combine(AppContext.BaseDirectory, "themes")));
    private ResourceDictionary? _current;

    /// <summary>List density (ui.compact): re-applied with the next theme build; <see cref="ApplyPreferences"/> sets it.</summary>
    public bool Compact { get; set; }

    /// <summary>Thread-safe: reads and resolves a theme without touching WPF.</summary>
    public static Theme Load(string themeId) => Loader.Load(themeId);

    public Theme Current { get; private set; } = null!;

    /// <summary>The Palette slot of an app under the current theme.</summary>
    public int AppSlot(long appId) => AppPalette.Slot(appId, Current.Apps.Count);

    /// <summary>The Brush.App* resource key an app is coloured with, the same on every tab.</summary>
    public string AppBrushKey(long appId) => "Brush.App" + AppSlot(appId);

    /// <summary>Raised after a theme swap so views that compose text in code can redraw.</summary>
    public event Action? Applied;

    public static string IdFor(string? setting, bool systemIsLight) => setting switch
    {
        "Light" => "pulse-light",
        "Dark" => "pulse-dark",
        _ => systemIsLight ? "pulse-light" : "pulse-dark",
    };

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public void Apply(string themeId) => Apply(Load(themeId));

    /// <summary>Applies the ui.theme (Dark/Light/System) and ui.accent (Lime/Blue/Pink) settings instantly.</summary>
    public void ApplyPreferences(string? themeSetting, string? accent, bool compact)
    {
        Compact = compact;
        Apply(Accents.Apply(Load(IdFor(themeSetting, SystemIsLight())), accent));
    }

    public void Apply(Theme theme)
    {
        var dict = Build(theme, Compact);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
        Current = theme;
        Applied?.Invoke();
    }

    private static ResourceDictionary Build(Theme t, bool compact)
    {
        var d = new ResourceDictionary();
        foreach (var (key, hex) in t.Colors) d["Brush." + Pascal(key)] = Brush(hex);
        for (var i = 0; i < t.Heat.Count; i++) d[$"Brush.Heat{i}"] = Brush(t.Heat[i]);
        for (var i = 0; i < t.Apps.Count; i++) d[$"Brush.App{i}"] = Brush(t.Apps[i]);

        d["CornerRadius.Small"] = new CornerRadius(t.Radius[0]);
        d["CornerRadius.Medium"] = new CornerRadius(t.Radius[1]);
        d["CornerRadius.Large"] = new CornerRadius(t.Radius[2]);
        d["CornerRadius.Tiny"] = new CornerRadius(Math.Max(1, t.Radius[0] / 4)); // bar ends, legend swatches, dots
        d["Double.Radius.Tiny"] = Math.Max(1, t.Radius[0] / 4);
        d["Double.Radius.Small"] = t.Radius[0];
        d["Double.Radius.Medium"] = t.Radius[1];
        d["Double.Radius.Large"] = t.Radius[2];
        d["Thickness.Border"] = new Thickness(t.Border);

        // Row density (ui.compact): lists size their rows from these.
        var row = Density.For(compact);
        d["Double.Row.Tile"] = row.Tile;
        d["Double.Row.TileSmall"] = row.TileSmall;
        d["Double.Row.TileLarge"] = row.TileLarge;
        d["Double.Row.Button"] = row.Button;
        d["Double.Row.Table"] = row.TableRow;
        d["Double.Row.TableRetired"] = row.TableRetiredRow;
        d["Double.Row.TableHead"] = row.TableHead;
        d["Double.Row.TableTotals"] = row.TableTotals;
        d["Thickness.RowCard"] = new Thickness(8, row.CardPadY, 8, row.CardPadY);
        d["Thickness.RowList"] = new Thickness(0, row.ListGapY, 0, row.ListGapY);
        d["Thickness.RowTile"] = new Thickness(8, row.AddTileGapY, 12, row.AddTileGapY);
        d["Thickness.RowTileTight"] = new Thickness(8, row.AddTileGapYTight, 12, row.AddTileGapYTight);

        // Opacity tokens (decision 114): fixed ratios shared by every theme.
        d["Double.Size.Dot"] = 7.0;     // status dot
        d["Double.Size.Swatch"] = 8.0;  // legend / list colour swatch
        d["Double.Opacity.Retired"] = 0.7;
        d["Double.Opacity.Wash"] = 0.14;
        d["Double.Opacity.Disabled"] = 0.35;

        foreach (var (key, name) in t.Fonts) d["FontFamily." + Pascal(key)] = new FontFamily(name);
        foreach (var (key, size) in t.Sizes) d["Double.FontSize." + Pascal(key)] = size;

        foreach (var (key, text) in t.Strings.Entries) d["Str." + Pascal(key)] = text;
        return d;
    }

    private static SolidColorBrush Brush(string hex)
    {
        // Validated by ThemeLoader as #RRGGBB or #RRGGBBAA; parsed by hand (ColorConverter is slow on a cold start).
        var rgb = uint.Parse(hex.AsSpan(1, 6), System.Globalization.NumberStyles.AllowHexSpecifier);
        var a = hex.Length == 9 ? byte.Parse(hex.AsSpan(7, 2), System.Globalization.NumberStyles.AllowHexSpecifier) : (byte)255;
        var b = new SolidColorBrush(Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    private static string Pascal(string key) => char.ToUpperInvariant(key[0]) + key[1..];
}
