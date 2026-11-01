using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Devices;

public static class DeviceInfoService
{
    public static Dictionary<string, (string Name, bool IsLocal)> Load(string dbPath)
    {
        var dict = new Dictionary<string, (string Name, bool IsLocal)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            var shortNames = true;
            using (var sCmd = conn.CreateCommand())
            {
                sCmd.CommandText = "SELECT value FROM settings WHERE key = 'devices.shortNames';";
                var res = sCmd.ExecuteScalar();
                if (res != null && res != DBNull.Value && string.Equals(res.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                {
                    shortNames = false;
                }
            }

            var rawDevices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT device_id, name, platform, is_local, first_seen_utc FROM devices WHERE state != 'forgotten' ORDER BY is_local DESC, first_seen_utc ASC, device_id ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    var name = reader.GetString(1);
                    var platform = reader.GetString(2);
                    var isLocal = reader.GetInt64(3) == 1;
                    var firstSeen = !reader.IsDBNull(4) ? reader.GetString(4) : "";
                    rawDevices.Add((id, name, platform, isLocal, firstSeen));
                }
            }

            var displayNames = ComputeDisplayNames(rawDevices, shortNames);
            foreach (var dev in rawDevices)
            {
                var disp = displayNames.TryGetValue(dev.Id, out var dn) ? dn : (!string.IsNullOrEmpty(dev.Name) ? dev.Name : (string.Equals(dev.Platform, "macos", StringComparison.OrdinalIgnoreCase) ? "Mac" : "PC"));
                dict[dev.Id] = (disp, dev.IsLocal);
            }
        }
        catch { }
        return dict;
    }

    public static Dictionary<string, string> ComputeDisplayNames(
        IReadOnlyList<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)> devices,
        bool shortNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!shortNames)
        {
            foreach (var d in devices)
            {
                var fallback = string.Equals(d.Platform, "macos", StringComparison.OrdinalIgnoreCase) ? "Mac" : "PC";
                result[d.Id] = !string.IsNullOrWhiteSpace(d.Name) ? d.Name : fallback;
            }
            return result;
        }

        var winDevices = devices.Where(d => !string.Equals(d.Platform, "macos", StringComparison.OrdinalIgnoreCase)).ToList();
        var macDevices = devices.Where(d => string.Equals(d.Platform, "macos", StringComparison.OrdinalIgnoreCase)).ToList();

        if (winDevices.Count == 1 && macDevices.Count == 1)
        {
            result[winDevices[0].Id] = "PC";
            result[macDevices[0].Id] = "Mac";
            return result;
        }

        // Windows devices
        if (winDevices.Count == 1)
        {
            result[winDevices[0].Id] = "PC";
        }
        else if (winDevices.Count > 1)
        {
            var orderedWin = winDevices.OrderByDescending(d => d.IsLocal).ThenBy(d => d.FirstSeen).ThenBy(d => d.Id).ToList();
            for (var i = 0; i < orderedWin.Count; i++)
            {
                result[orderedWin[i].Id] = $"PC {i + 1}";
            }
        }

        // Mac devices
        if (macDevices.Count == 1)
        {
            result[macDevices[0].Id] = "Mac";
        }
        else if (macDevices.Count > 1)
        {
            var orderedMac = macDevices.OrderByDescending(d => d.IsLocal).ThenBy(d => d.FirstSeen).ThenBy(d => d.Id).ToList();
            for (var i = 0; i < orderedMac.Count; i++)
            {
                result[orderedMac[i].Id] = $"Mac {i + 1}";
            }
        }

        return result;
    }

    public static bool IsLocal(IReadOnlyDictionary<string, (string Name, bool IsLocal)> info, string? deviceId, string? localDeviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return true;
        if (info.TryGetValue(deviceId, out var item)) return item.IsLocal;
        if (localDeviceId != null) return string.Equals(deviceId, localDeviceId, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static string GetName(IReadOnlyDictionary<string, (string Name, bool IsLocal)> info, string? deviceId, bool isLocal)
    {
        if (!string.IsNullOrEmpty(deviceId) && info.TryGetValue(deviceId, out var item) && !string.IsNullOrEmpty(item.Name))
            return item.Name;
        return isLocal ? "PC" : "Mac";
    }
}

