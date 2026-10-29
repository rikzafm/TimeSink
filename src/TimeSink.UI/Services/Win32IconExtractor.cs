using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>The real adapter for <see cref="IIconExtractor"/>: the shell image factory, the exe's icon resources and WPF bitmaps.</summary>
internal sealed class Win32IconExtractor : IIconExtractor
{
    public static readonly Win32IconExtractor Instance = new();

    public byte[]? FromTile(string aumid, int size) => IconService.FromTile(aumid, size);

    public byte[]? FromExe(string exePath, int size) => IconService.FromExe(exePath, size);

    public int EmbeddedSide(string exePath) => IconService.LargestEmbeddedIcon(exePath);

    public int? ImageSide(string imageFile) => IconService.ImageSide(imageFile);

    public byte[]? FromImage(string imageFile, int size) => IconService.FromImage(imageFile, size);
}
