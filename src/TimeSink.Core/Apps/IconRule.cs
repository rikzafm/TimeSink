namespace TimeSink.Core.Apps;

/// <summary>
/// Which picture an app gets (decision 154): its Start tile when an AUMID is known, else its exe's icon. The image file
/// (Steam's cached art) wins only when it has more real pixels than an exe whose largest embedded icon is under
/// <see cref="SharpFrame"/> px (the shell shows such an icon as a tiny picture on a blank tile), and stands in when the
/// exe cannot be read. An exe whose icon sizes cannot be read is trusted. Null when nothing yields a picture.
/// </summary>
public static class IconRule
{
    /// <summary>An exe whose largest embedded icon is at least this many px is sharp enough at tile size.</summary>
    public const int SharpFrame = 128;

    public static byte[]? Extract(IIconExtractor extractor, string exePath, string? aumid, string? imageFile, int size)
    {
        if (aumid is not null && extractor.FromTile(aumid, size) is { } tile) return tile;
        var exe = extractor.FromExe(exePath, size);
        if (imageFile is null) return exe;
        if (exe is not null)
        {
            var frame = extractor.EmbeddedSide(exePath);
            if (frame == 0 || frame >= SharpFrame) return exe;
            if (extractor.ImageSide(imageFile) is not { } side || side <= frame) return exe;
        }
        return extractor.FromImage(imageFile, size) ?? exe;
    }
}
