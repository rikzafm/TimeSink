using TimeSink.Core.Data;

namespace TimeSink.Core.Apps;

/// <summary>
/// Adds installed Steam games to the tracked apps. Runs in the Agent at start-up and every few hours. A game already in
/// the library, tracked or stopped, is never touched, and neither is one on the "Not this one" list, so a game the user
/// stopped tracking stays stopped.
/// </summary>
public static class SteamAutoTracker
{
    public const string SettingKey = "steam.autoTrack";

    /// <summary>On unless the setting is explicitly "false".</summary>
    public static bool Enabled(string? raw) => !bool.TryParse(raw, out var v) || v;

    /// <summary>Scans <paramref name="steamRoot"/> and adds new games; returns how many were added.</summary>
    public static int Run(string dbPath, string? steamRoot, long nowUtc)
    {
        if (!Enabled(SettingsStore.Get(dbPath, SettingKey))) return 0;
        using var lib = AppLibrary.Open(dbPath);

        var known = lib.Library().Select(a => a.ExePath).Concat(lib.MergedAliases().Select(a => a.ExePath)).Concat(lib.ExcludedFromRunning().Paths).ToList();
        bool IsKnown(string installDir)
        {
            var prefix = installDir.TrimEnd('\\') + "\\";
            return known.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        var added = 0;
        foreach (var game in SteamLibrary.Scan(steamRoot, IsKnown))
            if (!known.Contains(game.ExePath, StringComparer.OrdinalIgnoreCase)
                && lib.AddTracked(new NewApp(game.ExePath, game.Name, null), nowUtc, null).Outcome == AddOutcome.Added)
                added++;
        return added;
    }
}
