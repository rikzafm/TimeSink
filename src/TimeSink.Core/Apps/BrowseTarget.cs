using System.Diagnostics;

namespace TimeSink.Core.Apps;

public enum BrowseError { NotAnApp, TargetMissing }

public sealed record BrowseResult(NewApp? App, BrowseError? Error);

/// <summary>Turns a dropped or chosen file (.exe or .lnk) into an app to track (Browse tab).</summary>
public static class BrowseTarget
{
    public static BrowseResult Resolve(string path, Func<string, string?> resolveLnk)
    {
        if (!File.Exists(path)) return Fail(BrowseError.TargetMissing);
        var ext = Path.GetExtension(path);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            string? target;
            try { target = resolveLnk(path); } catch { target = null; }
            if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Fail(BrowseError.NotAnApp);
            if (!File.Exists(target)) return Fail(BrowseError.TargetMissing);
            path = target;
        }
        else if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Fail(BrowseError.NotAnApp);

        return new BrowseResult(new NewApp(path, DisplayName(path), null), null);
    }

    /// <summary>File description when the exe has one, else the file name without extension (decision 74).</summary>
    public static string DisplayName(string exePath)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static BrowseResult Fail(BrowseError e) => new(null, e);
}

public sealed record CommonPlace(string Name, string Path, bool Exists);

/// <summary>The "Common places" shortcuts of the Browse tab; <c>env</c> reads environment variables.</summary>
public static class CommonPlaces
{
    public static IReadOnlyList<CommonPlace> All(Func<string, string?> env)
    {
        var pf = env("ProgramFiles");
        var pf86 = env("ProgramFiles(x86)");
        var local = env("LocalAppData");
        var list = new List<CommonPlace>
        {
            Make("Program Files", pf),
            Make("Program Files (x86)", pf86),
            Make(@"LocalAppData\Programs", local is null ? null : Path.Combine(local, "Programs")),
            Make("WindowsApps", pf is null ? null : Path.Combine(pf, "WindowsApps")),
        };
        return list;
    }

    public static IReadOnlyList<CommonPlace> All() => All(Environment.GetEnvironmentVariable);

    private static CommonPlace Make(string name, string? path) => new(name, path ?? "", path is not null && Directory.Exists(path));
}
