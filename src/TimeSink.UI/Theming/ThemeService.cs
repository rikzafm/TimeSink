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

    /// <summary>Thread-safe: reads and resolves a theme without touching WPF.</summary>
    public static Theme Load(string themeId) => Loader.Load(themeId);

    public Theme Current { get; private set; } = null!;

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
    public void ApplyPreferences(string? themeSetting, string? accent)
        => Apply(Accents.Apply(Load(IdFor(themeSetting, SystemIsLight())), accent));

    public void Apply(Theme theme)
    {
        var dict = Build(theme);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
        Current = theme;
        Applied?.Invoke();
    }

    private static ResourceDictionary Build(Theme t)
    {
        var d = new ResourceDictionary();
        foreach (var (key, hex) in t.Colors) d["Brush." + Pascal(key)] = Brush(hex);
        for (var i = 0; i < t.Heat.Count; i++) d[$"Brush.Heat{i}"] = Brush(t.Heat[i]);
        for (var i = 0; i < t.Apps.Count; i++) d[$"Brush.App{i}"] = Brush(t.Apps[i]);

        d["CornerRadius.Small"] = new CornerRadius(t.Radius[0]);
        d["CornerRadius.Medium"] = new CornerRadius(t.Radius[1]);
        d["CornerRadius.Large"] = new CornerRadius(t.Radius[2]);
        d["Thickness.Border"] = new Thickness(t.Border);

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
