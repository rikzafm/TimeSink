namespace TimeSink.Core.Theming;

/// <summary>Visible text for a theme. Unknown keys render as a visible placeholder so a gap is noticed, not fatal.</summary>
public sealed class StringTable
{
    private readonly IReadOnlyDictionary<string, string> _values;
    public StringTable(IReadOnlyDictionary<string, string> values) => _values = values;

    public IEnumerable<KeyValuePair<string, string>> Entries => _values;

    public bool Has(string key) => _values.ContainsKey(key);
    public string Get(string key) => _values.TryGetValue(key, out var v) ? v : $"[{key}]";
}

/// <summary>Every string key the shell uses. Built-in themes must define all of them.</summary>
public static class StringKeys
{
    public const string AppName = "appName";
    public const string Settings = "settings";
    public const string TabToday = "tabToday";
    public const string TabWeek = "tabWeek";
    public const string TabMonth = "tabMonth";
    public const string TabYear = "tabYear";
    public const string TabAll = "tabAll";
    public const string AddApp = "addApp";
    public const string Back = "back";
    public const string StubComingSoon = "stubComingSoon";

    public static readonly IReadOnlyList<string> All =
        [AppName, Settings, TabToday, TabWeek, TabMonth, TabYear, TabAll, AddApp, Back, StubComingSoon];
}

/// <summary>The complete list of colour token keys; the UI maps each to a <c>Brush.*</c> resource.</summary>
public static class TokenCatalog
{
    public static readonly IReadOnlyList<string> ColorKeys =
    [
        "surface", "surfaceAlt", "panel", "border", "borderSoft",
        "text", "textSoft", "textMuted", "accent", "onAccent", "warning", "danger",
    ];

    public static readonly IReadOnlyList<string> FontKeys = ["ui", "numbers"];
    public static readonly IReadOnlyList<string> SizeKeys = ["small", "body", "title"];
    public const int HeatSteps = 5;
}

/// <summary>A fully resolved theme (extends chain flattened).</summary>
public sealed record Theme(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<string> Heat,
    IReadOnlyList<string> Apps,
    IReadOnlyDictionary<string, string> Fonts,
    IReadOnlyDictionary<string, double> Sizes,
    IReadOnlyList<double> Radius,
    double Border,
    string Layout,
    StringTable Strings);
