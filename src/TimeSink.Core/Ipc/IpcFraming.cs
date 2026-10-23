using System.Buffers.Binary;
using System.Text.Json;

namespace TimeSink.Core.Ipc;

/// <summary>4-byte little-endian length prefix + UTF-8 JSON.</summary>
public static class IpcFraming
{
    private const int MaxFrame = 1 << 20;

    public static async Task WriteAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.IpcMessage);
        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Next message, or null at end of stream. Throws <see cref="InvalidDataException"/> on a bad frame.</summary>
    public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrame) throw new InvalidDataException("IPC frame too large");
        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false)) throw new InvalidDataException("IPC frame truncated");
        try { return JsonSerializer.Deserialize(body, IpcJson.Default.IpcMessage); }
        catch (JsonException ex) { throw new InvalidDataException("IPC frame is not valid", ex); }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
