namespace TimeSink.Core.Theming;

/// <summary>
/// The Palette slot of an app: the index into the theme's <c>apps</c> colours it always uses, from its Main app id.
/// The colour count is a parameter because themes carry 4 to 8 colours (decision 34) and Core does not read the theme.
/// </summary>
public static class AppPalette
{
    public static int Slot(long key, int paletteSize)
    {
        var size = Math.Max(1, paletteSize);
        return (int)(((key % size) + size) % size);
    }
}
