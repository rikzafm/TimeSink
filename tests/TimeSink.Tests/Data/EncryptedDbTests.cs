using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Data;

public sealed class EncryptedDbTests : IDisposable
{
    private readonly string _testDir;
    private readonly byte[] _key;

    public EncryptedDbTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timesink-encrypted-db-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
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
    public void Database_created_by_Db_Open_is_encrypted_and_lacks_sqlite_magic_header()
    {
        var dbPath = Path.Combine(_testDir, "test.db");

        using (var db = Db.Open(dbPath, _key))
        {
            db.SchemaVersion.Should().BeGreaterThan(0);
        }

        File.Exists(dbPath).Should().BeTrue();
        var header = new byte[16];
        using (var fs = File.OpenRead(dbPath))
        {
            fs.ReadExactly(header);
        }

        // Encrypted database must NOT have the standard plaintext SQLite header
        var magicString = Encoding.ASCII.GetString(header);
        magicString.Should().NotStartWith("SQLite format 3");
    }

    [Fact]
    public void Raw_connection_without_key_fails_to_read_encrypted_database()
    {
        var dbPath = Path.Combine(_testDir, "test_nokey.db");

        using (var db = Db.Open(dbPath, _key))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM apps;";
            cmd.ExecuteScalar();
        }

        // Try reading raw database without PRAGMA key
        using var rawConn = new SqliteConnection($"Data Source={dbPath};Pooling=false");
        rawConn.Open();
        using var rawCmd = rawConn.CreateCommand();
        rawCmd.CommandText = "SELECT count(*) FROM sqlite_master;";

        var act = () => rawCmd.ExecuteScalar();
        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Opening_with_incorrect_key_fails()
    {
        var dbPath = Path.Combine(_testDir, "test_wrongkey.db");

        using (var db = Db.Open(dbPath, _key))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM apps;";
            cmd.ExecuteScalar();
        }

        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var act = () =>
        {
            using var db = Db.Open(dbPath, wrongKey);
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            cmd.ExecuteScalar();
        };

        act.Should().Throw<SqliteException>();
    }

    [Fact]
    public void Cipher_parameters_are_configured()
    {
        var dbPath = Path.Combine(_testDir, "test_params.db");

        using var db = Db.Open(dbPath, _key);
        using var cmd = db.Connection.CreateCommand();

        cmd.CommandText = "PRAGMA cipher_page_size;";
        var pageSize = Convert.ToInt32(cmd.ExecuteScalar());
        pageSize.Should().Be(4096);

        cmd.CommandText = "PRAGMA kdf_iter;";
        var kdfIter = Convert.ToInt32(cmd.ExecuteScalar());
        kdfIter.Should().Be(256000);
    }
}
