using System.Text;
using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

/// <summary>
/// An <see cref="IIconExtractor"/> that records its calls. An exe gives "png:" + its path, a tile "tile:" + its AUMID (none
/// unless listed in <see cref="Tiles"/>), an image file "art:" + its path. An exe's embedded icon is sharp (256 px) and an
/// image file large (1024 px) unless set otherwise.
/// </summary>
internal sealed class FakeIcons : IIconExtractor
{
    public List<(string Exe, int Size)> IconCalls { get; } = [];
    public List<(string Aumid, int Size)> TileCalls { get; } = [];
    public List<(string File, int Size)> ImageCalls { get; } = [];
    public HashSet<string> NoIcon { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tiles { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Embedded { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ImageSides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Exception? IconFails { get; set; }
    public Dictionary<string, Exception> IconFailsFor { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Action<string>? OnIcon { get; set; }

    public byte[]? FromTile(string aumid, int size)
    {
        TileCalls.Add((aumid, size));
        return Tiles.Contains(aumid) ? Encoding.UTF8.GetBytes("tile:" + aumid) : null;
    }

    public byte[]? FromExe(string exePath, int size)
    {
        IconCalls.Add((exePath, size));
        OnIcon?.Invoke(exePath);
        if (IconFails is { } fail) throw fail;
        if (IconFailsFor.TryGetValue(exePath, out var fails)) throw fails;
        return exePath.Length == 0 || NoIcon.Contains(exePath) ? null : Encoding.UTF8.GetBytes("png:" + exePath);
    }

    public int EmbeddedSide(string exePath) => Embedded.GetValueOrDefault(exePath, 256);

    public int? ImageSide(string imageFile) => ImageSides.TryGetValue(imageFile, out var side) ? side : 1024;

    public byte[]? FromImage(string imageFile, int size)
    {
        ImageCalls.Add((imageFile, size));
        return Encoding.UTF8.GetBytes("art:" + imageFile);
    }
}
