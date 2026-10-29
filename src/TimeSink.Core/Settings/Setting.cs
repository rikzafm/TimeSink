using System.Globalization;
using TimeSink.Core.Icons;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;

namespace TimeSink.Core.Settings;

public delegate bool SettingParser<T>(string stored, out T value);

/// <summary>What the Settings page and the catalogue need to know about a <see cref="Setting{T}"/> without its type.</summary>
public interface ISetting
{
    string Key { get; }

    /// <summary>The default as stored text.</summary>
    string DefaultText { get; }

    /// <summary>Every stored text the setting accepts, or null when any well-formed value is accepted.</summary>
    IReadOnlyList<string>? Allowed { get; }

    /// <summary>The stored text a value resolves to: itself when valid, else the default.</summary>
    string Resolve(string? stored);
}

/// <summary>
/// One <c>settings</c> table entry: its key, default, and the one rule for reading it. A stored value that is missing or
/// not valid reads as the default. A <see cref="Setting"/> is declared once and read through <see cref="AppSettings"/>.
/// </summary>
public sealed class Setting<T>(string key, T @default, SettingParser<T> tryParse, Func<T, string> format, IReadOnlyList<string>? allowed = null) : ISetting
{
    public string Key => key;
    public T Default => @default;
    public IReadOnlyList<string>? Allowed => allowed;
    public string DefaultText => format(@default);

    public T Parse(string? stored) => stored is not null && tryParse(stored, out var value) ? value : @default;

    public string Format(T value) => format(value);

    public string Resolve(string? stored) => format(Parse(stored));
}

/// <summary>
/// Every stored setting, declared once. <b>User settings</b> have a row on the Settings page (<see cref="SettingsCatalogue"/>
/// points at their handle). <b>Internal state</b> is remembered by the app and has no row.
/// </summary>
public static class Setting
{
    public static Setting<bool> Switch(string key, bool on) =>
        new(key, on, ParseSwitch, b => b ? "true" : "false", ["true", "false"]);

    public static Setting<string> Choice(string key, string @default, params string[] allowed) =>
        new(key, @default, (string stored, out string value) => { value = stored; return allowed.Contains(stored); }, s => s, allowed);

    public static Setting<int> Number(string key, int @default, params int[] allowed) =>
        new(key, @default, (string stored, out int value) => int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out value) && allowed.Contains(value),
            n => n.ToString(CultureInfo.InvariantCulture), allowed.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray());

    /// <summary>A setting that holds one name of an enum, spelled exactly.</summary>
    public static Setting<T> Name<T>(string key, T @default) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        return new(key, @default, (string stored, out T value) => { value = default; return names.Contains(stored) && Enum.TryParse(stored, out value); }, v => v.ToString(), names);
    }

    /// <summary>A whole number of zero or more, such as a Unix time. Zero when never stored.</summary>
    public static Setting<long> Count(string key) =>
        new(key, 0, (string stored, out long value) => long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out value), n => n.ToString(CultureInfo.InvariantCulture));

    /// <summary>Free text, null when never stored or empty.</summary>
    public static Setting<string?> Text(string key) =>
        new(key, null, (string stored, out string? value) => { value = stored; return stored.Length > 0; }, s => s ?? "");

    private static bool ParseSwitch(string stored, out bool value)
    {
        value = stored == "true";
        return stored is "true" or "false";
    }

    // ---- User settings: each has a row on the Settings page ----

    public static readonly Setting<bool> Startup = Switch("startup.enabled", true);
    public static readonly Setting<WeekStart> WeekStartsOn = Name("week.start", WeekStart.Mon);

    public static readonly Setting<bool> PauseOnSleep = Switch("tracking.pauseOnSleep", true);
    public static readonly Setting<bool> CountWhenLocked = Switch("tracking.countWhenLocked", true);
    public static readonly Setting<int> MinSessionSeconds = Number("tracking.minSessionSeconds", 15, 5, 15, 30);
    public static readonly Setting<bool> SuggestApps = Switch("tracking.suggestApps", true);
    public static readonly Setting<bool> SteamAutoTrack = Switch("steam.autoTrack", true);

    public static readonly Setting<string> Theme = Choice("ui.theme", "System", "Dark", "Light", "System");
    public static readonly Setting<string> Accent = Choice("ui.accent", "Lime", "Lime", "Blue", "Pink");
    public static readonly Setting<bool> Compact = Switch("ui.compact", false);
    public static readonly Setting<string> AppIcon =
        new("ui.icon", IconCatalogue.DefaultId, (string stored, out string value) => { value = IconCatalogue.Resolve(stored); return true; }, s => s);

    public static readonly Setting<bool> GoalAlerts = Switch("alerts.goals", true);
    public static readonly Setting<bool> WeeklySummary = Switch("alerts.weekly", false);

    public static readonly Setting<string> BackupMode = Choice("backup.mode", "Daily", "Off", "Daily", "Weekly");

    // ---- Internal state: remembered by the app, no row on the Settings page ----

    public static readonly Setting<ShellTab> LastTab = Name("ui.lastTab", ShellTab.Today);
    public static readonly Setting<YearSubView> LastYearView = Name("ui.lastYearView", YearSubView.Pace);
    public static readonly Setting<string> AllLayout = Choice("all.layout", "labeled", "labeled", "icons");
    public static readonly Setting<bool> InstantOpen = Switch("ui.instantOpen", false);

    public static readonly Setting<long> BackupLastUtc = Count("backup.lastUtc");
    public static readonly Setting<string?> BackupFolder = Text("backup.folder");

    public static readonly Setting<string?> AlertsSent = Text("alerts.sent");
    public static readonly Setting<string?> AlertsWeeklyLast = Text("alerts.weeklyLast");

    /// <summary>
    /// Stored as "1" by builds before this module existed, so an already repaired database stays repaired. The key ends in 2
    /// because "icons.steamRepaired" was the first pass, which used the exe icon alone.
    /// </summary>
    public static readonly Setting<bool> SteamIconsRepaired =
        new("icons.steamRepaired2", false, (string stored, out bool value) => { value = stored == "1"; return value; }, b => b ? "1" : "0");

    public static readonly IReadOnlyList<ISetting> All =
    [
        Startup, WeekStartsOn, PauseOnSleep, CountWhenLocked, MinSessionSeconds, SuggestApps, SteamAutoTrack,
        Theme, Accent, Compact, AppIcon, GoalAlerts, WeeklySummary, BackupMode,
        LastTab, LastYearView, AllLayout, InstantOpen, BackupLastUtc, BackupFolder, AlertsSent, AlertsWeeklyLast, SteamIconsRepaired,
    ];
}
