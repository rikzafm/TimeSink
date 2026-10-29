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

    /// <summary>An exe whose largest embedded icon is at least this many px is sharp enough at tile size.</summary>
    private const int SharpFrame = 128;

    /// <summary>
    /// PNG bytes (<paramref name="size"/> px, 64 by default) of the app's icon, from its Start tile when an AUMID is known,
    /// else from the exe. <paramref name="imageFile"/> (Steam's cached art) wins only when it has more real pixels than an
    /// exe whose largest embedded icon is under <see cref="SharpFrame"/> px (the shell shows such an icon as a tiny picture
    /// on a blank tile), and stands in when the exe cannot be read. An exe whose icon sizes cannot be read is trusted.
    /// Null on failure.
    /// </summary>
    public static byte[]? ExtractPng(string exePath, string? aumid, string? imageFile = null, int size = 64)
    {
        if (aumid is not null && Extract(@"shell:AppsFolder\" + aumid, size) is { } tile) return tile;
        var exe = Extract(exePath, size);
        if (imageFile is null) return exe;
        if (exe is not null)
        {
            var frame = LargestEmbeddedIcon(exePath);
            if (frame == 0 || frame >= SharpFrame) return exe;
            if (ImageSide(imageFile) is not { } side || side <= frame) return exe;
        }
        return FromImage(imageFile, size) ?? exe;
    }

    /// <summary>The shorter side of an image file in px, or null when unreadable.</summary>
    private static int? ImageSide(string path)
    {
        try
        {
            var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return Math.Min(frame.PixelWidth, frame.PixelHeight);
        }
        catch { return null; }
    }

    private delegate bool EnumResourceNameProc(nint module, nint type, nint name, nint param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string file, nint h, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceNamesW(nint module, nint type, EnumResourceNameProc proc, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint FindResourceW(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint res);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint res);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint res);

    private const uint LoadLibraryAsDatafile = 0x2;
    private const int RtGroupIcon = 14;

    /// <summary>
    /// The side in px of the biggest image in the exe's first icon group (the one the shell shows), or 0 when the exe has
    /// no icon group or cannot be read.
    /// </summary>
    private static int LargestEmbeddedIcon(string exePath)
    {
        var module = LoadLibraryExW(exePath, 0, LoadLibraryAsDatafile);
        if (module == 0) return 0;
        try
        {
            nint first = 0;
            EnumResourceNamesW(module, RtGroupIcon, (_, _, name, _) => { first = name; return false; }, 0);
            if (first == 0) return 0;
            var res = FindResourceW(module, first, RtGroupIcon);
            if (res == 0) return 0;
            var length = (int)SizeofResource(module, res);
            var data = LockResource(LoadResource(module, res));
            if (data == 0 || length < 6) return 0;
            // GRPICONDIR: 6-byte header (count at 4), then 14-byte entries whose first byte is the width (0 = 256)
            var count = Marshal.ReadInt16(data, 4);
            var best = 0;
            for (var i = 0; i < count && 6 + (i + 1) * 14 <= length; i++)
            {
                var width = Marshal.ReadByte(data, 6 + i * 14);
                best = Math.Max(best, width == 0 ? 256 : width);
            }
            return best;
        }
        catch { return 0; }
        finally { FreeLibrary(module); }
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
