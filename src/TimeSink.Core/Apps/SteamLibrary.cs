using System.Text.RegularExpressions;

namespace TimeSink.Core.Apps;

/// <summary>An installed Steam game resolved to the executable that represents it.</summary>
public sealed record SteamGame(string Name, string ExePath, string? IconFile = null);

/// <summary>
/// Finds installed Steam games from Steam's own files: <c>libraryfolders.vdf</c> lists the library folders and each
/// <c>appmanifest_*.acf</c> names a game and its install folder. The game's main exe is the largest plausible .exe
/// under that folder, since the manifests do not name one. Pure file reads; the Steam root comes from the caller.
/// </summary>
public static class SteamLibrary
{
    /// <summary>True for a file name in a library's <c>steamapps</c> folder that means the games changed: an app manifest or the library list.</summary>
    public static bool IsLibraryChange(string? name) =>
        name is not null
        && ((name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
            || name.Equals("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase));

    private const int MaxDepth = 3;

    private static readonly Regex Pair = new("\"(?<k>[^\"]*)\"\\s+\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    // Steamworks redistributables, Proton and runtimes show up as installed "apps" but are not games.
    private static readonly string[] NotGames = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "Steamworks Shared"];

    private static readonly string[] HelperExes =
        ["unins", "uninstall", "crash", "redist", "vcredist", "dxsetup", "dotnet", "setup", "installer", "easyanticheat", "beservice", "launcher_helper", "cefprocess"];

    /// <summary>Steam's install folder from the current user's registry, or null when Steam is not installed.</summary>
    public static string? FindRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        }
        catch { return null; }
    }

    private const string CommonMarker = @"\steamapps\common\";

    /// <summary>True for an exe under a Steam library's <c>steamapps\common</c> folder.</summary>
    public static bool IsInLibrary(string exePath) => exePath.Contains(CommonMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The cached art for the game that owns <paramref name="exePath"/> (a file under <c>steamapps\common\&lt;installdir&gt;</c>),
    /// or null when the exe is not in a Steam library or Steam cached nothing.
    /// </summary>
    public static string? ArtForExe(string exePath)
    {
        var at = exePath.IndexOf(CommonMarker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var steamapps = exePath[..(at + "\\steamapps".Length)];
        var dir = exePath[(at + CommonMarker.Length)..].Split('\\')[0];
        var root = FindRoot();
        if (root is null || !Directory.Exists(steamapps)) return null;
        try
        {
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var (_, installDir, appId) = ReadManifest(acf);
                if (appId is not null && string.Equals(installDir, dir, StringComparison.OrdinalIgnoreCase)) return CachedArt(root, appId);
            }
        }
        catch { }
        return null;
    }

    public static IReadOnlyList<SteamGame> Scan(string? steamRoot, Func<string, bool>? isKnownInstallDir = null)
    {
        var games = new List<SteamGame>();
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot)) return games;

        foreach (var lib in LibraryFolders(steamRoot))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").ToList(); }
            catch { continue; }
            foreach (var acf in manifests)
            {
                var (name, dir, appId) = ReadManifest(acf);
                if (name is null || dir is null || NotGames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) continue;
                var installDir = Path.Combine(steamapps, "common", dir);
                if (isKnownInstallDir?.Invoke(installDir) == true) continue; // skip the exe walk for games already in the library
                var exe = MainExe(installDir);
                if (exe is not null) games.Add(new SteamGame(name, exe, appId is null ? null : CachedArt(steamRoot, appId)));
            }
        }
        return games;
    }

    /// <summary>The Steam root plus every library path listed in <c>libraryfolders.vdf</c>, existing and distinct.</summary>
    public static IReadOnlyList<string> LibraryFolders(string steamRoot)
    {
        var folders = new List<string> { steamRoot };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                foreach (Match m in Pair.Matches(File.ReadAllText(vdf)))
                    if (m.Groups["k"].Value == "path") folders.Add(Unescape(m.Groups["v"].Value));
            }
            catch { }
        }
        return folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static (string? Name, string? InstallDir, string? AppId) ReadManifest(string acf)
    {
        string? name = null, dir = null, appId = null;
        try
        {
            foreach (Match m in Pair.Matches(File.ReadAllText(acf)))
            {
                var key = m.Groups["k"].Value;
                if (key == "name") name ??= Unescape(m.Groups["v"].Value);
                else if (key == "installdir") dir ??= Unescape(m.Groups["v"].Value);
                else if (key == "appid") appId ??= m.Groups["v"].Value;
            }
        }
        catch { }
        return (name, dir, appId);
    }

    /// <summary>
    /// The best image Steam has already cached for the game under <c>appcache\librarycache</c> (also in its hash
    /// subfolders), biggest first since it gets cropped square and scaled up: the 300x450 capsule
    /// (<c>library_600x900.jpg</c>), the 460x215 header (<c>library_header.jpg</c>), the older <c>library_capsule.jpg</c> and
    /// <c>header.jpg</c> names, and last the 32x32 community icon (a 40-hex-digit .jpg). Null when Steam cached none.
    /// </summary>
    public static string? CachedArt(string steamRoot, string appId)
    {
        var dir = Path.Combine(steamRoot, "appcache", "librarycache", appId);
        if (!Directory.Exists(dir)) return null;
        try
        {
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)).ToList();
            foreach (var name in ArtNames)
                if (files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found) return found;
            return files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), "^[0-9a-f]{40}\\.jpg$", RegexOptions.IgnoreCase));
        }
        catch { return null; }
    }

    private static readonly string[] ArtNames = ["library_600x900.jpg", "library_header.jpg", "library_capsule.jpg", "header.jpg"];

    /// <summary>The largest non-helper .exe within a few levels of the install folder, or null when there is none.</summary>
    public static string? MainExe(string installDir)
    {
        if (!Directory.Exists(installDir)) return null;
        string? best = null;
        long bestSize = -1;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = MaxDepth, IgnoreInaccessible = true };
        try
        {
            foreach (var exe in Directory.EnumerateFiles(installDir, "*.exe", options))
            {
                var file = Path.GetFileName(exe);
                if (HelperExes.Any(h => file.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                long size;
                try { size = new FileInfo(exe).Length; } catch { continue; }
                if (size > bestSize) { best = exe; bestSize = size; }
            }
        }
        catch { }
        return best;
    }

    private static string Unescape(string s) => s.Replace("\\\\", "\\").Replace("\\\"", "\"");
}
