using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

/// <summary>A Background app (the fixture's top app, Chrome) keeps its time but ranks after every other app on each ranked list (Wrapped leaves it out of its podiums).</summary>
public sealed class BackgroundRankingTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private void FlagChrome() => _f.Exec($"UPDATE apps SET is_background = 1 WHERE id = {_f.Chrome}");

    [Fact]
    public void Today_and_week_lists_put_the_background_app_last()
    {
        var todayBefore = Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc, null).Apps.Select(a => a.Name).ToList();
        var weekBefore = Q.Week(_f.DbPath, Now, TimeZoneInfo.Utc, WeekStart.Mon).Apps.Select(a => a.Name).ToList();
        FlagChrome();

        var today = Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc, null).Apps.Select(a => a.Name).ToList();
        var week = Q.Week(_f.DbPath, Now, TimeZoneInfo.Utc, WeekStart.Mon).Apps.Select(a => a.Name).ToList();

        // Running apps list first on Today, so the background app is last within its own running/closed group.
        var chrome = Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc, null).Apps.FirstOrDefault(a => a.Name == "Chrome");
        if (chrome is not null)
            Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc, null).Apps.Where(a => a.IsRunning == chrome.IsRunning).Last().Name.Should().Be("Chrome");
        if (weekBefore.Contains("Chrome")) week.Last().Should().Be("Chrome");
        today.Should().BeEquivalentTo(todayBefore);
        week.Should().BeEquivalentTo(weekBefore);
    }

    [Fact]
    public void Month_top_apps_put_the_background_app_last_and_wrapped_leaves_it_out()
    {
        FlagChrome();

        var month = MonthQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 3, DayOfWeek.Monday);
        month.TopApps.Last().Name.Should().Be("Chrome");

        var wrapped = WrappedQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc);
        wrapped.Podium.Concat(wrapped.Others).Select(a => a.Name).Should().NotContain("Chrome");
    }

    [Fact]
    public void Setting_the_flag_on_a_merged_alias_flags_the_app_it_is_merged_into()
    {
        const long alias = 950;
        _f.Exec($"INSERT INTO apps(id, exe_path, display_name, added_utc, canonical_id) VALUES({alias}, 'c:\\\\chrome2.exe', 'Chrome2', 0, {_f.Chrome})");
        using (var lib = AppLibrary.Open(_f.DbPath))
        {
            lib.SetBackground(alias, true);
            lib.Tracked().Single(a => a.Id == _f.Chrome).Background.Should().BeTrue();
            lib.Tracked().Where(a => a.Id != _f.Chrome).Should().OnlyContain(a => !a.Background);
            lib.SetBackground(_f.Chrome, false);
            lib.Tracked().Should().OnlyContain(a => !a.Background);
        }
    }
}
