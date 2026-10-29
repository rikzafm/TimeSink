namespace TimeSink.Core.Theming;

/// <summary>
/// The wording of the Today hour panel and the Week day panel (decision 157). Built-in themes must define all of them;
/// values with <c>{n}</c> are composed by the view.
/// </summary>
public static class PanelStringKeys
{
    public const string HourTypical = "panelHourTypical";
    public const string HourTypicalValue = "panelHourTypicalValue";
    public const string HourSummary = "panelHourSummary";
    public const string HourEmpty = "panelHourEmpty";
    public const string DayVsAverage = "panelDayVsAverage";
    public const string DayVsLastWeek = "panelDayVsLastWeek";
    public const string DaySpan = "panelDaySpan";
    public const string DayFocus = "panelDayFocus";
    public const string DayNotUsed = "panelDayNotUsed";
    public const string DayOpenInToday = "panelDayOpenInToday";

    public static readonly IReadOnlyList<string> All =
    [
        HourTypical, HourTypicalValue, HourSummary, HourEmpty,
        DayVsAverage, DayVsLastWeek, DaySpan, DayFocus, DayNotUsed, DayOpenInToday,
    ];
}
