using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
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
    private static readonly HashSet<long> Tried = new();

    public static event Action? Updated;

    /// <summary>Puts the app's icon in <paramref name="tile"/> at the full tile size, with no box around it; false means the caller shows its letter tile.</summary>
    public static bool TryFill(Border tile, long appId)
    {
        if (!Cache.TryGetValue(appId, out var bmp)) return false;
        tile.ClearValue(Border.BackgroundProperty);
        tile.ClearValue(Border.BorderBrushProperty);
        tile.BorderThickness = new Thickness(0);
        tile.Padding = new Thickness(0);
        tile.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
        return true;
    }

    /// <summary>The cached bitmap, or null when the app has no icon (yet).</summary>
    public static BitmapSource? Get(long appId) => Cache.GetValueOrDefault(appId);

    /// <summary>A bare square icon for list rows, or null when none is cached (the caller keeps its colour swatch).</summary>
    public static Image? Icon(long appId, double size) =>
        Cache.TryGetValue(appId, out var bmp)
            ? new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }
            : null;

    /// <summary>Fetches icons for ids not seen yet (UI thread). Each id is tried once per run, so a redraw cannot loop.</summary>
    public static async void Load(string dbPath, IEnumerable<long> appIds)
    {
        var want = appIds.Distinct().Where(id => !Cache.ContainsKey(id) && Tried.Add(id)).ToList();
        if (want.Count == 0) return;
        var found = await Task.Run(() =>
        {
            var pngs = new Dictionary<long, byte[]>();
            try
            {
                using var lib = AppLibrary.Open(dbPath);
                IReadOnlyList<LibraryApp>? apps = null;
                foreach (var id in want)
                {
                    var png = lib.GetIcon(id);
                    if (png is null)
                    {
                        apps ??= lib.Library();
                        var app = apps.FirstOrDefault(a => a.Id == id);
                        if (app is not null && IconService.ExtractPng(app.ExePath, null, SteamLibrary.ArtForExe(app.ExePath)) is { } extracted)
                        {
                            lib.PutIcon(id, extracted);
                            png = extracted;
                        }
                    }
                    if (png is not null) pngs[id] = png;
                }
            }
            catch (Exception ex) when (ex is SqliteException or IOException) { } // no DB yet: letters stay
            return pngs;
        });
        var changed = false;
        foreach (var (id, png) in found)
            if (IconService.Decode(png) is { } bmp) { Cache[id] = bmp; changed = true; }
        if (changed) Updated?.Invoke();
    }
}
