using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Data;

public sealed class EncryptionMigrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _backupDir;
    private readonly byte[] _key;

    public EncryptionMigrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timesink-migration-tests-" + Guid.NewGuid().ToString("N"));
        _backupDir = Path.Combine(_testDir, "backups");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_backupDir);
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void Plaintext_database_migrates_to_encrypted_and_preserves_data()
    {
        var dbPath = Path.Combine(_testDir, "plain.db");

        // 1. Create plaintext database with raw sqlite connection
        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            using var cmd = plainConn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE notes (id INTEGER PRIMARY KEY, content TEXT);
                INSERT INTO notes (content) VALUES ('secret note 1'), ('secret note 2');
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        // Verify it was plaintext
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeTrue();

        // 2. Run Migration directly
        EncryptionMigrator.Migrate(dbPath, _key, _backupDir);

        // Verify it is no longer plaintext
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();

        // 3. Verify user_version was preserved
        using (var encConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadWrite, _key))
        {
            encConn.Open();
            using var cmd = encConn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(1);

            cmd.CommandText = "SELECT COUNT(*) FROM notes;";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(2);
        }

        // 4. Verify safety backup was created
        var backupFiles = Directory.GetFiles(_backupDir, "pre-encrypt-*.timesink");
        backupFiles.Should().HaveCount(1);
    }

    [Fact]
    public void Stale_migrating_files_are_cleaned_up()
    {
        var dbPath = Path.Combine(_testDir, "crashed.db");
        var migratingPath = dbPath + ".migrating";

        File.WriteAllText(dbPath, "fake db");
        File.WriteAllText(migratingPath, "stale temp data");

        File.Exists(migratingPath).Should().BeTrue();

        EncryptionMigrator.CleanStaleMigrationFiles(dbPath);

        File.Exists(migratingPath).Should().BeFalse();
        File.Exists(dbPath).Should().BeTrue();
    }
}


