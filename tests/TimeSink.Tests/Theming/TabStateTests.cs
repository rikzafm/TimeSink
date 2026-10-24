using TimeSink.Core.Data;
using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class TabStateTests
{
    [Theory]
    [InlineData("Week", ShellTab.Week)]
    [InlineData("All", ShellTab.All)]
    [InlineData("Today", ShellTab.Today)]
    [InlineData(null, ShellTab.Today)]
    [InlineData("garbage", ShellTab.Today)]
    public void Stored_tab_parses_with_today_as_default(string? stored, ShellTab expected)
        => ShellTabs.Parse(stored).Should().Be(expected);

    [Fact]
    public void Last_tab_round_trips_through_the_settings_table()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.db");
        try
        {
            using (Db.Open(path)) { }
            SettingsStore.Set(path, "ui.lastTab", "Month");
            SettingsStore.Get(path, "ui.lastTab").Should().Be("Month");
            SettingsStore.Get(path, "ui.missing").Should().BeNull();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    [Fact]
    public void Settings_store_on_a_missing_database_is_quietly_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-none-{Guid.NewGuid():N}.db");
        SettingsStore.Get(path, "ui.lastTab").Should().BeNull();
        SettingsStore.Set(path, "ui.lastTab", "Week"); // must not throw or create the file
        File.Exists(path).Should().BeFalse();
    }
}
