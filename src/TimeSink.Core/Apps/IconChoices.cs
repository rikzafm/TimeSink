namespace TimeSink.Core.Apps;

/// <summary>Where an icon the picker offers comes from.</summary>
public enum IconChoiceKind { App, Exe, SteamCover, SteamHeader, SteamLogo, SteamArt, File }

/// <summary>One icon the user can give an app: its source, the file it came from and the square PNG.</summary>
public sealed record IconChoice(IconChoiceKind Kind, string Detail, byte[] Png);

/// <summary>
/// Every icon found for an app (decision 153): the tracked exe's own icon, the other game exes in its install folder, and
/// Steam's cached art when it is a Steam game. Choices that draw the same picture are listed once, the first source wins.
/// The picker is only offered when more than one is found.
/// </summary>
public static class IconChoices
{
    private const int MaxOtherExes = 3;

    public static IReadOnlyList<IconChoice> Find(string exePath, IIconExtractor extractor, int size)
    {
        var found = new List<IconChoice>();
        void Add(IconChoiceKind kind, string detail, byte[]? png)
        {
            if (png is { Length: > 0 } && !found.Any(c => c.Png.AsSpan().SequenceEqual(png))) found.Add(new IconChoice(kind, detail, png));
        }
        byte[]? Extract(string exe, string? image)
        {
            try { return IconRule.Extract(extractor, exe, null, image, size); }
            catch (IOException) { return null; }
        }

        Add(IconChoiceKind.App, Path.GetFileName(exePath), Extract(exePath, null));
        var folder = SteamLibrary.InstallDirOf(exePath) ?? Path.GetDirectoryName(exePath);
        if (folder is not null)
            foreach (var other in SteamLibrary.GameExes(folder, MaxOtherExes + 1).Where(e => !string.Equals(e, exePath, StringComparison.OrdinalIgnoreCase)).Take(MaxOtherExes))
                Add(IconChoiceKind.Exe, Path.GetFileName(other), Extract(other, null));
        foreach (var (kind, file) in SteamLibrary.ArtFilesForExe(exePath))
            Add(kind, Path.GetFileName(file), Extract("", file));
        return found;
    }

    /// <summary>An image file the user picked (PNG, JPG or ICO), centre-cropped to a square; null when it cannot be read.</summary>
    public static IconChoice? FromFile(string path, IIconExtractor extractor, int size)
    {
        try { return IconRule.Extract(extractor, "", null, path, size) is { Length: > 0 } png ? new IconChoice(IconChoiceKind.File, Path.GetFileName(path), png) : null; }
        catch (IOException) { return null; }
    }
}
