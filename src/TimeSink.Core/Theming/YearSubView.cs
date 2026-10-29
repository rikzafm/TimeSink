namespace TimeSink.Core.Theming;

/// <summary>The Year tab's three sub-views (decision 9), remembered in <c>ui.lastYearView</c>.</summary>
public enum YearSubView { Pace, Wrapped, Matrix }

public static class YearSubViews
{
    public static YearSubView Parse(string? stored)
        => Enum.TryParse<YearSubView>(stored, ignoreCase: false, out var v) && Enum.IsDefined(v) ? v : YearSubView.Pace;
}
