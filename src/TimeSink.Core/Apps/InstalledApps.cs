using System.Globalization;
using System.Text;

namespace TimeSink.Core.Apps;

/// <summary>A Start Menu shortcut already resolved to its target.</summary>
public sealed record ShortcutEntry(string Name, string TargetPath);

/// <summary>A packaged (Store) app from the Apps folder; <see cref="Aumid"/> is "PackageFamilyName!AppId".</summary>
public sealed record PackagedApp(string Name, string Aumid);

/// <summary>One row of the Installed tab. Packaged apps carry a synthetic <c>shell:AppsFolder\&lt;aumid&gt;</c> path.</summary>
public sealed record InstalledApp(string Name, string ExePath, string? PackageFamily, string? Aumid, string? Publisher)
{
    public NewApp ToNewApp() => new(ExePath, Name, PackageFamily);
}

public sealed record InstalledGroup(string Letter, IReadOnlyList<InstalledApp> Apps);

/// <summary>Reads Start Menu <c>.lnk</c> files; the resolver (IShellLink in the UI) is injected so this stays testable.</summary>
public static class StartMenuScanner
{
    /// <summary>All <c>.lnk</c> files under the roots (missing roots skipped), resolved; only <c>.exe</c> targets are kept.</summary>
    public static IReadOnlyList<ShortcutEntry> Scan(IEnumerable<string> roots, Func<string, string?> resolve)
    {
        var list = new List<ShortcutEntry>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
            catch { continue; }
            foreach (var lnk in files)
            {
                string? target;
                try { target = resolve(lnk); } catch { continue; }
                if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new ShortcutEntry(Path.GetFileNameWithoutExtension(lnk), target));
            }
        }
        return list;
    }
}

/// <summary>Installed-app list rules: filtering, de-duplication, A-Z grouping, search and the letter rail.</summary>
public static class InstalledCatalog
{
    private const string Other = "#";

    public static IReadOnlyList<InstalledApp> Build(IEnumerable<ShortcutEntry> shortcuts, IEnumerable<PackagedApp> packaged)
    {
        var byTarget = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in shortcuts)
        {
            if (IsUninstaller(s.Name, s.TargetPath)) continue;
            if (!byTarget.TryGetValue(s.TargetPath, out var seen) || s.Name.Length < seen.Name.Length)
                byTarget[s.TargetPath] = new InstalledApp(s.Name, s.TargetPath, null, null, null);
        }
        var all = byTarget.Values.ToList();
        foreach (var p in packaged)
        {
            var bang = p.Aumid.IndexOf('!');
            var family = bang > 0 ? p.Aumid[..bang] : null;
            var path = @"shell:AppsFolder\" + p.Aumid;
            if (all.Any(a => string.Equals(a.ExePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            all.Add(new InstalledApp(p.Name, path, family, p.Aumid, null));
        }
        return all.OrderBy(a => SortKey(a.Name), StringComparer.Ordinal).ToList();
    }

    private static bool IsUninstaller(string name, string target)
    {
        var file = Path.GetFileName(target);
        return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               || file.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Case-insensitive substring match on name or publisher; a blank query returns everything.</summary>
    public static IReadOnlyList<InstalledApp> Search(IEnumerable<InstalledApp> apps, string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return apps.ToList();
        return apps.Where(a => Contains(a.Name, q) || (a.Publisher is not null && Contains(a.Publisher, q))).ToList();
    }

    private static bool Contains(string text, string q) =>
        Fold(text).Contains(Fold(q), StringComparison.Ordinal);

    /// <summary>A-Z groups in order, digits and symbols together under "#" at the end.</summary>
    public static IReadOnlyList<InstalledGroup> Group(IEnumerable<InstalledApp> apps)
    {
        return apps.OrderBy(a => SortKey(a.Name), StringComparer.Ordinal)
            .GroupBy(a => LetterOf(a.Name))
            .OrderBy(g => g.Key == Other ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new InstalledGroup(g.Key, g.ToList()))
            .ToList();
    }

    /// <summary>Letters shown on the rail: those that have apps, in list order.</summary>
    public static IReadOnlyList<string> Rail(IReadOnlyList<InstalledGroup> groups) => groups.Select(g => g.Letter).ToList();

    public static string LetterOf(string name)
    {
        foreach (var ch in Fold(name))
            if (char.IsLetterOrDigit(ch)) return ch is >= 'a' and <= 'z' ? char.ToUpperInvariant(ch).ToString() : Other;
        return Other;
    }

    private static string SortKey(string name) => Fold(name);

    /// <summary>Lower-case with diacritics removed ("ábaco" sorts and groups as "abaco").</summary>
    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
