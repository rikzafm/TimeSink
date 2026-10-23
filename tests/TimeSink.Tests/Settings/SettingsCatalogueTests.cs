using TimeSink.Core.Data;
using TimeSink.Core.Settings;

namespace TimeSink.Tests.Settings;

public class SettingsCatalogueTests
{
    [Fact]
    public void Sections_appear_in_the_designed_order()
        => SettingsCatalogue.Sections.Should().Equal("General", "Tracking", "Appearance", "Notifications", "Backup and data", "About");

    [Theory]
    [InlineData("startup.enabled", "true")]
    [InlineData("week.start", "Mon")]
    [InlineData("tracking.pauseOnSleep", "true")]
    [InlineData("tracking.countWhenLocked", "true")]
    [InlineData("tracking.minSessionSeconds", "15")]
    [InlineData("tracking.suggestApps", "true")]
    [InlineData("ui.theme", "System")]
    [InlineData("ui.accent", "Lime")]
    [InlineData("ui.compact", "false")]
    [InlineData("alerts.goals", "true")]
    [InlineData("alerts.weekly", "false")]
    [InlineData("backup.mode", "Daily")]
    public void Persisted_rows_use_the_spec_key_and_default(string key, string expectedDefault)
        => SettingsCatalogue.Rows.Single(r => r.Key == key).Default.Should().Be(expectedDefault);

    [Fact]
    public void Every_row_has_a_label_and_ids_are_unique()
    {
        SettingsCatalogue.Rows.Should().OnlyContain(r => r.Label.Length > 0);
        SettingsCatalogue.Rows.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void There_is_no_check_for_updates_row()
        => SettingsCatalogue.Rows.Should().NotContain(r => r.Label.Contains("update", StringComparison.OrdinalIgnoreCase)
                                                          || r.Id.Contains("update", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Switch_and_segmented_rows_are_persisted_while_buttons_and_info_are_not()
    {
        SettingsCatalogue.Rows.Where(r => r.Kind is SettingKind.Switch or SettingKind.Segmented).Should().OnlyContain(r => r.Key != null);
        SettingsCatalogue.Rows.Where(r => r.Kind is SettingKind.Button or SettingKind.Info).Should().OnlyContain(r => r.Key == null);
    }

    [Theory]
    [InlineData("ui.theme", null, "System")]
    [InlineData("ui.theme", "Light", "Light")]
    [InlineData("ui.theme", "Purple", "System")]
    [InlineData("tracking.minSessionSeconds", "30", "30")]
    [InlineData("tracking.minSessionSeconds", "7", "15")]
    [InlineData("alerts.weekly", "true", "true")]
    [InlineData("alerts.weekly", "maybe", "false")]
    public void Stored_values_resolve_to_a_valid_value_or_the_default(string key, string? stored, string expected)
        => SettingsCatalogue.Resolve(SettingsCatalogue.Rows.Single(r => r.Key == key), stored).Should().Be(expected);

    [Fact]
    public void Values_round_trip_through_a_temp_database_and_ticket_05_keys_still_work()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.db");
        try
        {
            using (Db.Open(path)) { }
            var accent = SettingsCatalogue.Rows.Single(r => r.Key == "ui.accent");
            SettingsCatalogue.Read(path, accent).Should().Be("Lime");
            SettingsCatalogue.Write(path, accent, "Pink");
            SettingsCatalogue.Read(path, accent).Should().Be("Pink");
            SettingsStore.Get(path, "ui.accent").Should().Be("Pink");
            SettingsStore.Set(path, "ui.lastTab", "Year");
            SettingsStore.Get(path, "ui.lastTab").Should().Be("Year");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
