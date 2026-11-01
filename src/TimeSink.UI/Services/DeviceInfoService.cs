using Microsoft.Data.Sqlite;

namespace TimeSink.UI.Services;

public static class DeviceInfoService
{
    public static Dictionary<string, (string Name, bool IsLocal)> Load(string dbPath)
    {
        var dict = new Dictionary<string, (string Name, bool IsLocal)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT device_id, name, platform, is_local FROM devices";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var platform = reader.GetString(2);
                var isLocal = reader.GetInt64(3) == 1;
                var shortName = !string.IsNullOrEmpty(name) ? name : (platform == "macos" ? "Mac" : "PC");
                dict[id] = (shortName, isLocal);
            }
        }
        catch { }
        return dict;
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

