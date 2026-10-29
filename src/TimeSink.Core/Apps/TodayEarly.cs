namespace TimeSink.Core.Apps;

/// <summary>The untracked-apps card: how many running apps are untracked, and the first few to offer.</summary>
public sealed record UntrackedCard(int Count, IReadOnlyList<RunningApp> Shown);

/// <summary>String-table keys of the Today early state (kept apart from <c>StringKeys</c> so parallel tickets do not collide).</summary>
public static class TodayEarlyStrings
{
    public const string UntrackedMany = "todayUntrackedMany";
    public const string UntrackedOne = "todayUntrackedOne";
    public const string OpenSince = "todayUntrackedSince";
    public const string NotThisOne = "todayNotThisOne";
    public const string TrackedHeader = "todayTrackedHeader";

    public static readonly IReadOnlyList<string> All = [UntrackedMany, UntrackedOne, OpenSince, NotThisOne, TrackedHeader];
}

/// <summary>Rules of the Today early state (implementation.md 8.3), independent of Win32 and WPF.</summary>
public static class TodayEarly
{
    public const long CaptionBelowSeconds = 3600;
    public const int DefaultMaxShown = 3;

    /// <summary>The "tracking since you signed in" caption shows while today's combined total is under one hour.</summary>
    public static bool ShowCaption(long totalSeconds) => totalSeconds < CaptionBelowSeconds;

    /// <summary>
    /// The time the caption names: the sign-in time if it was today, else the first app's start today, else unknown
    /// (the plain caption is used then).
    /// </summary>
    public static long? TrackingSinceUtc(long dayStartUtc, long? signInUtc, long? firstAppStartUtc) =>
        signInUtc is { } s && s >= dayStartUtc ? s : firstAppStartUtc;

    /// <summary>
    /// Untracked running apps (tracked and ignored ones excluded, longest open first) when suggestions are on and any
    /// exist; otherwise null. Reuses the Running tab's rules.
    /// </summary>
    public static UntrackedCard? UntrackedCard(bool suggestionsOn, IEnumerable<OpenWindow> windows, ExcludedApps excluded, int maxShown = DefaultMaxShown)
    {
        if (!suggestionsOn) return null;
        return UntrackedCard(suggestionsOn, RunningApps.Select(windows, excluded), maxShown);
    }

    /// <summary>The same card from running apps already filtered (the catalogue's Running Candidates).</summary>
    public static UntrackedCard? UntrackedCard(bool suggestionsOn, IReadOnlyList<RunningApp> apps, int maxShown = DefaultMaxShown)
    {
        if (!suggestionsOn) return null;
        return apps.Count == 0 ? null : new UntrackedCard(apps.Count, apps.Take(maxShown).ToList());
    }
}
