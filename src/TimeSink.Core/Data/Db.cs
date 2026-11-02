using Microsoft.Data.Sqlite;
using TimeSink.Core.Data.Migrations;
using TimeSink.Core.Security;

namespace TimeSink.Core.Data;

public sealed record AppRow(long Id, string ExePath, string DisplayName, string State);

/// <summary>Opens (and migrates) the TimeSink SQLite database.</summary>
public sealed class Db : IDisposable
{
    private static int _batteriesInitialized;
    private static IKeyManager? _keyManager;

    private readonly SqliteConnection _connection;

    static Db()
    {
        InitializeBatteries();
    }

    private Db(SqliteConnection connection) => _connection = connection;

    public static void InitializeBatteries()
    {
        if (Interlocked.Exchange(ref _batteriesInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    public static IKeyManager KeyManager
    {
        get
        {
            if (_keyManager is not null) return _keyManager;
            if (OperatingSystem.IsWindows())
            {
                _keyManager = new DpapiKeyManager();
                return _keyManager;
            }
            throw new PlatformNotSupportedException("KeyManager is not supported on this platform without explicit injection.");
        }
        set => _keyManager = value;
    }

    public static byte[] GetDatabaseKey(byte[]? key = null) => key ?? KeyManager.GetOrCreateDatabaseKey();

    /// <summary>
    /// Creates and opens a new SQLite connection with the SQLCipher encryption key and cipher pragmas applied.
    /// </summary>
    public static SqliteConnection CreateConnection(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate, byte[]? key = null)
    {
        InitializeBatteries();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && mode != SqliteOpenMode.ReadOnly)
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());

        connection.Open();
        try
        {
            key = GetDatabaseKey(key);
            var hexKey = Convert.ToHexString(key);
            Execute(connection, $"PRAGMA key = \"x'{hexKey}'\"; PRAGMA cipher_page_size = 4096; PRAGMA kdf_iter = 256000;");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static Db Open(string path, byte[]? key = null)
    {
        InitializeBatteries();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        EncryptionMigrator.CleanStaleMigrationFiles(path);

        key = GetDatabaseKey(key);

        if (EncryptionMigrator.IsPlaintextDatabase(path))
        {
            EncryptionMigrator.Migrate(path, key);
        }

        var connection = CreateConnection(path, SqliteOpenMode.ReadWriteCreate, key);
        try
        {
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            Migrate(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new Db(connection);
    }

    internal SqliteConnection Connection => _connection;

    public int SchemaVersion => (int)(long)Scalar("PRAGMA user_version")!;

    public string JournalMode => (string)Scalar("PRAGMA journal_mode")!;

    public long InsertApp(string exePath, string displayName, long addedUtc, string? deviceId = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO apps (exe_path, display_name, added_utc, device_id)
            VALUES ($p, $n, $t, COALESCE($d, (SELECT value FROM settings WHERE key = 'device.id'), (SELECT device_id FROM devices WHERE is_local = 1)))
            RETURNING id
            """;
        cmd.Parameters.AddWithValue("$p", exePath);
        cmd.Parameters.AddWithValue("$n", displayName);
        cmd.Parameters.AddWithValue("$t", addedUtc);
        cmd.Parameters.AddWithValue("$d", (object?)deviceId ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public void SetAppState(long appId, string state, long utc)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE apps SET state = $s, retired_utc = CASE WHEN $s = 'retired' THEN $t ELSE NULL END WHERE id = $id";
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$t", utc);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AppRow> GetApps()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, exe_path, display_name, state FROM apps ORDER BY id";
        using var reader = cmd.ExecuteReader();
        var rows = new List<AppRow>();
        while (reader.Read())
            rows.Add(new AppRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    public string? GetSetting(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>The schema version this build writes and understands.</summary>
    public static int LatestSchemaVersion => Migrator.LatestVersion;

    private static void Migrate(SqliteConnection connection)
    {
        if (Migrator.CurrentVersion(connection) > Migrator.LatestVersion)
            throw new InvalidOperationException("The database was written by a newer version of TimeSink.");
        Migrator.Migrate(connection);
    }

    private object? Scalar(string sql) => Scalar(_connection, sql);

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
