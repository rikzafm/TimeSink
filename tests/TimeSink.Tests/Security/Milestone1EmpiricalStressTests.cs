using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Security;

namespace TimeSink.Tests.Security;

[SupportedOSPlatform("windows")]
public sealed class Milestone1EmpiricalStressTests : IDisposable
{
    private readonly string _testDir;

    public Milestone1EmpiricalStressTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timesink-stress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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

    // =========================================================================
    // CHALLENGE 1: Rejection of Unauthorized Database Access & High Entropy Verification
    // =========================================================================

    [Fact]
    public void Challenge1_UnauthorizedAccess_WithoutKey_ThrowsSqliteErrorCode26_AndNoPlaintextRemains()
    {
        var dbPath = Path.Combine(_testDir, "challenge1_auth.db");
        var masterKey = RandomNumberGenerator.GetBytes(32);
        const string sensitiveSecret = "SUPER_SECRET_PAYLOAD_TOP_CONFIDENTIAL_123456789";

        // 1. Create and populate encrypted database
        using (var db = Db.Open(dbPath, masterKey))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE secrets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    secret_body TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            for (var i = 0; i < 50; i++)
            {
                using var insertCmd = db.Connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO secrets (title, secret_body) VALUES ($t, $b)";
                insertCmd.Parameters.AddWithValue("$t", $"Secret Title {i}");
                insertCmd.Parameters.AddWithValue("$b", $"{sensitiveSecret}_{i}");
                insertCmd.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();

        // 2. Direct byte inspection of raw file
        File.Exists(dbPath).Should().BeTrue();
        var rawBytes = File.ReadAllBytes(dbPath);
        rawBytes.Length.Should().BeGreaterThanOrEqualTo(4096);

        // Header check: MUST NOT start with "SQLite format 3\0"
        var magicString = Encoding.ASCII.GetString(rawBytes, 0, 16);
        magicString.Should().NotStartWith("SQLite format 3");

        // High entropy check on the first page (4096 bytes)
        var firstPage = rawBytes.AsSpan(0, 4096).ToArray();
        var entropy = CalculateShannonEntropy(firstPage);
        // Ciphertext page should have high entropy (typically > 7.9 bits per byte out of 8.0)
        entropy.Should().BeGreaterThan(7.8, "encrypted page should exhibit high Shannon entropy");

        // Search for sensitive plaintext in the raw binary file
        var sensitiveBytes = Encoding.UTF8.GetBytes(sensitiveSecret);
        ContainsSubarray(rawBytes, sensitiveBytes).Should().BeFalse(
            "sensitive plaintext must not appear anywhere unencrypted in the database file");

        // 3. Connect WITHOUT key -> verify SqliteException with Error 26 (SQLITE_NOTADB)
        using (var noKeyConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            noKeyConn.Open();
            using var cmd = noKeyConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM secrets;";

            var act = () => cmd.ExecuteScalar();
            var ex = act.Should().Throw<SqliteException>().Which;
            ex.SqliteErrorCode.Should().Be(26, "SQLITE_NOTADB expected when connecting without encryption key");
        }

        // 4. Connect with INCORRECT key -> verify SqliteException with Error 26
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        using (var wrongKeyConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadWrite, wrongKey))
        {
            using var cmd = wrongKeyConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM secrets;";

            var act = () => cmd.ExecuteScalar();
            var ex = act.Should().Throw<SqliteException>().Which;
            ex.SqliteErrorCode.Should().Be(26, "SQLITE_NOTADB expected when connecting with incorrect key");
        }

        // 5. Connect with CORRECT key -> succeeds and reads back data
        using (var correctConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadWrite, masterKey))
        {
            using var cmd = correctConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM secrets;";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            count.Should().Be(50);
        }
    }

    // =========================================================================
    // CHALLENGE 2: Concurrency & Key Management Stress Test
    // =========================================================================

    [Fact]
    public void Challenge2_DpapiKeyManager_MultiThreadedStress_IsIdempotent_AndCorruptFree()
    {
        var keyFilePath = Path.Combine(_testDir, "concurrent.dbkey");
        const int threadCount = 64;
        var results = new ConcurrentBag<byte[]>();
        var exceptions = new ConcurrentBag<Exception>();

        // Run 64 concurrent tasks accessing GetOrCreateDatabaseKey() simultaneously
        // on independent DpapiKeyManager instances pointing to the same key file
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, _ =>
        {
            try
            {
                var manager = new DpapiKeyManager(keyFilePath);
                var key = manager.GetOrCreateDatabaseKey();
                results.Add(key);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty("no exceptions should occur during highly concurrent key creation/retrieval");
        results.Count.Should().Be(threadCount);

        var firstKey = results.First();
        firstKey.Should().NotBeNull();
        firstKey.Length.Should().Be(32);

        // Verify that every single thread got the exact same key bytes
        foreach (var key in results)
        {
            key.Should().Equal(firstKey, "all concurrent callers must receive identical key bytes");
        }

        // Verify file on disk is valid DPAPI ciphertext
        File.Exists(keyFilePath).Should().BeTrue();
        var rawEncrypted = File.ReadAllBytes(keyFilePath);
        rawEncrypted.Should().NotEqual(firstKey);

        var decrypted = ProtectedData.Unprotect(rawEncrypted, null, DataProtectionScope.CurrentUser);
        decrypted.Should().Equal(firstKey, "file on disk must decrypt to the exact shared key");

        // Stress-test concurrent reads on existing key
        var readResults = new ConcurrentBag<byte[]>();
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, _ =>
        {
            var manager = new DpapiKeyManager(keyFilePath);
            var key = manager.GetOrCreateDatabaseKey();
            readResults.Add(key);
        });

        readResults.Count.Should().Be(threadCount);
        foreach (var key in readResults)
        {
            key.Should().Equal(firstKey);
        }
    }

    // =========================================================================
    // CHALLENGE 3: Plain-to-Encrypted Migration Stress Test
    // =========================================================================

    [Fact]
    public void Challenge3_PlainToEncryptedMigration_PreservesComplexSchema_Indexes_Triggers_AndData100Percent()
    {
        var plainDbPath = Path.Combine(_testDir, "synthetic_plain.db");
        var backupDir = Path.Combine(_testDir, "migration_backups");
        var masterKey = RandomNumberGenerator.GetBytes(32);
        const int expectedUserVersion = 42;
        const int recordCount = 1000;

        // 1. Build a synthetic unencrypted SQLite database with:
        //    - Multiple relational tables (users, orders, order_items, audit_logs)
        //    - Foreign keys and unique constraints
        //    - Secondary indexes
        //    - A trigger that populates audit_logs on order inserts
        //    - A view that joins orders and users
        //    - WAL journal mode with active WAL frames
        //    - Custom user_version = 42
        using (var plainConn = new SqliteConnection($"Data Source={plainDbPath};Pooling=false"))
        {
            plainConn.Open();
            using var cmd = plainConn.CreateCommand();
            cmd.CommandText = $"""
                PRAGMA journal_mode = WAL;
                PRAGMA user_version = {expectedUserVersion};

                CREATE TABLE users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE,
                    email TEXT NOT NULL,
                    created_at INTEGER NOT NULL
                );

                CREATE TABLE orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL REFERENCES users(id),
                    order_number TEXT NOT NULL UNIQUE,
                    total_cents INTEGER NOT NULL,
                    status TEXT NOT NULL
                );

                CREATE INDEX idx_orders_user ON orders(user_id);
                CREATE INDEX idx_orders_status ON orders(status);

                CREATE TABLE order_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_id INTEGER NOT NULL REFERENCES orders(id),
                    sku TEXT NOT NULL,
                    quantity INTEGER NOT NULL,
                    price_cents INTEGER NOT NULL
                );

                CREATE INDEX idx_order_items_order ON order_items(order_id);

                CREATE TABLE audit_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    action TEXT NOT NULL,
                    details TEXT NOT NULL,
                    log_time INTEGER NOT NULL
                );

                CREATE TRIGGER trg_order_audit
                AFTER INSERT ON orders
                BEGIN
                    INSERT INTO audit_logs (action, details, log_time)
                    VALUES ('ORDER_CREATED', 'Order #' || NEW.order_number, strftime('%s', 'now'));
                END;

                CREATE VIEW v_user_orders AS
                SELECT u.username, o.order_number, o.total_cents, o.status
                FROM users u
                JOIN orders o ON u.id = o.user_id;
                """;
            cmd.ExecuteNonQuery();

            // Populate data inside a transaction
            using var tx = plainConn.BeginTransaction();
            using var insertUser = plainConn.CreateCommand();
            insertUser.Transaction = tx;
            insertUser.CommandText = "INSERT INTO users (username, email, created_at) VALUES ($u, $e, $c) RETURNING id;";
            insertUser.Parameters.Add("$u", SqliteType.Text);
            insertUser.Parameters.Add("$e", SqliteType.Text);
            insertUser.Parameters.Add("$c", SqliteType.Integer);

            using var insertOrder = plainConn.CreateCommand();
            insertOrder.Transaction = tx;
            insertOrder.CommandText = "INSERT INTO orders (user_id, order_number, total_cents, status) VALUES ($uid, $onum, $tot, $st) RETURNING id;";
            insertOrder.Parameters.Add("$uid", SqliteType.Integer);
            insertOrder.Parameters.Add("$onum", SqliteType.Text);
            insertOrder.Parameters.Add("$tot", SqliteType.Integer);
            insertOrder.Parameters.Add("$st", SqliteType.Text);

            using var insertItem = plainConn.CreateCommand();
            insertItem.Transaction = tx;
            insertItem.CommandText = "INSERT INTO order_items (order_id, sku, quantity, price_cents) VALUES ($oid, $sku, $qty, $prc);";
            insertItem.Parameters.Add("$oid", SqliteType.Integer);
            insertItem.Parameters.Add("$sku", SqliteType.Text);
            insertItem.Parameters.Add("$qty", SqliteType.Integer);
            insertItem.Parameters.Add("$prc", SqliteType.Integer);

            for (var i = 1; i <= recordCount; i++)
            {
                insertUser.Parameters["$u"].Value = $"user_{i}";
                insertUser.Parameters["$e"].Value = $"user_{i}@example.com";
                insertUser.Parameters["$c"].Value = 1700000000 + i;
                var userId = (long)insertUser.ExecuteScalar()!;

                insertOrder.Parameters["$uid"].Value = userId;
                insertOrder.Parameters["$onum"].Value = $"ORD-{i:D6}";
                insertOrder.Parameters["$tot"].Value = i * 100;
                insertOrder.Parameters["$st"].Value = i % 2 == 0 ? "completed" : "pending";
                var orderId = (long)insertOrder.ExecuteScalar()!;

                insertItem.Parameters["$oid"].Value = orderId;
                insertItem.Parameters["$sku"].Value = $"SKU-{i % 10}";
                insertItem.Parameters["$qty"].Value = (i % 5) + 1;
                insertItem.Parameters["$prc"].Value = 50 * i;
                insertItem.ExecuteNonQuery();
            }

            tx.Commit();
        }

        SqliteConnection.ClearAllPools();

        // Verify initial state
        EncryptionMigrator.IsPlaintextDatabase(plainDbPath).Should().BeTrue();

        // 2. Execute migration
        EncryptionMigrator.Migrate(plainDbPath, masterKey, backupDir);

        // 3. Verify it is no longer plaintext
        EncryptionMigrator.IsPlaintextDatabase(plainDbPath).Should().BeFalse();

        // Verify safety backup was created in backupDir
        var backupFiles = Directory.GetFiles(backupDir, "pre-encrypt-*.timesink");
        backupFiles.Should().HaveCount(1);
        File.Exists(backupFiles[0]).Should().BeTrue();
        new FileInfo(backupFiles[0]).Length.Should().BeGreaterThan(0);

        // 4. Open migrated database using SQLCipher master key and rigorously verify everything
        using (var encConn = Db.CreateConnection(plainDbPath, SqliteOpenMode.ReadWrite, masterKey))
        {
            // Verify integrity check
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var integrity = cmd.ExecuteScalar() as string;
                integrity.Should().Be("ok");
            }

            // Verify user_version preservation
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                var actualVersion = Convert.ToInt32(cmd.ExecuteScalar());
                actualVersion.Should().Be(expectedUserVersion, "PRAGMA user_version must match the pre-migration value");
            }

            // Verify tables and row counts
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM users;";
                Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(recordCount);

                cmd.CommandText = "SELECT COUNT(*) FROM orders;";
                Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(recordCount);

                cmd.CommandText = "SELECT COUNT(*) FROM order_items;";
                Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(recordCount);

                // Audit logs populated by trigger
                cmd.CommandText = "SELECT COUNT(*) FROM audit_logs;";
                Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(recordCount);

                // Verify view functionality
                cmd.CommandText = "SELECT COUNT(*) FROM v_user_orders;";
                Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(recordCount);
            }

            // Verify indexes were preserved
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'idx_%' ORDER BY name;";
                using var reader = cmd.ExecuteReader();
                var indexNames = new List<string>();
                while (reader.Read())
                {
                    indexNames.Add(reader.GetString(0));
                }
                indexNames.Should().BeEquivalentTo(new[]
                {
                    "idx_order_items_order",
                    "idx_orders_status",
                    "idx_orders_user"
                });
            }

            // Verify data integrity of individual rows
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "SELECT username, email FROM users WHERE id = 500;";
                using var reader = cmd.ExecuteReader();
                reader.Read().Should().BeTrue();
                reader.GetString(0).Should().Be("user_500");
                reader.GetString(1).Should().Be("user_500@example.com");
            }

            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "SELECT order_number, total_cents, status FROM orders WHERE id = 500;";
                using var reader = cmd.ExecuteReader();
                reader.Read().Should().BeTrue();
                reader.GetString(0).Should().Be("ORD-000500");
                reader.GetInt32(1).Should().Be(50000);
                reader.GetString(2).Should().Be("completed");
            }
        }
    }

    [Fact]
    public void Challenge1_CorruptedCiphertext_FailsIntegrityOrThrowsOnRead()
    {
        var dbPath = Path.Combine(_testDir, "challenge1_corrupt.db");
        var masterKey = RandomNumberGenerator.GetBytes(32);

        using (var db = Db.Open(dbPath, masterKey))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE data (val TEXT); INSERT INTO data VALUES ('test_value');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // Mutate ciphertext bytes at offset 128 (corrupting encrypted page data / HMAC)
        var fileBytes = File.ReadAllBytes(dbPath);
        for (var i = 128; i < 144; i++)
        {
            fileBytes[i] ^= 0xFF;
        }
        File.WriteAllBytes(dbPath, fileBytes);

        // Opening with correct key should fail integrity or throw SqliteException when querying
        using var encConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadWrite, masterKey);
        using var cmdCheck = encConn.CreateCommand();
        cmdCheck.CommandText = "SELECT val FROM data;";
        var act = () => cmdCheck.ExecuteScalar();
        act.Should().Throw<SqliteException>("tampered ciphertext must fail HMAC/decryption");
    }

    [Fact]
    public void Challenge1_AttachEncryptedDatabase_WithWrongKey_Fails()
    {
        var dbPathA = Path.Combine(_testDir, "challenge1_attach_a.db");
        var dbPathB = Path.Combine(_testDir, "challenge1_attach_b.db");
        var keyA = RandomNumberGenerator.GetBytes(32);
        var keyB = RandomNumberGenerator.GetBytes(32);

        using (var dbA = Db.Open(dbPathA, keyA))
        {
            using var cmd = dbA.Connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE sample (id INT); INSERT INTO sample VALUES (1);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using (var dbB = Db.Open(dbPathB, keyB))
        {
            var wrongHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var quotedPathA = dbPathA.Replace('\\', '/').Replace("'", "''");
            var act = () =>
            {
                using var cmd = dbB.Connection.CreateCommand();
                cmd.CommandText = $"ATTACH DATABASE '{quotedPathA}' AS attachedDb KEY 'x''{wrongHex}''';";
                cmd.ExecuteNonQuery();
            };

            // SQLCipher fails immediately during ATTACH with error 26
            var ex = act.Should().Throw<SqliteException>().Which;
            ex.SqliteErrorCode.Should().Be(26);
        }
    }

    [Fact]
    public void Challenge2_NestedPathCreation_AndRapidMultiInstanceAccess()
    {
        var nestedDir = Path.Combine(_testDir, "nested", "level1", "level2");
        var keyFilePath = Path.Combine(nestedDir, ".dbkey");
        const int iterations = 32;
        var keys = new ConcurrentBag<byte[]>();

        Parallel.For(0, iterations, _ =>
        {
            var mgr = new DpapiKeyManager(keyFilePath);
            keys.Add(mgr.GetOrCreateDatabaseKey());
        });

        keys.Count.Should().Be(iterations);
        var baseKey = keys.First();
        foreach (var k in keys)
        {
            k.Should().Equal(baseKey);
        }

        // Entropy check on the generated 256-bit key itself
        CalculateShannonEntropy(baseKey).Should().BeGreaterThan(3.5, "random key should have reasonable Shannon entropy");

        // Delete key and recreate
        var finalMgr = new DpapiKeyManager(keyFilePath);
        finalMgr.DeleteDatabaseKey();
        File.Exists(keyFilePath).Should().BeFalse();

        var newKey = finalMgr.GetOrCreateDatabaseKey();
        newKey.Should().NotBeNull();
        newKey.Length.Should().Be(32);
        File.Exists(keyFilePath).Should().BeTrue();
    }

    [Fact]
    public void Challenge3_MigrationIdempotency_AlreadyEncryptedDatabase_IsUntouched()
    {
        var dbPath = Path.Combine(_testDir, "idempotent.db");
        var backupDir = Path.Combine(_testDir, "idempotent_backups");
        var masterKey = RandomNumberGenerator.GetBytes(32);

        using (var db = Db.Open(dbPath, masterKey))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE existing (col TEXT); INSERT INTO existing VALUES ('hello');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var initialModified = File.GetLastWriteTimeUtc(dbPath);
        var initialLength = new FileInfo(dbPath).Length;

        // Run migration on already encrypted database
        EncryptionMigrator.Migrate(dbPath, masterKey, backupDir);

        Directory.Exists(backupDir).Should().BeFalse("no safety backup should be created when database is already encrypted");
        new FileInfo(dbPath).Length.Should().Be(initialLength);

        // Database should still open normally
        using (var db = Db.Open(dbPath, masterKey))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT col FROM existing;";
            var val = cmd.ExecuteScalar() as string;
            val.Should().Be("hello");
        }
    }

    [Fact]
    public void Challenge3_Migration_HandlesSpecialCharactersBlobsNullsEmojis()
    {
        var dbPath = Path.Combine(_testDir, "special_chars.db");
        var backupDir = Path.Combine(_testDir, "special_backups");
        var masterKey = RandomNumberGenerator.GetBytes(32);
        const string specialText = "Special 'quotes' and \"double quotes\" and emojis: 🔥💻🚀 and CJK: タイムシンク and symbols: \t\r\n\\/";
        var blobBytes = new byte[65536];
        RandomNumberGenerator.Fill(blobBytes);

        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            using var cmd = plainConn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE complex_types (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    text_val TEXT,
                    blob_val BLOB,
                    null_val TEXT
                );
                INSERT INTO complex_types (text_val, blob_val, null_val) VALUES ($t, $b, $n);
                """;
            cmd.Parameters.AddWithValue("$t", specialText);
            cmd.Parameters.AddWithValue("$b", blobBytes);
            cmd.Parameters.AddWithValue("$n", DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        EncryptionMigrator.Migrate(dbPath, masterKey, backupDir);

        using (var encConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadWrite, masterKey))
        {
            using var cmd = encConn.CreateCommand();
            cmd.CommandText = "SELECT text_val, blob_val, null_val FROM complex_types WHERE id = 1;";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be(specialText);
            
            var readBlob = (byte[])reader["blob_val"];
            readBlob.Should().Equal(blobBytes);

            reader.IsDBNull(2).Should().BeTrue();
        }
    }

    [Fact]
    public void Challenge3_TimeSinkDb_PlainToEncrypted_AndSafetyBackupRestorable()
    {
        var dbPath = Path.Combine(_testDir, "timesink_legacy.db");
        var backupDir = Path.Combine(_testDir, "legacy_backups");
        var restorePath = Path.Combine(_testDir, "restored.db");
        var masterKey = RandomNumberGenerator.GetBytes(32);

        // 1. Create a TimeSink database in plaintext with initial migrations applied
        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            // Apply standard TimeSink migrations
            Migrator.Migrate(plainConn);

            string localDeviceId;
            using (var devCmd = plainConn.CreateCommand())
            {
                devCmd.CommandText = "SELECT device_id FROM devices WHERE is_local = 1";
                localDeviceId = (string)devCmd.ExecuteScalar()!;
            }

            // Insert apps
            using var insertCmd = plainConn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO apps (exe_path, display_name, added_utc, device_id)
                VALUES ('C:\\apps\\editor.exe', 'Code Editor', 1700000000, $d),
                       ('C:\\apps\\browser.exe', 'Web Browser', 1700000010, $d);
                """;
            insertCmd.Parameters.AddWithValue("$d", localDeviceId);
            insertCmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        // 2. Perform migration to encrypted
        EncryptionMigrator.Migrate(dbPath, masterKey, backupDir);

        // 3. Verify safety backup is inspectable by RestoreService
        var backupFiles = Directory.GetFiles(backupDir, "pre-encrypt-*.timesink");
        backupFiles.Should().HaveCount(1);
        var safetyBackup = backupFiles[0];

        // The pre-migration backup is plaintext, so RestoreService can inspect it
        var inspection = RestoreService.Inspect(safetyBackup);
        inspection.Apps.Should().Be(2);

        // 4. Restore safety backup into fresh database using RestoreService.Replace
        var safetyFolder = Path.Combine(_testDir, "safety_folder");
        Directory.CreateDirectory(safetyFolder);
        RestoreService.Replace(safetyBackup, restorePath, safetyFolder, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, masterKey);

        // 5. Verify the restored database is encrypted with masterKey and contains the apps
        using (var restoredDb = Db.Open(restorePath, masterKey))
        {
            var apps = restoredDb.GetApps();
            apps.Should().HaveCount(2);
            apps.Select(a => a.DisplayName).Should().Contain(new[] { "Code Editor", "Web Browser" });
        }
    }


    private static double CalculateShannonEntropy(byte[] data)
    {
        var frequency = new int[256];
        foreach (var b in data)
        {
            frequency[b]++;
        }

        var entropy = 0.0;
        var len = data.Length;
        foreach (var count in frequency)
        {
            if (count > 0)
            {
                var p = (double)count / len;
                entropy -= p * Math.Log2(p);
            }
        }
        return entropy;
    }

    private static bool ContainsSubarray(byte[] source, byte[] target)
    {
        if (target.Length == 0 || source.Length < target.Length) return false;
        for (var i = 0; i <= source.Length - target.Length; i++)
        {
            var match = true;
            for (var j = 0; j < target.Length; j++)
            {
                if (source[i + j] != target[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
