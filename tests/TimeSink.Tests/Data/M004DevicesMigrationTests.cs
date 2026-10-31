using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Data.Migrations;

namespace TimeSink.Tests.Data;

public sealed class M004DevicesMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-m004-tests-" + Guid.NewGuid().ToString("N"));

    public M004DevicesMigrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private string NewDbPath(string name = "test.db") => Path.Combine(_dir, name);

    [Fact]
    public void Fresh_database_migrates_to_version_4_with_local_device()
    {
        var path = NewDbPath("fresh.db");
        using var db = Db.Open(path);

        db.SchemaVersion.Should().Be(4);
        var deviceId = db.GetSetting("device.id");
        deviceId.Should().NotBeNullOrWhiteSpace();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name, platform, is_local, state FROM devices WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$id", deviceId);
        using var reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().NotBeNullOrWhiteSpace();
        reader.GetString(1).Should().Match(p => p == "windows" || p == "macos");
        reader.GetInt32(2).Should().Be(1);
        reader.GetString(3).Should().Be("active");
    }

    [Fact]
    public void Migrating_v3_to_v4_preserves_apps_sessions_merges_icons_goals_and_ignored_suggestions()
    {
        var path = NewDbPath("v3_to_v4.db");

        // 1. Create a schema v3 database directly
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            M001_Initial.Apply(conn);
            M002_BackgroundApps.Apply(conn);
            M003_AppKind.Apply(conn);

            using var vCmd = conn.CreateCommand();
            vCmd.CommandText = "PRAGMA user_version = 3;";
            vCmd.ExecuteNonQuery();

            // Insert initial data
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO apps (id, exe_path, display_name, category, state, added_utc, is_background, kind)
                VALUES (1, 'c:\apps\main.exe', 'Main App', 'Games', 'tracked', 1000, 0, 1);

                INSERT INTO apps (id, canonical_id, exe_path, display_name, category, state, added_utc, is_background, kind)
                VALUES (2, 1, 'c:\apps\merged.exe', 'Merged App', 'Games', 'tracked', 1050, 0, 1);

                INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
                VALUES (1, 1, 1000, 2000, 2000, 'exit'),
                       (2, 2, 2100, 2500, 2500, 'exit');

                INSERT INTO daily_usage (app_id, day, seconds) VALUES (1, '2026-03-10', 1000), (2, '2026-03-10', 400);

                INSERT INTO icons (app_id, png, hash) VALUES (1, X'89504E470D0A1A0A', 'hash1');

                INSERT INTO goals (app_id, kind, seconds_per_day) VALUES (1, 'limit', 3600);

                INSERT INTO ignored_suggestions (exe_path, utc) VALUES ('c:\windows\system32\cmd.exe', 9999);
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        // 2. Open with Db.Open which triggers migration M004
        using (var db = Db.Open(path))
        {
            db.SchemaVersion.Should().Be(4);

            var deviceId = db.GetSetting("device.id");
            deviceId.Should().NotBeNullOrWhiteSpace();

            using var cmd = db.Connection.CreateCommand();

            // Verify apps survived and received device_id
            cmd.CommandText = "SELECT id, canonical_id, exe_path, device_id, display_name FROM apps ORDER BY id";
            using (var r = cmd.ExecuteReader())
            {
                r.Read().Should().BeTrue();
                r.GetInt64(0).Should().Be(1);
                r.IsDBNull(1).Should().BeTrue();
                r.GetString(2).Should().Be(@"c:\apps\main.exe");
                r.GetString(3).Should().Be(deviceId);
                r.GetString(4).Should().Be("Main App");

                r.Read().Should().BeTrue();
                r.GetInt64(0).Should().Be(2);
                r.GetInt64(1).Should().Be(1); // canonical_id survived!
                r.GetString(2).Should().Be(@"c:\apps\merged.exe");
                r.GetString(3).Should().Be(deviceId);
                r.GetString(4).Should().Be("Merged App");
            }

            // Verify sessions survived
            cmd.CommandText = "SELECT COUNT(*) FROM sessions";
            ((long)cmd.ExecuteScalar()!).Should().Be(2);

            // Verify daily usage survived
            cmd.CommandText = "SELECT SUM(seconds) FROM daily_usage";
            ((long)cmd.ExecuteScalar()!).Should().Be(1400);

            // Verify icons survived
            cmd.CommandText = "SELECT app_id, hash FROM icons";
            using (var r = cmd.ExecuteReader())
            {
                r.Read().Should().BeTrue();
                r.GetInt64(0).Should().Be(1);
                r.GetString(1).Should().Be("hash1");
            }

            // Verify goals survived
            cmd.CommandText = "SELECT app_id, kind, seconds_per_day FROM goals";
            using (var r = cmd.ExecuteReader())
            {
                r.Read().Should().BeTrue();
                r.GetInt64(0).Should().Be(1);
                r.GetString(1).Should().Be("limit");
                r.GetInt64(2).Should().Be(3600);
            }

            // Verify ignored_suggestions survived
            cmd.CommandText = "SELECT COUNT(*) FROM ignored_suggestions";
            ((long)cmd.ExecuteScalar()!).Should().Be(1);
        }

        // 3. Verify safety backup pre-migrate-*.timesink was created
        var backupDir = Path.Combine(_dir, "backups");
        Directory.Exists(backupDir).Should().BeTrue();
        var safetyBackups = Directory.GetFiles(backupDir, "pre-migrate-*.timesink");
        safetyBackups.Should().NotBeEmpty();
    }

    [Fact]
    public void Running_migration_twice_is_idempotent()
    {
        var path = NewDbPath("idempotent.db");
        using (var db = Db.Open(path))
        {
            db.InsertApp(@"c:\test.exe", "Test", 100);
        }

        using (var db2 = Db.Open(path))
        {
            db2.SchemaVersion.Should().Be(4);
            db2.GetApps().Should().ContainSingle();
        }
    }

    [Fact]
    public void Per_device_app_uniqueness_allows_same_path_on_different_devices()
    {
        var path = NewDbPath("uniqueness.db");
        using var db = Db.Open(path);

        var localId = db.GetSetting("device.id")!;
        const string otherDevice = "other-device-uuid-12345";

        // Register other device
        using (var devCmd = db.Connection.CreateCommand())
        {
            devCmd.CommandText = "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc) VALUES ($id, 'Other PC', 'windows', 0, '2026-03-10T00:00:00Z')";
            devCmd.Parameters.AddWithValue("$id", otherDevice);
            devCmd.ExecuteNonQuery();
        }

        // Insert app for local device
        db.InsertApp(@"c:\tools\app.exe", "App", 1, localId);

        // Same app on SAME device throws SQLite unique constraint exception
        var actSame = () => db.InsertApp(@"C:\Tools\App.EXE", "App Again", 2, localId);
        actSame.Should().Throw<SqliteException>();

        // Same app on DIFFERENT device succeeds!
        var idOther = db.InsertApp(@"c:\tools\app.exe", "App on Other PC", 3, otherDevice);
        idOther.Should().BeGreaterThan(0);
    }
}
