namespace TimeSink.Core.Theming;

/// <summary>
/// String keys of the app detail page (opened by double-clicking a row in the All tab). Kept apart from
/// <see cref="StringKeys"/> like <see cref="AllStringKeys"/>; built-in themes must define all of them.
/// </summary>
public static class AppDetailStringKeys
{
    public const string Back = "detailBack";
    public const string Meta = "detailMeta";
    public const string LastOpened = "detailLastOpened";
    public const string AvgSession = "detailAvgSession";
    public const string TimesOpened = "detailTimesOpened";
    public const string Today = "detailToday";
    public const string Yesterday = "detailYesterday";
    public const string RunningNow = "detailRunningNow";
    public const string LastSub = "detailLastSub";
    public const string LastSubRunning = "detailLastSubRunning";
    public const string Never = "detailNever";
    public const string Longest = "detailLongest";
    public const string TotalIn = "detailTotalIn";
    public const string YearCaption = "detailYearCaption";
    public const string YearHours = "detailYearHours";
    public const string PrevYear = "detailPrevYear";
    public const string NextYear = "detailNextYear";
    public const string Hint = "detailHint";
    public const string DayTracked = "detailDayTracked";
    public const string SessionOne = "detailSessionOne";
    public const string SessionMany = "detailSessionMany";
    public const string NotOpened = "detailNotOpened";
    public const string BeforeTracking = "detailBeforeTracking";
    public const string NotYet = "detailNotYet";
    public const string Footnote = "detailFootnote";
    public const string FootnoteNone = "detailFootnoteNone";

    public static readonly IReadOnlyList<string> All =
    [
        Back, Meta, LastOpened, AvgSession, TimesOpened, Today, Yesterday, RunningNow, LastSub, LastSubRunning, Never,
        Longest, TotalIn, YearCaption, YearHours, PrevYear, NextYear, Hint, DayTracked, SessionOne, SessionMany,
        NotOpened, BeforeTracking, NotYet, Footnote, FootnoteNone,
    ];
}
