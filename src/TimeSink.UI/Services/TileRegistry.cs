using System.Windows.Controls;
using TimeSink.UI.Controls;
using TimeSink.UI.Theming;

namespace TimeSink.UI.Services;

/// <summary>
/// The tiles on screen that show a letter while their App icon is still being extracted, by exe path. When the icon
/// arrives, <see cref="Fill"/> swaps it into every tile registered for that path. UI thread only.
/// </summary>
internal sealed class TileRegistry(ThemeService themes, TileStyle style)
{
    private readonly Dictionary<string, List<(Border Tile, string Name)>> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string exePath, Border tile, string name)
    {
        if (!_byPath.TryGetValue(exePath, out var list)) _byPath[exePath] = list = [];
        list.Add((tile, name));
    }

    public void Fill(string exePath, byte[] png)
    {
        if (!_byPath.TryGetValue(exePath, out var list)) return;
        foreach (var (tile, name) in list.ToList()) AppTile.Fill(tile, themes, exePath, name, png, style);
    }

    public void Clear() => _byPath.Clear();
}
