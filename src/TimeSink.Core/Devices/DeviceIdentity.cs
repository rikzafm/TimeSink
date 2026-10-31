using System.Runtime.InteropServices;
using TimeSink.Core.Data;

namespace TimeSink.Core.Devices;

/// <summary>
/// Manages local device identity (UUID, name, platform) and duplicate/cloned install conflict resolution.
/// </summary>
public static class DeviceIdentity
{
    public static DeviceRecord GetLocalDevice(Db db)
    {
        var id = db.GetSetting("device.id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            db.SetSetting("device.id", id);
        }

        var name = db.GetSetting("device.name");
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = Environment.MachineName; } catch { name = "Unknown Device"; }
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown Device";
            db.SetSetting("device.name", name);
        }

        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "macos";
        var nowUtc = DateTimeOffset.UtcNow;

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ($id, $name, $platform, 1, $now, 'active')
            ON CONFLICT(device_id) DO UPDATE SET is_local = 1, state = 'active'
            RETURNING first_seen_utc, last_import_utc, last_file_utc, state;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToString("O"));

        using var r = cmd.ExecuteReader();
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
