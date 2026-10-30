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
    public const string GamesOnly = "gamesOnly";
    public const string StubComingSoon = "stubComingSoon";

    // Today view. Values with {0} are composed by the view (string.Format).
    public const string TodayCaption = "todayCaption";
    public const string TodayCombined = "todayCombined";
    public const string TodayEarlyCaption = "todayEarlyCaption";
    public const string TodayEarlyHint = "todayEarlyHint";
    public const string TodayEmpty = "todayEmpty";
    public const string RunningPill = "runningPill";
    public const string LegendAppsOpen = "legendAppsOpen";
    public const string LegendTypical = "legendTypical";
    public const string LegendAsleep = "legendAsleep";
    public const string TileFirstApp = "tileFirstApp";
    public const string TileLongest = "tileLongest";
    public const string TileVsYesterday = "tileVsYesterday";
    public const string TileNone = "tileNone";
    public const string TileLongestValue = "tileLongestValue";
    public const string TileVsYesterdayValue = "tileVsYesterdayValue";
    public const string StatusRunning = "statusRunning";
    public const string StatusClosed = "statusClosed";
    public const string SessionsToday = "sessionsToday";
    public const string SessionLine = "sessionLine";
    public const string SessionNow = "sessionNow";
    public const string VsAverage = "vsAverage";
    public const string VsAverageNone = "vsAverageNone";
    public const string DialSessions = "dialSessions";
    public const string DialLongest = "dialLongest";
    public const string DialAverage = "dialAverage";
    public const string DialBlips = "dialBlips";
    public const string DialLabel = "dialLabel";

    // Month view.
    public const string MonthCombined = "monthCombined";
    public const string MonthSpanSoFar = "monthSpanSoFar";
    public const string MonthSpanFull = "monthSpanFull";
    public const string MonthDeltaVs = "monthDeltaVs";
    public const string MonthAvgPrefix = "monthAvgPrefix";
    public const string MonthAvgSuffix = "monthAvgSuffix";
    public const string MonthBestPrefix = "monthBestPrefix";
    public const string MonthWeek = "monthWeek";
    public const string MonthLess = "monthLess";
    public const string MonthMore = "monthMore";
    public const string MonthMostly = "monthMostly";
    public const string MonthSpread = "monthSpread";
    public const string MonthDayEmpty = "monthDayEmpty";
    public const string MonthEmpty = "monthEmpty";
    public const string MonthNewApp = "monthNewApp";
    public const string MonthExport = "monthExport";
    public const string MonthExportDone = "monthExportDone";

    public static readonly IReadOnlyList<string> MonthKeys =
    [
        MonthCombined, MonthSpanSoFar, MonthSpanFull, MonthDeltaVs, MonthAvgPrefix, MonthAvgSuffix, MonthBestPrefix,
        MonthWeek, MonthLess, MonthMore, MonthMostly, MonthSpread,MonthDayEmpty, MonthEmpty, MonthNewApp, MonthExport, MonthExportDone,
    ];

    // Week view.
    public const string WeekCaption = "weekCaption";
    public const string WeekLastCaption = "weekLastCaption";
    public const string WeekStreak = "weekStreak";
    public const string WeekStreakSub = "weekStreakSub";
    public const string WeekGridCaption = "weekGridCaption";
    public const string WeekEmpty = "weekEmpty";
    public const string WeekVsLastUp = "weekVsLastUp";
    public const string WeekVsLastDown = "weekVsLastDown";
    public const string WeekAvg = "weekAvg";
    public const string WeekTrendCaption = "weekTrendCaption";

    public static readonly IReadOnlyList<string> All =
    [
        AppName, Settings, TabToday, TabWeek, TabMonth, TabYear, TabAll, AddApp, Back, GamesOnly, StubComingSoon,
        TodayCaption, TodayCombined, TodayEarlyCaption,TodayEarlyHint, TodayEmpty, RunningPill, LegendAppsOpen, LegendTypical,
        LegendAsleep, TileFirstApp, TileLongest, TileVsYesterday, TileNone, TileLongestValue, TileVsYesterdayValue,
        StatusRunning, StatusClosed, SessionsToday, SessionLine, SessionNow, VsAverage, VsAverageNone,
        DialSessions, DialLongest, DialAverage, DialBlips, DialLabel,
        WeekCaption, WeekLastCaption, WeekStreak, WeekStreakSub, WeekGridCaption, WeekEmpty, WeekVsLastUp, WeekVsLastDown, WeekAvg, WeekTrendCaption,
        .. MonthKeys,
    ];
}

/// <summary>The complete list of colour token keys; the UI maps each to a <c>Brush.*</c> resource.</summary>
public static class TokenCatalog
{
    public static readonly IReadOnlyList<string> ColorKeys =
    [
        "surface", "surfaceAlt", "panel", "border", "borderSoft",
        "text", "textSoft", "textMuted", "accent", "onAccent", "warning", "danger", "scrim",
    ];

    public static readonly IReadOnlyList<string> FontKeys = ["ui", "numbers"];
    public static readonly IReadOnlyList<string> SizeKeys = ["small", "body", "title", "display"];
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
