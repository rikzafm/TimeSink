using TimeSink.Core.Data;

namespace TimeSink.Core.Settings;

public enum SettingKind { Switch, Segmented, Button, Info, Icon }

public sealed record SettingOption(string Value, string Label);

/// <summary>
/// One row of the Settings page. <see cref="Setting"/> is the stored setting behind a Switch/Segmented/Icon row
/// (null for Button/Info rows, which carry no stored value); the row shows its key and default.
/// </summary>
public sealed record SettingRow(
    string Id, string Section, string Label, string Description, SettingKind Kind,
    ISetting? Setting = null, IReadOnlyList<SettingOption>? Options = null, bool Danger = false)
{
    public string? Key => Setting?.Key;
    public string Default => Setting?.DefaultText ?? "";
    public IReadOnlyList<SettingOption> OptionList => Options ?? [];
}

/// <summary>The single declarative source for the Settings page (implementation.md section 8.3, key table 5.1).</summary>
public static class SettingsCatalogue
{
    public const string General = "General", Tracking = "Tracking", Appearance = "Appearance",
        Notifications = "Notifications", BackupAndData = "Backup and data", About = "About";

    public static readonly IReadOnlyList<string> Sections = [General, Tracking, Appearance, Notifications, BackupAndData, About];

    /// <summary>One option per value the setting accepts, labelled by the value itself unless <paramref name="label"/> says otherwise.</summary>
    private static SettingOption[] Options(ISetting setting, Func<string, string>? label = null)
        => setting.Allowed!.Select(v => new SettingOption(v, label?.Invoke(v) ?? v)).ToArray();

    public static readonly IReadOnlyList<SettingRow> Rows =
    [
        new("startup", General, "Start with Windows", "Launch TimeSink quietly when you sign in.", SettingKind.Switch, Setting.Startup),
        new("weekStart", General, "Week starts on", "Used by the Week tab and weekly totals.", SettingKind.Segmented, Setting.WeekStartsOn, Options(Setting.WeekStartsOn)),

        new("pauseOnSleep", Tracking, "Pause while the PC sleeps", "Time stops counting while the computer is asleep.", SettingKind.Switch, Setting.PauseOnSleep),
        new("countWhenLocked", Tracking, "Keep counting when locked", "Apps still running behind the lock screen keep adding up.", SettingKind.Switch, Setting.CountWhenLocked),
        new("minSession", Tracking, "Ignore short sessions", "Sessions shorter than this are dropped.", SettingKind.Segmented, Setting.MinSessionSeconds, Options(Setting.MinSessionSeconds, v => v + "s")),
        new("suggestApps", Tracking, "Suggest apps I use", "Offer running apps you have not added yet.", SettingKind.Switch, Setting.SuggestApps),

        new("steamAutoTrack", Tracking, "Track Steam games", "Add newly installed Steam games automatically.", SettingKind.Switch, Setting.SteamAutoTrack),

        new("theme", Appearance, "Theme", "Follows Windows when set to System.", SettingKind.Segmented, Setting.Theme, Options(Setting.Theme)),
        new("accent", Appearance, "Accent", "The highlight colour used across the app.", SettingKind.Segmented, Setting.Accent, Options(Setting.Accent)),
        new("compact", Appearance, "Compact lists", "Tighter rows in app lists.", SettingKind.Switch, Setting.Compact),
        new("appIcon", Appearance, "App icon", "How TimeSink looks in the tray.", SettingKind.Icon, Setting.AppIcon),

        new("goalAlerts", Notifications, "Goal and limit alerts", "A notification when a goal or limit is reached.", SettingKind.Switch, Setting.GoalAlerts),
        new("weeklySummary", Notifications, "Weekly summary", "A short recap at the start of each week.", SettingKind.Switch, Setting.WeeklySummary),

        new("backupMode", BackupAndData, "Automatic backup", "Keeps the last 7 plus one per month.", SettingKind.Segmented, Setting.BackupMode, Options(Setting.BackupMode)),
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
        => row.Setting is { } setting && row.Kind is SettingKind.Switch or SettingKind.Segmented or SettingKind.Icon
            ? setting.Resolve(stored)
            : row.Default;

    public static string Read(string dbPath, SettingRow row)
        => Resolve(row, row.Key is null ? null : SettingsStore.Get(dbPath, row.Key));

    public static void Write(string dbPath, SettingRow row, string value)
    {
        if (row.Key is null) return;
        SettingsStore.Set(dbPath, row.Key, Resolve(row, value));
    }
}
