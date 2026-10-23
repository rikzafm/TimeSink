using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimeSink.Core.Theming;

public interface IThemeSource
{
    /// <summary>Raw theme.json text for an id, or null when there is no such theme.</summary>
    string? Read(string id);
}

/// <summary>Reads <c>&lt;root&gt;/&lt;id&gt;/theme.json</c>.</summary>
public sealed class FolderThemeSource(string root) : IThemeSource
{
    public string? Read(string id)
    {
        if (id.Length == 0 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))) return null;
        var path = Path.Combine(root, id, "theme.json");
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

public sealed class ThemeException(string message) : Exception(message);

/// <summary>Loads and flattens theme.json files. Any failure reverts to <see cref="DefaultId"/>.</summary>
public sealed class ThemeLoader(IThemeSource source)
{
    public const string DefaultId = "pulse-dark";
    private const int MaxDepth = 8;
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);

    public Theme Load(string id)
    {
        try { return Resolve(id, 0); }
        catch (Exception ex) when (ex is ThemeException or JsonException or InvalidOperationException or FormatException)
        {
            if (id == DefaultId) throw; // the shipped default must be valid
            return Resolve(DefaultId, 0);
        }
    }

    private Theme Resolve(string id, int depth)
    {
        if (depth > MaxDepth) throw new ThemeException("extends chain too deep");
        var json = source.Read(id) ?? throw new ThemeException($"theme '{id}' not found");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ThemeException("theme root must be an object");

        var parentId = Str(root, "extends");
        if (parentId == id) throw new ThemeException("theme extends itself");
        var parent = parentId is null ? null : Resolve(parentId, depth + 1);

        var colors = new Dictionary<string, string>(parent?.Colors ?? new Dictionary<string, string>());
        var heat = parent?.Heat;
        var apps = parent?.Apps;
        if (root.TryGetProperty("colors", out var c))
        {
            foreach (var p in c.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    colors[p.Name] = CheckHex(p.Value.GetString()!);
                }
                else if (p.Name == "heat") heat = HexList(p.Value, TokenCatalog.HeatSteps, TokenCatalog.HeatSteps);
                else if (p.Name == "apps") apps = HexList(p.Value, 4, 8);
                else throw new ThemeException($"colour '{p.Name}' has the wrong type");
            }
        }

        var fonts = new Dictionary<string, string>(parent?.Fonts ?? new Dictionary<string, string>());
        var sizes = new Dictionary<string, double>(parent?.Sizes ?? new Dictionary<string, double>());
        if (root.TryGetProperty("type", out var t))
        {
            foreach (var p in t.EnumerateObject())
            {
                if (p.Name is "ui" or "numbers") fonts[p.Name] = p.Value.GetString() ?? throw new ThemeException("font must be a string");
                else if (p.Name == "sizes") foreach (var s in p.Value.EnumerateObject()) sizes[s.Name] = s.Value.GetDouble();
            }
        }

        var radius = parent?.Radius;
        var border = parent?.Border;
        if (root.TryGetProperty("shape", out var shape))
        {
            if (shape.TryGetProperty("radius", out var r))
            {
                var list = r.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                if (list.Length != 3) throw new ThemeException("radius needs exactly 3 values");
                radius = list;
            }
            if (shape.TryGetProperty("border", out var b)) border = b.GetDouble();
        }

        var layout = Str(root, "layout") ?? parent?.Layout ?? "pulse";
        if (layout != "pulse") throw new ThemeException($"layout '{layout}' is not supported");

        var strings = new Dictionary<string, string>();
        if (parent is not null) foreach (var key in AllKeys(parent.Strings)) strings[key.Key] = key.Value;
        if (root.TryGetProperty("strings", out var st))
            foreach (var p in st.EnumerateObject())
                strings[p.Name] = p.Value.GetString() ?? throw new ThemeException("string must be text");

        foreach (var key in TokenCatalog.ColorKeys)
            if (!colors.ContainsKey(key)) throw new ThemeException($"colour '{key}' missing");
        foreach (var key in TokenCatalog.FontKeys)
            if (!fonts.ContainsKey(key)) throw new ThemeException($"font '{key}' missing");
        foreach (var key in TokenCatalog.SizeKeys)
            if (!sizes.ContainsKey(key)) throw new ThemeException($"size '{key}' missing");
        if (heat is null || apps is null || radius is null || border is null)
            throw new ThemeException("theme is incomplete");

        return new Theme(Str(root, "id") ?? id, Str(root, "name") ?? id, colors, heat, apps, fonts, sizes, radius, border.Value, layout,
            new StringTable(strings));
    }

    private static IEnumerable<KeyValuePair<string, string>> AllKeys(StringTable table)
        => table.Entries;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string CheckHex(string s) => Hex.IsMatch(s) ? s : throw new ThemeException($"'{s}' is not a hex colour");

    private static string[] HexList(JsonElement e, int min, int max)
    {
        var list = e.EnumerateArray().Select(x => CheckHex(x.GetString() ?? "")).ToArray();
        if (list.Length < min || list.Length > max) throw new ThemeException("colour list has the wrong length");
        return list;
    }
}
