namespace TimeSink.Core.Apps;

/// <summary>
/// The seam between the icon rule (<see cref="IconRule"/>) and the shell: five small capabilities, each answering null (or
/// 0) when it cannot. The real adapter lives in the UI project; tests use a fake. Calls block and extraction is slow, so
/// call from a pool thread.
/// </summary>
public interface IIconExtractor
{
    /// <summary>A PNG of the Start tile logo of the packaged app <paramref name="aumid"/> at about <paramref name="size"/> px.</summary>
    byte[]? FromTile(string aumid, int size);

    /// <summary>A PNG of the exe's icon at about <paramref name="size"/> px, as the shell shows it.</summary>
    byte[]? FromExe(string exePath, int size);

    /// <summary>The side in px of the biggest image in the exe's first icon group, or 0 when it has none or cannot be read.</summary>
    int EmbeddedSide(string exePath);

    /// <summary>The shorter side of an image file in px, or null when unreadable.</summary>
    int? ImageSide(string imageFile);

    /// <summary>An image file centre-cropped to a square and scaled to <paramref name="size"/> px as PNG; null when unreadable.</summary>
    byte[]? FromImage(string imageFile, int size);
}
