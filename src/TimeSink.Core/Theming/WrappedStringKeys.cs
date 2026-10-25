namespace TimeSink.Core.Theming;

/// <summary>
/// String keys of the Year > Wrapped sub-view (apart from <see cref="StringKeys"/> and <see cref="YearStringKeys"/> so
/// parallel tickets do not collide). Built-in themes must define all of them; values with <c>{n}</c> are composed by the view.
/// </summary>
public static class WrappedStringKeys
{
    public const string Card1Label = "wrapCard1Label";
    public const string Equivalent = "wrapEquivalent";
    public const string And = "wrapAnd";
    public const string Day = "wrapDay";
    public const string Days = "wrapDays";
    public const string Hour = "wrapHour";
    public const string Hours = "wrapHours";
    public const string TileTracked = "wrapTileTracked";
    public const string TileActive = "wrapTileActive";
    public const string TileApps = "wrapTileApps";

    public const string Card2Label = "wrapCard2Label";
    public const string TopHeadline = "wrapTopHeadline";
    public const string Then = "wrapThen";
    public const string ShortHours = "wrapShortHours";

    public const string Card3Label = "wrapCard3Label";
    public const string HabitsHeadline = "wrapHabitsHeadline";
    public const string Streak = "wrapStreak";
    public const string StreakSpan = "wrapStreakSpan";
    public const string StreakOne = "wrapStreakOne";
    public const string Busiest = "wrapBusiest";
    public const string BusiestValue = "wrapBusiestValue";
    public const string BusiestVs = "wrapBusiestVs";
    public const string BusiestOnly = "wrapBusiestOnly";
    public const string Peak = "wrapPeak";
    public const string PeakValue = "wrapPeakValue";
    public const string PeakSub = "wrapPeakSub";
    public const string Noon = "wrapNoon";
    public const string NoonSub = "wrapNoonSub";
    public const string NoonSubEarly = "wrapNoonSubEarly";

    public const string CardNameTotal = "wrapCardNameTotal";
    public const string CardNameTop = "wrapCardNameTop";
    public const string CardNameHabits = "wrapCardNameHabits";
    public const string CardNameGames = "wrapCardNameGames";
    public const string CardNameGacha = "wrapCardNameGacha";
    public const string Card4Label = "wrapCard4Label";
    public const string GamesHeadline = "wrapGamesHeadline";
    public const string Card5Label = "wrapCard5Label";
    public const string GachaHeadline = "wrapGachaHeadline";
    public const string NoGames = "wrapNoGames";
    public const string NoGacha = "wrapNoGacha";
    public const string Prev = "wrapPrev";
    public const string Next = "wrapNext";
    public const string GoTo = "wrapGoTo";
    public const string Save = "wrapSave";
    public const string SaveDone = "wrapSaveDone";
    public const string SaveTitle = "wrapSaveTitle";
    public const string SaveFilter = "wrapSaveFilter";

    public static readonly IReadOnlyList<string> All =
    [
        Card1Label, Equivalent, And, Day, Days, Hour, Hours, TileTracked, TileActive, TileApps, Card2Label, TopHeadline, Then,
        ShortHours, Card3Label, HabitsHeadline, Streak, StreakSpan, StreakOne, Busiest, BusiestValue, BusiestVs, BusiestOnly,
        Peak, PeakValue, PeakSub, Noon, NoonSub, NoonSubEarly, CardNameTotal, CardNameTop, CardNameHabits, Prev, Next, GoTo,
        Save, SaveDone, SaveTitle, SaveFilter,
        CardNameGames, CardNameGacha, Card4Label, GamesHeadline, Card5Label, GachaHeadline, NoGames, NoGacha,
    ];
}
