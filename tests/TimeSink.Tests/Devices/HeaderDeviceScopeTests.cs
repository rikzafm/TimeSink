using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Settings;
using TimeSink.Core.Theming;
using Xunit;

namespace TimeSink.Tests.Devices;

public sealed class HeaderDeviceScopeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public HeaderDeviceScopeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ts-header-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "timesink.db");
        using var db = Db.Open(_dbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Device_tokens_and_shape_rules_are_consistent()
    {
        DeviceTokens.ShapeFor(isLocal: true).Should().Be(DeviceShape.Circle);
        DeviceTokens.ShapeFor(isLocal: false).Should().Be(DeviceShape.Square);

        DeviceTokens.BrushKeyFor(isLocal: true).Should().Be("Brush.DeviceA");
        DeviceTokens.BrushKeyFor(isLocal: false).Should().Be("Brush.DeviceB");
    }

    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Builtin_themes_contain_device_colors_and_scope_strings(string themeId)
    {
        var loader = new ThemeLoader(new FolderThemeSource(Path.Combine(AppContext.BaseDirectory, "themes")));
        var theme = loader.Load(themeId);

        theme.Colors.Should().ContainKey("deviceA");
        theme.Colors.Should().ContainKey("deviceB");

        theme.Strings.Has(StringKeys.ScopeThisPc).Should().BeTrue();
        theme.Strings.Has(StringKeys.ScopeAllDevices).Should().BeTrue();

        theme.Strings.Get(StringKeys.ScopeThisPc).Should().Be("This PC");
        theme.Strings.Get(StringKeys.ScopeAllDevices).Should().Be("All devices");

        theme.Strings.Has(StringKeys.LegendDeviceOn).Should().BeTrue();
        theme.Strings.Has(StringKeys.LegendAsleepGeneric).Should().BeTrue();
        theme.Strings.Has(StringKeys.FooterUpdatedJustNow).Should().BeTrue();
        theme.Strings.Has(StringKeys.FooterSynced).Should().BeTrue();
    }

    [Fact]
    public void Setting_device_scope_reads_and_writes_correctly()
    {
        var settings = AppSettings.ForFile(_dbPath);

        // Default is This
        settings.Get(Setting.DeviceScope).Should().Be(DeviceScope.This);

        // Can set to All
        settings.Set(Setting.DeviceScope, DeviceScope.All);
        settings.Get(Setting.DeviceScope).Should().Be(DeviceScope.All);

        // Persists and re-reads
        var reload = AppSettings.ForFile(_dbPath);
        reload.Get(Setting.DeviceScope).Should().Be(DeviceScope.All);

        // Switch back to This
        reload.Set(Setting.DeviceScope, DeviceScope.This);
        AppSettings.ForFile(_dbPath).Get(Setting.DeviceScope).Should().Be(DeviceScope.This);
    }

    [Fact]
    public void Control_is_hidden_with_single_device_and_sync_off()
    {
        // Initially single local device created by migration, sync is off
        using (var conn = Db.CreateConnection(_dbPath))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE is_local = 0;";
            var remoteCount = (long)(cmd.ExecuteScalar() ?? 0L);
            remoteCount.Should().Be(0);
        }

        var syncOn = AppSettings.ForFile(_dbPath).Get(Setting.SyncEnabled);
        syncOn.Should().BeFalse();

        var shouldShow = CheckShouldShowScopeControl(_dbPath);
        shouldShow.Should().BeFalse("control must be hidden when only 1 device exists and sync is off");
    }

    [Fact]
    public void Control_is_shown_when_another_device_exists_or_sync_is_on()
    {
        // 1. Sync enabled makes it visible
        AppSettings.ForFile(_dbPath).Set(Setting.SyncEnabled, true);
        CheckShouldShowScopeControl(_dbPath).Should().BeTrue("sync on must show scope control");

        // 2. Sync off but remote device added makes it visible
        AppSettings.ForFile(_dbPath).Set(Setting.SyncEnabled, false);
        using (var conn = Db.CreateConnection(_dbPath))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                VALUES ('remote-mac-1', 'MacBook Air', 'macos', 0, '2026-11-01T00:00:00Z', 'active');
                """;
            cmd.ExecuteNonQuery();
        }

        CheckShouldShowScopeControl(_dbPath).Should().BeTrue("remote device presence must show scope control");
    }

    private static bool CheckShouldShowScopeControl(string dbPath)
    {
        var syncOn = AppSettings.ForFile(dbPath).Get(Setting.SyncEnabled);
        if (syncOn) return true;
        try
        {
            using var conn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE is_local = 0;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }
}

