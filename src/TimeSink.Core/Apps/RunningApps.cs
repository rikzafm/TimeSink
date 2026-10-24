namespace TimeSink.Core.Apps;

/// <summary>One top-level window as the UI's Win32 enumeration sees it, already mapped to its owning process.</summary>
public sealed record OpenWindow(
    string? ExePath, string DisplayName, DateTimeOffset ProcessStartUtc,
    bool Visible, bool Cloaked, bool ToolWindow, string? PackageFamily);

/// <summary>A row of the Add app / Running tab.</summary>
public sealed record RunningApp(string ExePath, string DisplayName, DateTimeOffset StartUtc, string? PackageFamily);

/// <summary>Apps the Running tab must not offer: already tracked (path or package family) and ignored paths.</summary>
public sealed record ExcludedApps(IEnumerable<string> Paths, IEnumerable<string> PackageFamilies);

/// <summary>The Running tab's filtering rules (implementation.md 8.3), separated from the Win32 enumeration.</summary>
public static class RunningApps
{
    public static IReadOnlyList<RunningApp> Select(IEnumerable<OpenWindow> windows, ExcludedApps excluded)
    {
        var paths = new HashSet<string>(excluded.Paths, StringComparer.OrdinalIgnoreCase);
        var families = new HashSet<string>(excluded.PackageFamilies, StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var w in windows)
        {
            if (w.ExePath is not { Length: > 0 } path) continue;
            if (!w.Visible || w.Cloaked || w.ToolWindow) continue;
            if (paths.Contains(path)) continue;
            if (w.PackageFamily is { } f && families.Contains(f)) continue;
            if (!byPath.TryGetValue(path, out var seen) || w.ProcessStartUtc < seen.StartUtc)
                byPath[path] = new RunningApp(path, w.DisplayName, w.ProcessStartUtc, w.PackageFamily);
        }
        return byPath.Values.OrderBy(a => a.StartUtc).ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
