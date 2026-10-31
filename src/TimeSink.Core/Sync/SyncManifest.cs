using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TimeSink.Core.Sync;

/// <summary>
/// The manifest stored inside each &lt;deviceId&gt;.tssync archive (shared/sync-format.md section 3).
/// </summary>
public sealed record SyncManifest(
    int Format,
    int SchemaVersion,
    string AppVersion,
    string DeviceId,
    string DeviceName,
    string Platform,
    DateTimeOffset CreatedUtc,
    int SessionCount,
    string Hash)
{
    public const string EntryName = "manifest.json";
    public const string DbEntryName = "snapshot.db";

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("appVersion", AppVersion);
            w.WriteString("deviceId", DeviceId);
            w.WriteString("deviceName", DeviceName);
            w.WriteString("platform", Platform);
            w.WriteString("createdUtc", CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            w.WriteNumber("sessionCount", SessionCount);
            w.WriteString("hash", Hash);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static SyncManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new SyncManifest(
                r.GetProperty("format").GetInt32(),
                r.GetProperty("schemaVersion").GetInt32(),
                r.TryGetProperty("appVersion", out var appVer) ? appVer.GetString() ?? "" : "",
                r.GetProperty("deviceId").GetString() ?? "",
                r.GetProperty("deviceName").GetString() ?? "",
                r.GetProperty("platform").GetString() ?? "",
                DateTimeOffset.Parse(r.GetProperty("createdUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                r.GetProperty("sessionCount").GetInt32(),
                r.GetProperty("hash").GetString() ?? ""
            );
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new FormatException("The sync manifest is invalid or unreadable.", ex);
        }
    }
}
