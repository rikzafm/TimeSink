using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TimeSink.UI.Services;

/// <summary>
/// Icons for the UI (implementation.md 8.5): extraction through <c>IShellItemImageFactory</c> (64 px) to PNG bytes,
/// which <c>AppLibrary</c> stores in the <c>icons</c> table; decoding PNG bytes to a frozen bitmap. Callers fall back
/// to the letter tile when either step yields null. Never call from the UI thread (extraction is slow).
/// </summary>
internal static class IconService
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out nint hbitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint h);

    private static Guid _iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    /// <summary>
    /// PNG bytes (<paramref name="size"/> px, 64 by default) of the app's icon, from its Start tile when an AUMID is known,
    /// else from the exe at full size; <paramref name="imageFile"/> (Steam's cached art, mostly small) only stands in when
    /// neither can be read, for example an exe that was uninstalled. Null on failure.
    /// </summary>
    public static byte[]? ExtractPng(string exePath, string? aumid, string? imageFile = null, int size = 64)
    {
        if (aumid is not null && Extract(@"shell:AppsFolder\" + aumid, size) is { } tile) return tile;
        if (Extract(exePath, size) is { } exe) return exe;
        return imageFile is not null ? FromImage(imageFile, size) : null;
    }

    /// <summary>An image file (Steam's cached art) centre-cropped to a square and scaled to <paramref name="size"/> px; null when unreadable.</summary>
    private static byte[]? FromImage(string path, int size)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            var side = Math.Min(bmp.PixelWidth, bmp.PixelHeight);
            var crop = new CroppedBitmap(bmp, new Int32Rect((bmp.PixelWidth - side) / 2, (bmp.PixelHeight - side) / 2, side, side));
            var scaled = new TransformedBitmap(crop, new System.Windows.Media.ScaleTransform((double)size / side, (double)size / side));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static byte[]? Extract(string parsingName, int size)
    {
        nint hbmp = 0;
        IShellItemImageFactory? factory = null;
        try
        {
            if (SHCreateItemFromParsingName(parsingName, 0, ref _iid, out factory) != 0) return null;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out hbmp) != 0 || hbmp == 0) return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
        finally
        {
            if (hbmp != 0) DeleteObject(hbmp);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    // Decoded once per byte array: list views rebuild their rows often and would otherwise decode every icon each time.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], BitmapSource> Decoded = new();

    public static BitmapSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        if (Decoded.TryGetValue(png, out var cached)) return cached;
        try
        {
            using var ms = new MemoryStream(png);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Decoded.AddOrUpdate(png, bmp);
            return bmp;
        }
        catch { return null; }
    }
}
