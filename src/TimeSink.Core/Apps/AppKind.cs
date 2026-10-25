namespace TimeSink.Core.Apps;

/// <summary>What a tracked app is for the Year > Wrapped game pages. Stored as <c>apps.kind</c> (NULL = detect).</summary>
public enum AppKind { Other = 0, Game = 1, Gacha = 2 }

public static class AppKinds
{
    // Lower-case fragments of the app name or exe file name.
    private static readonly string[] GachaNames =
    [
        "genshin", "yuanshen", "starrail", "star rail", "zenless", "zzz", "wuthering", "wuwa", "arknights", "endfield",
        "bluearchive", "blue archive", "nikke", "fate/grand", "fategrand", "fgo", "punishing", "reverse1999", "reverse 1999",
        "honkai", "azur lane", "azurlane", "girls' frontline", "girlsfrontline", "counterside", "epic seven", "epicseven",
        "uma musume", "umamusume", "snowbreak", "limbus", "dislyte", "tower of fantasy", "neverness", "onmyoji",
        "genshin impact", "love and deepspace", "infinity nikki", "granblue", "princess connect", "pokemon masters",
    ];

    private static readonly string[] SteamFolders = [@"\steamapps\common\"];

    /// <summary>The kind an app has when nobody chose one: known gacha titles, then anything installed by Steam, else Other.</summary>
    public static AppKind Detect(string exePath, string displayName)
    {
        var file = System.IO.Path.GetFileNameWithoutExtension(exePath);
        foreach (var n in GachaNames)
            if (displayName.Contains(n, StringComparison.OrdinalIgnoreCase) || file.Contains(n, StringComparison.OrdinalIgnoreCase)) return AppKind.Gacha;
        foreach (var f in SteamFolders)
            if (exePath.Contains(f, StringComparison.OrdinalIgnoreCase)) return AppKind.Game;
        return AppKind.Other;
    }

    public static AppKind Resolve(long? stored, string exePath, string displayName) =>
        stored is { } k && Enum.IsDefined(typeof(AppKind), (int)k) ? (AppKind)(int)k : Detect(exePath, displayName);

    /// <summary>The next kind in the row button's cycle: Other, Game, Gacha, Other.</summary>
    public static AppKind Next(AppKind kind) => kind switch { AppKind.Other => AppKind.Game, AppKind.Game => AppKind.Gacha, _ => AppKind.Other };
}
