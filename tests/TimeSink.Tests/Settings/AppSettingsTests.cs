using TimeSink.Core.Alerts;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;
using TimeSink.Core.Theming;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Settings;

public class AppSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.db");

    public AppSettingsTests()
    {
        using var db = Db.Open(_path);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public void A_missing_database_reads_every_setting_as_its_default()
    {
        var settings = AppSettings.ForFile(Path.Combine(Path.GetTempPath(), $"ts-missing-{Guid.NewGuid():N}.db"));
        settings.Get(Setting.WeeklySummary).Should().BeFalse();
        settings.Get(Setting.MinSessionSeconds).Should().Be(15);
        settings.Get(Setting.BackupFolder).Should().BeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)] // not the words: GoalAlerts reads its default, which is true
    [InlineData("", true)]
    public void A_switch_reads_only_the_exact_words_true_and_false(string stored, bool expected)
    {
        new FileSettingsSource(_path).Set(Setting.GoalAlerts.Key, stored);
        AppSettings.ForFile(_path).Get(Setting.GoalAlerts).Should().Be(expected);
    }

    [Fact]
    public void A_switch_stored_in_the_wrong_case_reads_as_its_default_whichever_way_that_goes()
    {
        new FileSettingsSource(_path).Set(Setting.WeeklySummary.Key, "True");
        AppSettings.ForFile(_path).Get(Setting.WeeklySummary).Should().BeFalse();
        new FileSettingsSource(_path).Set(Setting.GoalAlerts.Key, "FALSE");
        AppSettings.ForFile(_path).Get(Setting.GoalAlerts).Should().BeTrue();
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("30", 30)]
    [InlineData("20", 15)]
    [InlineData("-5", 15)]
    [InlineData("abc", 15)]
    public void A_number_setting_reads_only_the_offered_values(string stored, int expected)
    {
        new FileSettingsSource(_path).Set(Setting.MinSessionSeconds.Key, stored);
        AppSettings.ForFile(_path).Get(Setting.MinSessionSeconds).Should().Be(expected);
    }

    [Theory]
    [InlineData("Sat", WeekStart.Sat)]
    [InlineData("sat", WeekStart.Mon)]
    [InlineData("1", WeekStart.Mon)]
    [InlineData("Fri", WeekStart.Mon)]
    public void An_enum_setting_reads_only_an_exact_name(string stored, WeekStart expected)
    {
        new FileSettingsSource(_path).Set(Setting.WeekStartsOn.Key, stored);
        AppSettings.ForFile(_path).Get(Setting.WeekStartsOn).Should().Be(expected);
    }

    [Fact]
    public void The_steam_repair_flag_still_reads_the_one_that_earlier_builds_stored()
    {
        new FileSettingsSource(_path).Set("icons.steamRepaired2", "1");
        AppSettings.ForFile(_path).Get(Setting.SteamIconsRepaired).Should().BeTrue();
    }

    [Fact]
    public void Text_and_count_settings_round_trip_and_an_empty_text_reads_as_null()
    {
        var settings = AppSettings.ForFile(_path);
        settings.Set(Setting.BackupFolder, @"D:\Backups");
        settings.Get(Setting.BackupFolder).Should().Be(@"D:\Backups");
        settings.Set(Setting.BackupFolder, null);
        settings.Get(Setting.BackupFolder).Should().BeNull();
        settings.Get(Setting.BackupLastUtc).Should().Be(0);
        settings.Set(Setting.BackupLastUtc, 1_700_000_000);
        settings.Get(Setting.BackupLastUtc).Should().Be(1_700_000_000);
    }

    [Fact]
    public void Both_adapters_see_the_same_values()
    {
        using var db = Db.Open(_path);
        var viaDb = AppSettings.ForDb(db);
        var viaFile = AppSettings.ForFile(_path);

        viaDb.Set(Setting.Accent, "Pink");
        viaFile.Get(Setting.Accent).Should().Be("Pink");

        viaFile.Set(Setting.LastTab, ShellTab.Year);
        viaDb.Get(Setting.LastTab).Should().Be(ShellTab.Year);

        viaDb.Get(Setting.LastYearView).Should().Be(YearSubView.Pace);
    }

    // ---- the old literals and parsers still agree with the module, until the callers move over ----

    [Fact]
    public void Every_setting_has_its_own_key_and_a_default_it_would_accept()
    {
        Setting.All.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        foreach (var s in Setting.All.Where(s => s.Allowed is not null))
            s.Allowed.Should().Contain(s.DefaultText, s.Key);
    }

    [Fact]
    public void Every_catalogue_row_with_a_value_points_at_a_declared_setting_and_offers_exactly_what_it_accepts()
    {
        foreach (var row in SettingsCatalogue.Rows.Where(r => r.Setting is not null))
        {
            Setting.All.Should().Contain(row.Setting!, row.Id);
            if (row.Kind == SettingKind.Segmented)
                row.OptionList.Select(o => o.Value).Should().Equal(row.Setting!.Allowed, row.Id);
        }
    }

    [Fact]
    public void Keys_that_callers_still_spell_out_match_the_declared_settings()
    {
        AlertEngine.GoalsKey.Should().Be(Setting.GoalAlerts.Key);
        AlertEngine.WeeklyKey.Should().Be(Setting.WeeklySummary.Key);
        AlertEngine.SentKey.Should().Be(Setting.AlertsSent.Key);
        AlertEngine.WeeklyLastKey.Should().Be(Setting.AlertsWeeklyLast.Key);
        ShellTabs.SettingKey.Should().Be(Setting.LastTab.Key);
        YearSubViews.SettingKey.Should().Be(Setting.LastYearView.Key);
        WeekStarts.SettingKey.Should().Be(Setting.WeekStartsOn.Key);
        SteamAutoTracker.SettingKey.Should().Be(Setting.SteamAutoTrack.Key);
    }

    [Fact]
    public void Defaults_that_callers_still_spell_out_match_the_declared_settings()
    {
        ShellTabs.Parse(null).Should().Be(Setting.LastTab.Default);
        YearSubViews.Parse(null).Should().Be(Setting.LastYearView.Default);
        WeekStarts.Parse(null).Should().Be(Setting.WeekStartsOn.Default);
        SteamAutoTracker.Enabled(null).Should().Be(Setting.SteamAutoTrack.Default);
        TodayEarly.SuggestionsEnabled(null).Should().Be(Setting.SuggestApps.Default);

        using var db = Db.Open(_path);
        var tracking = new DbTrackingSettings(db);
        tracking.PauseOnSleep.Should().Be(Setting.PauseOnSleep.Default);
        tracking.CountWhenLocked.Should().Be(Setting.CountWhenLocked.Default);
        tracking.MinSessionSeconds.Should().Be(Setting.MinSessionSeconds.Default);
    }
}
