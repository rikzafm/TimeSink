using System.Runtime.InteropServices;
using TimeSink.Core.Data;

namespace TimeSink.Core.Devices;

/// <summary>
/// Manages local device identity (UUID, name, platform) and duplicate/cloned install conflict resolution.
/// </summary>
public static class DeviceIdentity
{
    public static DeviceRecord GetLocalDevice(Db db) => GetLocalDevice(db.Connection);

    public static DeviceRecord GetLocalDevice(System.Data.Common.DbConnection connection)
    {
        string? id;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM settings WHERE key = 'device.id';";
            id = cmd.ExecuteScalar() as string;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('device.id', $id) ON CONFLICT(key) DO UPDATE SET value = $id;";
            var p = cmd.CreateParameter();
            p.ParameterName = "$id";
            p.Value = id;
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        string? name;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM settings WHERE key = 'device.name';";
            name = cmd.ExecuteScalar() as string;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = Environment.MachineName; } catch { name = "Unknown Device"; }
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown Device";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('device.name', $n) ON CONFLICT(key) DO UPDATE SET value = $n;";
            var p = cmd.CreateParameter();
            p.ParameterName = "$n";
            p.Value = name;
            cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }

        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "macos";
        var nowUtc = DateTimeOffset.UtcNow;

        using var insCmd = connection.CreateCommand();
        insCmd.CommandText = """
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ($id, $name, $platform, 1, $now, 'active')
            ON CONFLICT(device_id) DO UPDATE SET is_local = 1, state = 'active'
            RETURNING first_seen_utc, last_import_utc, last_file_utc, state;
            """;
        var p1 = insCmd.CreateParameter(); p1.ParameterName = "$id"; p1.Value = id; insCmd.Parameters.Add(p1);
        var p2 = insCmd.CreateParameter(); p2.ParameterName = "$name"; p2.Value = name; insCmd.Parameters.Add(p2);
        var p3 = insCmd.CreateParameter(); p3.ParameterName = "$platform"; p3.Value = platform; insCmd.Parameters.Add(p3);
        var p4 = insCmd.CreateParameter(); p4.ParameterName = "$now"; p4.Value = nowUtc.ToString("O"); insCmd.Parameters.Add(p4);

        using var r = insCmd.ExecuteReader();
        r.Read();
        var firstSeen = DateTimeOffset.Parse(r.GetString(0));
        DateTimeOffset? lastImport = r.IsDBNull(1) ? null : DateTimeOffset.Parse(r.GetString(1));
        DateTimeOffset? lastFile = r.IsDBNull(2) ? null : DateTimeOffset.Parse(r.GetString(2));
        var state = r.GetString(3);

        return new DeviceRecord(id, name, platform, true, firstSeen, lastImport, lastFile, state);
    }

    public static void RenameLocalDevice(Db db, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Device name cannot be empty", nameof(newName));

        var id = db.GetSetting("device.id") ?? throw new InvalidOperationException("No local device configured");
        db.SetSetting("device.name", newName);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE devices SET name = $n WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$n", newName);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void RenameDevice(Db db, string deviceId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Device name cannot be empty", nameof(newName));

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE devices SET name = $n WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$n", newName);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.ExecuteNonQuery();

        if (db.GetSetting("device.id") == deviceId)
            db.SetSetting("device.name", newName);
    }

    public static void ForgetDevice(Db db, string deviceId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE devices SET state = 'forgotten' WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteDeviceHistory(Db db, string deviceId)
    {
        using var tx = db.Connection.BeginTransaction();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = $id);
                DELETE FROM daily_usage WHERE app_id IN (SELECT id FROM apps WHERE device_id = $id);
                DELETE FROM apps WHERE device_id = $id;
                DELETE FROM devices WHERE device_id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// When an incoming snapshot file has the exact same device ID as the local device,
    /// a cloned install has occurred. The local device regenerates its UUID and rebinds its apps.
    /// </summary>
    public static string HandleClonedDeviceConflict(Db db, string incomingSnapshotDeviceId)
    {
        var currentLocalId = db.GetSetting("device.id");
        if (string.IsNullOrEmpty(currentLocalId)) return "";

        if (string.Equals(currentLocalId, incomingSnapshotDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            var newId = Guid.NewGuid().ToString();
            var name = db.GetSetting("device.name") ?? "Cloned Device";
            var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "macos";
            var nowUtc = DateTimeOffset.UtcNow.ToString("O");

            db.SetSetting("device.id", newId);

            using var tx = db.Connection.BeginTransaction();

            // Insert new local device
            using (var ins = db.Connection.CreateCommand())
            {
                ins.CommandText = """
                    INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                    VALUES ($newId, $name, $platform, 1, $now, 'active');
                    """;
                ins.Parameters.AddWithValue("$newId", newId);
                ins.Parameters.AddWithValue("$name", name);
                ins.Parameters.AddWithValue("$platform", platform);
                ins.Parameters.AddWithValue("$now", nowUtc);
                ins.ExecuteNonQuery();
            }

            // Remap local apps to the new local device id
            using (var upApps = db.Connection.CreateCommand())
            {
                upApps.CommandText = "UPDATE apps SET device_id = $newId WHERE device_id = $oldId";
                upApps.Parameters.AddWithValue("$newId", newId);
                upApps.Parameters.AddWithValue("$oldId", currentLocalId);
                upApps.ExecuteNonQuery();
            }

            // Demote the old cloned device to remote (is_local = 0)
            using (var upOld = db.Connection.CreateCommand())
            {
                upOld.CommandText = "UPDATE devices SET is_local = 0 WHERE device_id = $oldId";
                upOld.Parameters.AddWithValue("$oldId", currentLocalId);
                upOld.ExecuteNonQuery();
            }

            tx.Commit();

            return $"Cloned device identity detected ({currentLocalId}). Regenerated new local device ID: {newId}.";
        }

        return "";
    }
}
