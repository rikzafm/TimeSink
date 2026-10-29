using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Theming;

namespace TimeSink.Tests.Apps;

public sealed class TodayEarlyTests
{
    private const long DayStart = 1_700_000_000;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(DayStart + 9 * 3600);

    // ---- caption rule ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(3599, true)]
    [InlineData(3600, false)]
    [InlineData(7200, false)]
    public void Caption_shows_only_while_the_total_is_under_one_hour(long total, bool shown) =>
        TodayEarly.ShowCaption(total).Should().Be(shown);

    [Fact]
    public void Tracking_since_is_the_sign_in_time_when_it_was_today() =>
        TodayEarly.TrackingSinceUtc(DayStart, DayStart + 500, DayStart + 900).Should().Be(DayStart + 500);

    [Fact]
    public void Tracking_since_falls_back_to_the_first_app_when_the_sign_in_was_before_today_or_unknown()
    {
        TodayEarly.TrackingSinceUtc(DayStart, DayStart - 10, DayStart + 900).Should().Be(DayStart + 900);
        TodayEarly.TrackingSinceUtc(DayStart, null, DayStart + 900).Should().Be(DayStart + 900);
    }

    [Fact]
    public void Tracking_since_is_unknown_with_neither()
    {
        TodayEarly.TrackingSinceUtc(DayStart, null, null).Should().BeNull();
        TodayEarly.TrackingSinceUtc(DayStart, DayStart - 10, null).Should().BeNull();
    }

    // ---- untracked card ----------------------------------------------------------------------------------

    private static OpenWindow Win(string path, string name, int minutesAgo, string? family = null) =>
        new(path, name, Now.AddMinutes(-minutesAgo), true, false, false, family);

    private static readonly ExcludedApps None = new([], []);

    [Fact]
    public void Card_lists_untracked_running_apps_longest_open_first_and_counts_them_all()
    {
        var card = TodayEarly.UntrackedCard(true, [
            Win(@"c:\a\one.exe", "One", 5), Win(@"c:\a\two.exe", "Two", 50), Win(@"c:\a\three.exe", "Three", 20),
            Win(@"c:\a\four.exe", "Four", 10), Win(@"c:\a\five.exe", "Five", 1),
        ], None, maxShown: 3);

        card.Should().NotBeNull();
        card!.Count.Should().Be(5);
        card.Shown.Select(a => a.DisplayName).Should().Equal("Two", "Three", "Four");
    }

    [Fact]
    public void Card_excludes_tracked_and_ignored_apps()
    {
        var card = TodayEarly.UntrackedCard(true,
            [Win(@"c:\a\tracked.exe", "Tracked", 5), Win(@"C:\A\IGNORED.exe", "Ignored", 5), Win(@"c:\a\new.exe", "New", 5)],
            new ExcludedApps([@"c:\a\tracked.exe", @"c:\a\ignored.exe"], []));
        card!.Shown.Select(a => a.DisplayName).Should().Equal("New");
        card.Count.Should().Be(1);
    }

    [Fact]
    public void Card_is_absent_when_the_setting_is_off_or_nothing_is_untracked()
    {
        TodayEarly.UntrackedCard(false, [Win(@"c:\a\new.exe", "New", 5)], None).Should().BeNull();
        TodayEarly.UntrackedCard(true, [], None).Should().BeNull();
        TodayEarly.UntrackedCard(true, [Win(@"c:\a\t.exe", "T", 5)], new ExcludedApps([@"c:\a\t.exe"], [])).Should().BeNull();
    }

    [Fact]
    public void Tracking_or_dismissing_a_suggestion_removes_it_from_the_card_via_the_real_library()
    {
        var dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "timesink.db");
        try
        {
            Db.Open(path).Dispose();
            var windows = new[] { Win(@"c:\a\a.exe", "A", 30), Win(@"c:\a\b.exe", "B", 20), Win(@"c:\a\c.exe", "C", 10) };
            using var lib = AppLibrary.Open(path);

            TodayEarly.UntrackedCard(true, windows, lib.ExcludedFromRunning())!.Count.Should().Be(3);
            lib.AddTracked(new NewApp(@"c:\a\a.exe", "A", null), DayStart, null);
            lib.Ignore(@"c:\a\b.exe", DayStart);

            var card = TodayEarly.UntrackedCard(true, windows, lib.ExcludedFromRunning());
            card!.Shown.Select(a => a.DisplayName).Should().Equal("C");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Builtin_themes_define_every_early_state_string(string id)
    {
        var loader = new ThemeLoader(new FolderThemeSource(Path.Combine(AppContext.BaseDirectory, "themes")));
        var t = loader.Load(id);
        foreach (var key in TodayEarlyStrings.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }
}
