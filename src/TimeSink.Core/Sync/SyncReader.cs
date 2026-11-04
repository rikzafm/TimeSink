using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;

namespace TimeSink.Core.Sync;

/// <summary>
/// Validates and imports &lt;deviceId&gt;.tssync snapshot archives (shared/sync-format.md section 5).
/// </summary>
public static partial class SyncReader
{
    [GeneratedRegex(@"(\(\d+\)| - Copy|-conflict-|\.sync-conflict-|conflicted copy)", RegexOptions.IgnoreCase)]
    private static partial Regex ConflictRegex();

    public static bool IsConflictCopy(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return ConflictRegex().IsMatch(name);
    }

    public static bool IsPlaceholder(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        // An empty file ending in .tssync is an unhydrated stub/placeholder
        try
        {
            var info = new FileInfo(filePath);
            if (info.Length == 0 && filePath.EndsWith(SyncWriter.Extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }

        // Test marker seam
        if (File.Exists(filePath + ".placeholder"))
            return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var attr = File.GetAttributes(filePath);
                const int recallOnOpen = 0x00040000;
                const int recallOnDataAccess = 0x00400000;
                if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 ||
                    ((int)attr & (recallOnOpen | recallOnDataAccess)) != 0)
                {
                    return true;
                }
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                if (IsMacDataless(filePath)) return true;
            }
            catch { }
        }

        return false;
    }

    private static bool IsMacDataless(string filePath)
    {
        try
        {
            var statBuf = new byte[144];
            if (stat64(filePath, statBuf) == 0)
            {
                // st_flags is at byte offset 124 in Darwin 64-bit struct stat
                var flags = BitConverter.ToUInt32(statBuf, 124);
                const uint sfDataless = 0x40000000;
                if ((flags & sfDataless) != 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    [DllImport("libSystem.dylib", EntryPoint = "stat$INODE64", SetLastError = true)]
    private static extern int stat64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] statBuf);

    public static SyncImportResult Import(string syncFile, string dbPath, TimeZoneInfo zone, IKeyManager? keyManager = null)
    {
        if (!File.Exists(syncFile))
            return new SyncImportResult(SyncStatus.Failed, "File not found", 0, 0, 0, 0);

        if (IsConflictCopy(syncFile))
            return new SyncImportResult(SyncStatus.ConflictCopy, "Cloud conflict copy ignored", 0, 0, 0, 0);

        if (IsPlaceholder(syncFile))
            return new SyncImportResult(SyncStatus.Pending, "Cloud placeholder pending download", 0, 0, 0, 0);

        var scratchDir = Path.Combine(Path.GetTempPath(), "timesink-sync-in-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);

        keyManager ??= Db.KeyManager;

        try
        {
            SyncManifest manifest;
            var extractedDbPath = Path.Combine(scratchDir, SyncManifest.DbEntryName);

            try
            {
                using var zip = ZipFile.OpenRead(syncFile);
                var manifestEntry = zip.GetEntry(SyncManifest.EntryName);
                if (manifestEntry == null)
                    return new SyncImportResult(SyncStatus.BadHash, "Missing manifest.json", 0, 0, 0, 0);

                using (var s = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                {
                    manifest = SyncManifest.Parse(s.ReadToEnd());
                }

                var dbEntry = zip.GetEntry(SyncManifest.DbEntryName);
                if (dbEntry == null)
                    return new SyncImportResult(SyncStatus.BadHash, "Missing snapshot.db", 0, 0, 0, 0);

                dbEntry.ExtractToFile(extractedDbPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
            {
                return new SyncImportResult(SyncStatus.BadHash, "Archive is damaged or unreadable: " + ex.Message, 0, 0, 0, 0);
            }

            // Verify SHA-256 hash
            string computedHash;
            using (var sha256 = SHA256.Create())
            using (var fs = File.OpenRead(extractedDbPath))
            {
                var hashBytes = sha256.ComputeHash(fs);
                computedHash = Convert.ToHexStringLower(hashBytes);
            }

            if (!string.Equals(computedHash, manifest.Hash, StringComparison.OrdinalIgnoreCase))
                return new SyncImportResult(SyncStatus.BadHash, "Snapshot hash mismatch", 0, 0, 0, 0);

            // Format & schema version check (supports schema 5)
            var latestSupportedSchema = Math.Max(5, Migrator.Shipped.Max(m => m.Version));
            if (manifest.Format > 1 || manifest.SchemaVersion > latestSupportedSchema)
                return new SyncImportResult(SyncStatus.NewerFormat, $"Update required (format {manifest.Format}, schema {manifest.SchemaVersion})", 0, 0, 0, 0);

            // Encryption verification & integrity check
            string? passphrase = null;
            if (manifest.Encrypted)
            {
                if (!keyManager.HasSyncPassphrase() || string.IsNullOrEmpty(keyManager.GetSyncPassphrase()))
                {
                    return new SyncImportResult(SyncStatus.EncryptionKeyMissing, "Sync encryption key missing", 0, 0, 0, 0);
                }

                passphrase = keyManager.GetSyncPassphrase()!;

                using (var snapConn = new SqliteConnection(new SqliteConnectionStringBuilder
                       { DataSource = extractedDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    snapConn.Open();
                    try
                    {
                        var escPass = passphrase.Replace("'", "''");
                        using var pragma = snapConn.CreateCommand();
                        pragma.CommandText = $"PRAGMA key = '{escPass}';";
                        pragma.ExecuteNonQuery();

                        using var cmd = snapConn.CreateCommand();
                        cmd.CommandText = "PRAGMA integrity_check;";
                        var integrity = cmd.ExecuteScalar() as string;
                        if (integrity != "ok")
                        {
                            return new SyncImportResult(SyncStatus.EncryptionKeyWrong, "Incorrect sync passphrase or damaged snapshot", 0, 0, 0, 0);
                        }
                    }
                    catch (SqliteException)
                    {
                        return new SyncImportResult(SyncStatus.EncryptionKeyWrong, "Incorrect sync passphrase", 0, 0, 0, 0);
                    }
                }
            }
            else
            {
                // Check if local device expects encryption
                var localEncrypted = AppSettings.ForFile(dbPath).Get(Setting.SyncEncrypted);
                if (localEncrypted)
                {
                    return new SyncImportResult(SyncStatus.RemoteNotEncrypted, "Remote sync archive is not encrypted", 0, 0, 0, 0);
                }

                using (var snapConn = new SqliteConnection(new SqliteConnectionStringBuilder
                       { DataSource = extractedDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    snapConn.Open();
                    using var cmd = snapConn.CreateCommand();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var integrity = cmd.ExecuteScalar() as string;
                    if (integrity != "ok")
                        return new SyncImportResult(SyncStatus.BadHash, "snapshot.db failed integrity check", 0, 0, 0, 0);
                }
            }

            // Validate against local database
            using var db = Db.Open(dbPath);
            var conn = db.Connection;
            var local = DeviceIdentity.GetLocalDevice(conn);

            // Self-file check
            if (string.Equals(manifest.DeviceId, local.DeviceId, StringComparison.OrdinalIgnoreCase))
                return new SyncImportResult(SyncStatus.SelfFile, "Self snapshot ignored", 0, 0, 0, 0);

            // Remote device count and state checks
            string? existingState = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT state FROM devices WHERE device_id = $id;";
                cmd.Parameters.AddWithValue("$id", manifest.DeviceId);
                existingState = cmd.ExecuteScalar() as string;
            }

            if (existingState == "forgotten")
                return new SyncImportResult(SyncStatus.DeviceForgotten, "Device is forgotten", 0, 0, 0, 0);

            if (existingState == null)
            {
                // Check if another active remote device already exists
                long activeRemoteCount;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE is_local = 0 AND state = 'active';";
                    activeRemoteCount = (long)(cmd.ExecuteScalar() ?? 0L);
                }

                if (activeRemoteCount >= 1)
                    return new SyncImportResult(SyncStatus.ThirdDeviceIgnored, "Only two devices are supported yet", 0, 0, 0, 0);
            }

            // Perform ingestion inside a single transaction
            int appsAdded = 0;
            int sessionsAdded = 0;
            int sessionsUpdated = 0;
            int sessionsSkipped = 0;

            var escapedPath = extractedDbPath.Replace('\\', '/').Replace("'", "''");
            using (var cmd = conn.CreateCommand())
            {
                if (manifest.Encrypted)
                {
                    var escPass = passphrase!.Replace("'", "''");
                    cmd.CommandText = $"ATTACH DATABASE '{escapedPath}' AS src KEY '{escPass}';";
                }
                else
                {
                    cmd.CommandText = $"ATTACH DATABASE '{escapedPath}' AS src KEY '';";
                }
                cmd.ExecuteNonQuery();
            }

            try
            {
                using var tx = conn.BeginTransaction();

                var nowIso = DateTimeOffset.UtcNow.ToString("O");
                var fileUtcIso = manifest.CreatedUtc.ToString("O");

                // 1. Upsert device record
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        INSERT INTO main.devices (device_id, name, platform, is_local, first_seen_utc, last_import_utc, last_file_utc, state)
                        VALUES ($id, $name, $platform, 0, $now, $now, $fileUtc, 'active')
                        ON CONFLICT(device_id) DO UPDATE SET
                            name = excluded.name,
                            platform = excluded.platform,
                            last_import_utc = excluded.last_import_utc,
                            last_file_utc = excluded.last_file_utc;
                        """;
                    cmd.Parameters.AddWithValue("$id", manifest.DeviceId);
                    cmd.Parameters.AddWithValue("$name", manifest.DeviceName);
                    cmd.Parameters.AddWithValue("$platform", manifest.Platform);
                    cmd.Parameters.AddWithValue("$now", nowIso);
                    cmd.Parameters.AddWithValue("$fileUtc", fileUtcIso);
                    cmd.ExecuteNonQuery();
                }

                // 2. Map & insert apps
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE TEMP TABLE idmap (src_id INTEGER PRIMARY KEY, dst_id INTEGER NOT NULL);";
                    cmd.ExecuteNonQuery();
                }

                var newlyInsertedApps = new List<(long SrcId, long DstId, long? CanonicalId)>();
                var snapshotApps = new List<(long SrcId, long DstId, long? CanonicalId)>();
                using (var cmd = conn.CreateCommand())
                {
                    bool hasUpdatedUtcInSrc = false;
                    using (var checkSrcCmd = conn.CreateCommand())
                    {
                        checkSrcCmd.CommandText = "PRAGMA src.table_info(apps);";
                        using var pragmaReader = checkSrcCmd.ExecuteReader();
                        while (pragmaReader.Read())
                        {
                            if (string.Equals(pragmaReader.GetString(1), "updated_utc", StringComparison.OrdinalIgnoreCase))
                            {
                                hasUpdatedUtcInSrc = true;
                                break;
                            }
                        }
                    }
                    cmd.CommandText = hasUpdatedUtcInSrc
                        ? "SELECT id, canonical_id, exe_path, bundle_id, device_id, package_family, display_name, category, state, added_utc, retired_utc, is_background, kind, updated_utc FROM src.apps;"
                        : "SELECT id, canonical_id, exe_path, bundle_id, device_id, package_family, display_name, category, state, added_utc, retired_utc, is_background, kind, 0 FROM src.apps;";
                    using var r = cmd.ExecuteReader();
                    var srcApps = new List<object?[]>();
                    while (r.Read())
                    {
                        var row = new object?[14];
                        r.GetValues(row);
                        srcApps.Add(row);
                    }
                    r.Close();

                    foreach (var a in srcApps)
                    {
                        var srcId = (long)a[0]!;
                        var canonicalId = a[1] as long?;
                        var exePath = (string)a[2]!;
                        var bundleId = a[3] as string;
                        var devId = (string)a[4]!;
                        var packageFamily = a[5] as string;
                        var displayName = (string)a[6]!;
                        var category = (string)a[7]!;
                        var state = (string)a[8]!;
                        var addedUtc = (long)a[9]!;
                        var retiredUtc = a[10] as long?;
                        var isBackground = Convert.ToInt32(a[11] ?? 0);
                        var kind = a[12] as long?;
                        var effectiveKind = kind ?? (long)AppKinds.Detect(exePath, displayName);
                        var updatedUtc = Convert.ToInt64(a[13] ?? 0);

                        long dstId;
                        using (var findCmd = conn.CreateCommand())
                        {
                            findCmd.CommandText = "SELECT id FROM main.apps WHERE device_id = $devId AND exe_path = $path COLLATE NOCASE;";
                            findCmd.Parameters.AddWithValue("$devId", devId);
                            findCmd.Parameters.AddWithValue("$path", exePath);
                            var existingId = findCmd.ExecuteScalar();

                            if (existingId is null && !string.IsNullOrEmpty(bundleId))
                            {
                                using var findBundleCmd = conn.CreateCommand();
                                findBundleCmd.CommandText = "SELECT id FROM main.apps WHERE device_id = $devId AND bundle_id = $bundleId;";
                                findBundleCmd.Parameters.AddWithValue("$devId", devId);
                                findBundleCmd.Parameters.AddWithValue("$bundleId", bundleId);
                                existingId = findBundleCmd.ExecuteScalar();
                            }

                            if (existingId is long id)
                            {
                                dstId = id;
                                long localUpdatedUtc = 0;
                                using (var checkLocalCmd = conn.CreateCommand())
                                {
                                    checkLocalCmd.CommandText = "SELECT updated_utc FROM main.apps WHERE id = $id";
                                    checkLocalCmd.Parameters.AddWithValue("$id", dstId);
                                    var obj = checkLocalCmd.ExecuteScalar();
                                    if (obj is long lu) localUpdatedUtc = lu;
                                }

                                if (string.Equals(devId, manifest.DeviceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    using var updCmd = conn.CreateCommand();
                                    updCmd.CommandText = """
                                        UPDATE main.apps SET
                                            exe_path = $p,
                                            bundle_id = CASE WHEN $b IS NOT NULL AND $b != '' THEN $b ELSE bundle_id END,
                                            package_family = CASE WHEN $f IS NOT NULL AND $f != '' THEN $f ELSE package_family END,
                                            display_name = CASE WHEN $updUtc >= $localUpdUtc THEN $n ELSE display_name END,
                                            category = CASE WHEN $updUtc >= $localUpdUtc THEN $c ELSE category END,
                                            state = $s,
                                            retired_utc = $r,
                                            is_background = CASE
                                                WHEN $updUtc >= $localUpdUtc THEN (CASE WHEN $bg != 0 THEN 1 ELSE 0 END)
                                                WHEN $bg != 0 THEN 1
                                                WHEN is_background != 0 THEN 1
                                                WHEN canonical_id IS NOT NULL AND (SELECT is_background FROM main.apps WHERE id = main.apps.canonical_id) != 0 THEN 1
                                                ELSE 0
                                            END,
                                            kind = CASE
                                                WHEN $updUtc >= $localUpdUtc THEN (CASE WHEN $k != 0 THEN $k ELSE $k END)
                                                WHEN $k != 0 THEN $k
                                                WHEN kind IS NOT NULL AND kind != 0 THEN kind
                                                WHEN canonical_id IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id) IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id) != 0
                                                    THEN (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id)
                                                ELSE $k
                                            END,
                                            updated_utc = CASE WHEN $updUtc >= $localUpdUtc THEN $updUtc ELSE updated_utc END
                                        WHERE id = $id;
                                        """;
                                    updCmd.Parameters.AddWithValue("$p", exePath);
                                    updCmd.Parameters.AddWithValue("$b", (object?)bundleId ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$f", (object?)packageFamily ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$n", displayName);
                                    updCmd.Parameters.AddWithValue("$c", category);
                                    updCmd.Parameters.AddWithValue("$s", state);
                                    updCmd.Parameters.AddWithValue("$r", (object?)retiredUtc ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$bg", isBackground);
                                    updCmd.Parameters.AddWithValue("$k", effectiveKind);
                                    updCmd.Parameters.AddWithValue("$id", dstId);
                                    updCmd.Parameters.AddWithValue("$updUtc", updatedUtc);
                                    updCmd.Parameters.AddWithValue("$localUpdUtc", localUpdatedUtc);
                                    updCmd.ExecuteNonQuery();
                                }
                                else if (updatedUtc > localUpdatedUtc)
                                {
                                    using var updRemoteCmd = conn.CreateCommand();
                                    updRemoteCmd.CommandText = """
                                        UPDATE main.apps SET
                                            display_name = $n,
                                            category = $c,
                                            is_background = $bg,
                                            kind = $k,
                                            updated_utc = $updUtc
                                        WHERE id = $id;
                                        """;
                                    updRemoteCmd.Parameters.AddWithValue("$n", displayName);
                                    updRemoteCmd.Parameters.AddWithValue("$c", category);
                                    updRemoteCmd.Parameters.AddWithValue("$bg", isBackground);
                                    updRemoteCmd.Parameters.AddWithValue("$k", effectiveKind);
                                    updRemoteCmd.Parameters.AddWithValue("$updUtc", updatedUtc);
                                    updRemoteCmd.Parameters.AddWithValue("$id", dstId);
                                    updRemoteCmd.ExecuteNonQuery();

                                    using var propCmd = conn.CreateCommand();
                                    propCmd.CommandText = """
                                        UPDATE main.apps SET
                                            category = $c,
                                            kind = $k,
                                            is_background = $bg,
                                            updated_utc = $updUtc
                                        WHERE (canonical_id = $id OR id = (SELECT canonical_id FROM main.apps WHERE id = $id))
                                          AND updated_utc <= $updUtc;
                                        """;
                                    propCmd.Parameters.AddWithValue("$c", category);
                                    propCmd.Parameters.AddWithValue("$k", effectiveKind);
                                    propCmd.Parameters.AddWithValue("$bg", isBackground);
                                    propCmd.Parameters.AddWithValue("$updUtc", updatedUtc);
                                    propCmd.Parameters.AddWithValue("$id", dstId);
                                    propCmd.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                using var insCmd = conn.CreateCommand();
                                insCmd.CommandText = """
                                    INSERT INTO main.apps (
                                        exe_path, bundle_id, device_id, package_family, display_name,
                                        category, state, added_utc, retired_utc, is_background, kind, updated_utc
                                    ) VALUES (
                                        $p, $b, $d, $f, $n, $c, $s, $a, $r, $bg, $k, $updUtc
                                    ) RETURNING id;
                                    """;
                                insCmd.Parameters.AddWithValue("$p", exePath);
                                insCmd.Parameters.AddWithValue("$b", (object?)bundleId ?? DBNull.Value);
                                insCmd.Parameters.AddWithValue("$d", devId);
                                insCmd.Parameters.AddWithValue("$f", (object?)packageFamily ?? DBNull.Value);
                                insCmd.Parameters.AddWithValue("$n", displayName);
                                insCmd.Parameters.AddWithValue("$c", category);
                                insCmd.Parameters.AddWithValue("$s", state);
                                insCmd.Parameters.AddWithValue("$a", addedUtc);
                                insCmd.Parameters.AddWithValue("$r", (object?)retiredUtc ?? DBNull.Value);
                                insCmd.Parameters.AddWithValue("$bg", isBackground);
                                insCmd.Parameters.AddWithValue("$k", effectiveKind);
                                insCmd.Parameters.AddWithValue("$updUtc", updatedUtc);
                                dstId = (long)insCmd.ExecuteScalar()!;
                                appsAdded++;
                                newlyInsertedApps.Add((srcId, dstId, canonicalId));
                            }
                        }

                        snapshotApps.Add((srcId, dstId, canonicalId));

                        using var mapCmd = conn.CreateCommand();
                        mapCmd.CommandText = "INSERT INTO temp.idmap VALUES ($src, $dst);";
                        mapCmd.Parameters.AddWithValue("$src", srcId);
                        mapCmd.Parameters.AddWithValue("$dst", dstId);
                        mapCmd.ExecuteNonQuery();
                    }
                }

                // 3. Remap canonical links for all snapshot apps (intra-device merges)
                foreach (var (_, dstId, srcCanonical) in snapshotApps)
                {
                    if (srcCanonical is { } sc)
                    {
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.CommandText = """
                            UPDATE main.apps
                            SET canonical_id = (SELECT dst_id FROM temp.idmap WHERE src_id = $sc)
                            WHERE id = $dst;
                            """;
                        updateCmd.Parameters.AddWithValue("$sc", sc);
                        updateCmd.Parameters.AddWithValue("$dst", dstId);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        using var unmergeCmd = conn.CreateCommand();
                        unmergeCmd.CommandText = """
                            UPDATE main.apps
                            SET canonical_id = NULL
                            WHERE id = $dst AND canonical_id IN (SELECT dst_id FROM temp.idmap);
                            """;
                        unmergeCmd.Parameters.AddWithValue("$dst", dstId);
                        unmergeCmd.ExecuteNonQuery();
                    }
                }

                // 3b. Sync cross-device merges
                bool hasCrossMergesTable = false;
                using (var checkCmd = conn.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM src.sqlite_master WHERE type='table' AND name='cross_device_merges';";
                    hasCrossMergesTable = Convert.ToInt64(checkCmd.ExecuteScalar()) > 0;
                }

                if (hasCrossMergesTable)
                {
                    long? FindAppInMain(string targetDev, string targetExe, string targetBundle)
                    {
                        using var fCmd = conn.CreateCommand();
                        fCmd.CommandText = "SELECT id FROM main.apps WHERE device_id = $d AND exe_path = $p COLLATE NOCASE LIMIT 1;";
                        fCmd.Parameters.AddWithValue("$d", targetDev);
                        fCmd.Parameters.AddWithValue("$p", targetExe);
                        var found = fCmd.ExecuteScalar() as long?;
                        if (found.HasValue) return found;

                        if (!string.IsNullOrEmpty(targetBundle))
                        {
                            using var fbCmd = conn.CreateCommand();
                            fbCmd.CommandText = "SELECT id FROM main.apps WHERE device_id = $d AND bundle_id = $b LIMIT 1;";
                            fbCmd.Parameters.AddWithValue("$d", targetDev);
                            fbCmd.Parameters.AddWithValue("$b", targetBundle);
                            return fbCmd.ExecuteScalar() as long?;
                        }
                        return null;
                    }

                    var validCrossSubordinates = new HashSet<long>();
                    using (var readCrossCmd = conn.CreateCommand())
                    {
                        readCrossCmd.CommandText = "SELECT source_device_id, source_exe_path, source_bundle_id, target_device_id, target_exe_path, target_bundle_id FROM src.cross_device_merges;";
                        using var r = readCrossCmd.ExecuteReader();
                        while (r.Read())
                        {
                            var sDev = r.GetString(0);
                            var sExe = r.GetString(1);
                            var sBundle = r.GetString(2);
                            var tDev = r.GetString(3);
                            var tExe = r.GetString(4);
                            var tBundle = r.GetString(5);

                            var sId = FindAppInMain(sDev, sExe, sBundle);
                            var tId = FindAppInMain(tDev, tExe, tBundle);
                            if (sId.HasValue && tId.HasValue && sId.Value != tId.Value)
                            {
                                using var upCross = conn.CreateCommand();
                                upCross.CommandText = """
                                    UPDATE main.apps
                                    SET canonical_id = $t,
                                        kind = CASE
                                            WHEN (SELECT kind FROM main.apps WHERE id = $t) IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = $t) != 0
                                                THEN (SELECT kind FROM main.apps WHERE id = $t)
                                            WHEN kind IS NOT NULL AND kind != 0 THEN kind
                                            ELSE 0
                                        END,
                                        is_background = CASE
                                            WHEN (SELECT is_background FROM main.apps WHERE id = $t) != 0 THEN 1
                                            WHEN is_background != 0 THEN 1
                                            ELSE 0
                                        END
                                    WHERE id = $s;

                                    UPDATE main.apps
                                    SET kind = (SELECT kind FROM main.apps WHERE id = $s),
                                        is_background = CASE WHEN (SELECT is_background FROM main.apps WHERE id = $s) != 0 THEN 1 ELSE is_background END
                                    WHERE id = $t
                                      AND (kind IS NULL OR kind = 0)
                                      AND (SELECT kind FROM main.apps WHERE id = $s) IS NOT NULL
                                      AND (SELECT kind FROM main.apps WHERE id = $s) != 0;
                                    """;
                                upCross.Parameters.AddWithValue("$t", tId.Value);
                                upCross.Parameters.AddWithValue("$s", sId.Value);
                                upCross.ExecuteNonQuery();
                                validCrossSubordinates.Add(sId.Value);
                            }
                        }
                    }

                    // If a subordinate app from this snapshot's device was cross-device merged with another device,
                    // but is NOT in the snapshot's cross_device_merges table anymore, unmerge it.
                    using (var unmergeCrossCmd = conn.CreateCommand())
                    {
                        unmergeCrossCmd.CommandText = """
                            SELECT a.id FROM main.apps a
                            JOIN main.apps c ON c.id = a.canonical_id
                            WHERE a.device_id = $devId AND c.device_id != $devId;
                            """;
                        unmergeCrossCmd.Parameters.AddWithValue("$devId", manifest.DeviceId);
                        var staleIds = new List<long>();
                        using (var r = unmergeCrossCmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var id = r.GetInt64(0);
                                if (!validCrossSubordinates.Contains(id)) staleIds.Add(id);
                            }
                        }
                        foreach (var sid in staleIds)
                        {
                            using var clearCmd = conn.CreateCommand();
                            clearCmd.CommandText = "UPDATE main.apps SET canonical_id = NULL WHERE id = $id;";
                            clearCmd.Parameters.AddWithValue("$id", sid);
                            clearCmd.ExecuteNonQuery();
                        }
                    }
                }

                // 3c. Harmonize kind and is_background across all merged groups
                using (var harmCmd = conn.CreateCommand())
                {
                    harmCmd.CommandText = """
                        UPDATE main.apps
                        SET kind = (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id)
                        WHERE canonical_id IS NOT NULL
                          AND (kind IS NULL OR kind = 0)
                          AND (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id) IS NOT NULL
                          AND (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id) != 0;

                        UPDATE main.apps
                        SET kind = (SELECT a.kind FROM main.apps a WHERE a.canonical_id = apps.id AND a.kind IS NOT NULL AND a.kind != 0 LIMIT 1)
                        WHERE (kind IS NULL OR kind = 0)
                          AND EXISTS (SELECT 1 FROM main.apps a WHERE a.canonical_id = apps.id AND a.kind IS NOT NULL AND a.kind != 0);

                        UPDATE main.apps
                        SET is_background = 1
                        WHERE is_background = 0
                          AND canonical_id IS NOT NULL
                          AND (SELECT is_background FROM main.apps c WHERE c.id = apps.canonical_id) != 0;

                        UPDATE main.apps
                        SET is_background = 1
                        WHERE is_background = 0
                          AND EXISTS (SELECT 1 FROM main.apps a WHERE a.canonical_id = apps.id AND a.is_background != 0);
                        """;
                    harmCmd.ExecuteNonQuery();
                }

                // 4. Merge icons
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        INSERT OR IGNORE INTO main.icons (app_id, png, hash)
                        SELECT m.dst_id, i.png, i.hash
                        FROM src.icons i
                        JOIN temp.idmap m ON m.src_id = i.app_id;
                        """;
                    cmd.ExecuteNonQuery();
                }

                // 5. Gather existing sessions for this device to track added/updated/skipped stats
                var existingSessions = new Dictionary<(long AppId, long StartUtc), (long? EndUtc, long LastSeenUtc)>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT s.app_id, s.start_utc, s.end_utc, s.last_seen_utc
                        FROM main.sessions s
                        JOIN main.apps a ON a.id = s.app_id
                        WHERE a.device_id = $devId;
                        """;
                    cmd.Parameters.AddWithValue("$devId", manifest.DeviceId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var appId = r.GetInt64(0);
                        var startUtc = r.GetInt64(1);
                        var endUtc = r.IsDBNull(2) ? (long?)null : r.GetInt64(2);
                        var lastSeen = r.GetInt64(3);
                        existingSessions[(appId, startUtc)] = (endUtc, lastSeen);
                    }
                }

                // 6. Ingest sessions with ON CONFLICT DO UPDATE
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        INSERT INTO main.sessions (app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason)
                        SELECT
                            m.dst_id,
                            s.start_utc,
                            COALESCE(s.end_utc, s.last_seen_utc),
                            s.last_seen_utc,
                            s.pid,
                            s.proc_start_utc,
                            COALESCE(s.end_reason, 'restore')
                        FROM src.sessions s
                        JOIN temp.idmap m ON m.src_id = s.app_id
                        ON CONFLICT(app_id, start_utc) DO UPDATE SET
                            end_utc = CASE
                                WHEN COALESCE(excluded.end_utc, excluded.last_seen_utc) > COALESCE(main.sessions.end_utc, main.sessions.last_seen_utc)
                                THEN excluded.end_utc
                                ELSE main.sessions.end_utc
                            END,
                            last_seen_utc = MAX(main.sessions.last_seen_utc, excluded.last_seen_utc),
                            end_reason = CASE
                                WHEN excluded.last_seen_utc >= main.sessions.last_seen_utc AND excluded.end_reason IS NOT NULL
                                THEN excluded.end_reason
                                ELSE main.sessions.end_reason
                            END;
                        """;
                    cmd.ExecuteNonQuery();
                }

                // 7. Calculate stats from src.sessions vs existingSessions
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT m.dst_id, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc), s.last_seen_utc
                        FROM src.sessions s
                        JOIN temp.idmap m ON m.src_id = s.app_id;
                        """;
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var dstId = r.GetInt64(0);
                        var startUtc = r.GetInt64(1);
                        var effectiveEnd = r.GetInt64(2);
                        var lastSeen = r.GetInt64(3);

                        if (!existingSessions.TryGetValue((dstId, startUtc), out var prev))
                        {
                            sessionsAdded++;
                        }
                        else
                        {
                            var prevEnd = prev.EndUtc ?? prev.LastSeenUtc;
                            if (effectiveEnd > prevEnd || lastSeen > prev.LastSeenUtc)
                            {
                                sessionsUpdated++;
                            }
                            else
                            {
                                sessionsSkipped++;
                            }
                        }
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DROP TABLE temp.idmap;";
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            finally
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DETACH DATABASE src;";
                cmd.ExecuteNonQuery();
            }

            // Rebuild daily usage
            new UsageAccumulator(new SqliteSessionStore(db), zone).RebuildAll();

            return new SyncImportResult(SyncStatus.Success, null, appsAdded, sessionsAdded, sessionsUpdated, sessionsSkipped);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(scratchDir, true); } catch (IOException) { }
        }
    }
}
