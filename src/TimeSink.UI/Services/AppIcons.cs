using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>
/// App icons for the usage rows (Today, Week, Month). Decoded bitmaps are cached per app id; <see cref="Load"/> reads the
/// stored PNG from the <c>icons</c> table off-thread, extracting and storing one when the app has none yet, then raises
/// <see cref="Updated"/> so views redraw. Rows without an icon keep their letter tile.
/// </summary>
internal static class AppIcons
{
    private static readonly Dictionary<long, BitmapSource> Cache = new();
    private static readonly object SourceGate = new();
    private static AppIconSource? _source;
    private static string? _sourceDb;

    public static event Action? Updated;

    /// <summary>The one <see cref="AppIconSource"/> of this run for <paramref name="dbPath"/>, shared with the Add app catalogue.</summary>
    public static AppIconSource Source(string dbPath)
    {
        lock (SourceGate)
        {
            if (_source is null || _sourceDb != dbPath)
            {
                _source = new AppIconSource(dbPath, Win32Desktop.Instance);
                _sourceDb = dbPath;
            }
            return _source;
        }
    }

    /// <summary>The cached bitmap, or null when the app has no icon (yet).</summary>
    public static BitmapSource? Get(long appId) => Cache.GetValueOrDefault(appId);

    /// <summary>A bare square icon for list rows, or null when none is cached (the caller keeps its colour swatch).</summary>
    public static Image? Icon(long appId, double size) =>
        Cache.TryGetValue(appId, out var bmp)
            ? new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }
            : null;

    /// <summary>
    /// Fetches icons for ids not decoded yet (UI thread). <see cref="AppIconSource"/> resolves each id once per run, so an
    /// app without an icon costs a cheap lookup per redraw and raises no <see cref="Updated"/>, so a redraw cannot loop.
    /// </summary>
    public static async void Load(string dbPath, IEnumerable<long> appIds)
    {
        var want = appIds.Distinct().Where(id => !Cache.ContainsKey(id)).ToList();
        if (want.Count == 0) return;
        var source = Source(dbPath);
        var found = await Task.Run(() => source.ForApps(want));
        var changed = false;
        foreach (var (id, png) in found)
            if (IconService.Decode(png) is { } bmp) { Cache[id] = bmp; changed = true; }
        if (changed) Updated?.Invoke();
    }
}
