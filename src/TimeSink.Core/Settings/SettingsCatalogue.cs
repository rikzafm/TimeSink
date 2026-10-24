using TimeSink.Core.Data;
using TimeSink.Core.Icons;

namespace TimeSink.Core.Settings;

public enum SettingKind { Switch, Segmented, Button, Info, Icon }

public sealed record SettingOption(string Value, string Label);

/// <summary>
/// One row of the Settings page. <see cref="Key"/> is the <c>settings</c> table key for Switch/Segmented rows
/// (null for Button/Info rows, which carry no stored value).
/// </summary>
public sealed record SettingRow(
    string Id, string Section, string Label, string Description, SettingKind Kind,
    string? Key = null, string Default = "", IReadOnlyList<SettingOption>? Options = null, bool Danger = false)
{
    public IReadOnlyList<SettingOption> OptionList => Options ?? [];
}

/// <summary>The single declarative source for the Settings page (implementation.md section 8.3, key table 5.1).</summary>
public static class SettingsCatalogue
{
    public const string General = "General", Tracking = "Tracking", Appearance = "Appearance",
        Notifications = "Notifications", BackupAndData = "Backup and data", About = "About";

    public static readonly IReadOnlyList<string> Sections = [General, Tracking, Appearance, Notifications, BackupAndData, About];

    private static SettingOption[] Opts(params string[] values) => values.Select(v => new SettingOption(v, v)).ToArray();

    public static readonly IReadOnlyList<SettingRow> Rows =
    [
        new("startup", General, "Start with Windows", "Launch TimeSink quietly when you sign in.", SettingKind.Switch, "startup.enabled", "true"),
        new("weekStart", General, "Week starts on", "Used by the Week tab and weekly totals.", SettingKind.Segmented, "week.start", "Mon",
            [new("Mon", "Mon"), new("Sun", "Sun"), new("Sat", "Sat")]),

        new("pauseOnSleep", Tracking, "Pause while the PC sleeps", "Time stops counting while the computer is asleep.", SettingKind.Switch, "tracking.pauseOnSleep", "true"),
        new("countWhenLocked", Tracking, "Keep counting when locked", "Apps still running behind the lock screen keep adding up.", SettingKind.Switch, "tracking.countWhenLocked", "true"),
        new("minSession", Tracking, "Ignore short sessions", "Sessions shorter than this are dropped.", SettingKind.Segmented, "tracking.minSessionSeconds", "15",
            [new("5", "5s"), new("15", "15s"), new("30", "30s")]),
        new("suggestApps", Tracking, "Suggest apps I use", "Offer running apps you have not added yet.", SettingKind.Switch, "tracking.suggestApps", "true"),

        new("steamAutoTrack", Tracking, "Track Steam games", "Add newly installed Steam games automatically.", SettingKind.Switch, "steam.autoTrack", "true"),

        new("theme", Appearance, "Theme", "Follows Windows when set to System.", SettingKind.Segmented, "ui.theme", "System", Opts("Dark", "Light", "System")),
        new("accent", Appearance, "Accent", "The highlight colour used across the app.", SettingKind.Segmented, "ui.accent", "Lime", Opts("Lime", "Blue", "Pink")),
        new("compact", Appearance, "Compact lists", "Tighter rows in app lists.", SettingKind.Switch, "ui.compact", "false"),
        new("appIcon", Appearance, "App icon", "How TimeSink looks in the tray.", SettingKind.Icon, "ui.icon", IconCatalogue.DefaultId),

        new("goalAlerts", Notifications, "Goal and limit alerts", "A notification when a goal or limit is reached.", SettingKind.Switch, "alerts.goals", "true"),
        new("weeklySummary", Notifications, "Weekly summary", "A short recap at the start of each week.", SettingKind.Switch, "alerts.weekly", "false"),

        new("backupMode", BackupAndData, "Automatic backup", "Keeps the last 7 plus one per month.", SettingKind.Segmented, "backup.mode", "Daily", Opts("Off", "Daily", "Weekly")),
        new("backupNow", BackupAndData, "Back up now", "", SettingKind.Button),
        new("restore", BackupAndData, "Restore from backup", "Merge or replace from a backup file.", SettingKind.Button),
        new("export", BackupAndData, "Export all data", "CSV or JSON.", SettingKind.Button),
        new("deleteHistory", BackupAndData, "Delete all history", "Removes every recorded session. Your app list stays.", SettingKind.Button, Danger: true),

        new("resourceUse", About, "Resource use", "What the tracking Agent costs right now.", SettingKind.Info),
        new("trackingSince", About, "Tracking since", "", SettingKind.Info),
        new("installFolder", About, "Installed location", "Open the folder TimeSink runs from.", SettingKind.Button),
        new("uninstall", About, "Uninstall TimeSink", "Removes the app. You choose whether to keep your history.", SettingKind.Button, Danger: true),
    ];

    public static SettingRow Find(string id) => Rows.Single(r => r.Id == id);

    public static IEnumerable<SettingRow> InSection(string section) => Rows.Where(r => r.Section == section);

    /// <summary>A stored value if it is valid for the row, else the row's default.</summary>
    public static string Resolve(SettingRow row, string? stored)
    {
        if (stored is null) return row.Default;
        return row.Kind switch
        {
            SettingKind.Switch => stored is "true" or "false" ? stored : row.Default,
            SettingKind.Segmented => row.OptionList.Any(o => o.Value == stored) ? stored : row.Default,
            SettingKind.Icon => IconCatalogue.Resolve(stored),
            _ => row.Default,
        };
    }

    public static string Read(string dbPath, SettingRow row)
        => Resolve(row, row.Key is null ? null : SettingsStore.Get(dbPath, row.Key));

    public static void Write(string dbPath, SettingRow row, string value)
    {
        if (row.Key is null) return;
        SettingsStore.Set(dbPath, row.Key, Resolve(row, value));
    }
}
