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

    /// <summary>PNG bytes (64 px) of the app's icon, from its Start tile when an AUMID is known, else from the exe; null on failure.</summary>
    public static byte[]? ExtractPng(string exePath, string? aumid)
    {
        if (aumid is not null && Extract(@"shell:AppsFolder\" + aumid) is { } tile) return tile;
        return Extract(exePath);
    }

    private static byte[]? Extract(string parsingName)
    {
        nint hbmp = 0;
        try
        {
            if (SHCreateItemFromParsingName(parsingName, 0, ref _iid, out var factory) != 0) return null;
            if (factory.GetImage(new SIZE { cx = 64, cy = 64 }, SIIGBF_BIGGERSIZEOK, out hbmp) != 0 || hbmp == 0) return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
        finally { if (hbmp != 0) DeleteObject(hbmp); }
    }

    public static BitmapSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(png);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
