namespace TimeSink.Core.Icons;

public enum IconStyle { Clean, Comic, Misprint }

/// <param name="Id"><c>style.motif</c>, the value stored in <c>ui.icon</c>.</param>
public sealed record IconEntry(string Id, IconStyle Style, string Name);

/// <summary>Every selectable app icon (tray icon). The picker, the Agent and the settings row all read this list.</summary>
public static class IconCatalogue
{
    public const string DefaultId = "comic.ticks";

    public static readonly IReadOnlyList<IconEntry> All =
    [
        new("clean.bars", IconStyle.Clean, "Bars"),
        new("clean.gauge", IconStyle.Clean, "Gauge"),
        new("clean.funnel", IconStyle.Clean, "Funnel"),
        new("clean.marker", IconStyle.Clean, "Marker"),
        new("clean.ticks", IconStyle.Clean, "Ticks"),
        new("clean.dashed", IconStyle.Clean, "Dashed"),
        new("clean.layers", IconStyle.Clean, "Layers"),
        new("clean.ripple", IconStyle.Clean, "Ripple"),

        new("comic.ticks", IconStyle.Comic, "Ticks"),

        new("misprint.ticks", IconStyle.Misprint, "Ticks"),
        new("misprint.t", IconStyle.Misprint, "T"),
        new("misprint.web", IconStyle.Misprint, "Web"),
        new("misprint.cursor", IconStyle.Misprint, "Cursor"),
        new("misprint.film", IconStyle.Misprint, "Film strip"),
        new("misprint.rings", IconStyle.Misprint, "Rings"),
        new("misprint.burst", IconStyle.Misprint, "Burst"),
    ];

    public static IEnumerable<IconEntry> InStyle(IconStyle style) => All.Where(i => i.Style == style);

    public static IconEntry Find(string id) => All.Single(i => i.Id == id);

    /// <summary>A stored id if it names a known icon (exact match), else the default.</summary>
    public static string Resolve(string? stored)
        => stored is not null && All.Any(i => i.Id == stored) ? stored : DefaultId;

    /// <summary>The multi-size ICO for an icon (16/20/24/32/48 plus a 256 frame), embedded in this assembly.</summary>
    public static Stream OpenIcon(string id)
        => typeof(IconCatalogue).Assembly.GetManifestResourceStream($"TimeSink.Icons.{Find(id).Id}.ico")
           ?? throw new InvalidOperationException($"Icon asset missing for {id}");

    /// <summary>The PNG frame of an icon whose size is the smallest one at least <paramref name="size"/> (else the largest).</summary>
    public static byte[] ReadFrame(string id, int size)
    {
        using var s = OpenIcon(id);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var ico = ms.ToArray();
        var count = BitConverter.ToUInt16(ico, 4);
        (int Size, int Length, int Offset)? best = null;
        for (var i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            var frame = (Size: ico[e] == 0 ? 256 : ico[e], Length: (int)BitConverter.ToUInt32(ico, e + 8), Offset: (int)BitConverter.ToUInt32(ico, e + 12));
            if (best is null || (best.Value.Size < size ? frame.Size > best.Value.Size : frame.Size >= size && frame.Size < best.Value.Size))
                best = frame;
        }
        return ico.AsSpan(best!.Value.Offset, best.Value.Length).ToArray();
    }
}
