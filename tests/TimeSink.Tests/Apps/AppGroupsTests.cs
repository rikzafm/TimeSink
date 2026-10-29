using TimeSink.Core.Apps;
using TimeSink.Core.Usage;
using TimeSink.Tests.Usage;

namespace TimeSink.Tests.Apps;

/// <summary>The group rule on its own: a Merged app counts under its Main app, unless that Main app is retired or gone.</summary>
public sealed class AppGroupsTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private static GroupedApp MainApp(long id, string name = "Main", AppState state = AppState.Tracked) => new(id, id, name, 0, State: state);

    private static GroupedApp MergedApp(long id, long main, string name = "Alias", AppState state = AppState.Tracked) => new(id, main, name, 0, State: state);

    private AppGroups Load(bool includeRetired = false)
    {
        using var c = UsageDb.OpenReadOnly(_f.DbPath);
        return AppGroups.Load(c, includeRetired);
    }

    [Fact]
    public void A_merged_app_counts_under_its_tracked_main_app()
    {
        var groups = AppGroups.From([MainApp(1), MergedApp(2, 1)]);

        groups.GroupOf(2).Should().Be(1);
        groups[2].Group.Should().Be(1);
    }

    [Fact]
    public void A_tracked_merged_app_of_a_retired_main_app_is_its_own_group()
    {
        var groups = AppGroups.From([MainApp(1, state: AppState.Retired), MergedApp(2, 1)]);

        groups.GroupOf(2).Should().Be(2);
    }

    [Fact]
    public void A_retired_merged_app_of_a_retired_main_app_stays_in_its_group()
    {
        var groups = AppGroups.From([MainApp(1, state: AppState.Retired), MergedApp(2, 1, state: AppState.Retired)]);

        groups.GroupOf(2).Should().Be(1);
    }

    [Fact]
    public void A_merged_app_whose_main_app_is_not_loaded_is_its_own_group()
    {
        var groups = AppGroups.From([MergedApp(2, 1)]);

        groups.GroupOf(2).Should().Be(2);
    }

    [Fact]
    public void The_games_filter_keeps_game_and_gacha_apps_and_their_merged_apps()
    {
        var groups = AppGroups.From(
        [
            MainApp(1) with { Kind = AppKind.Game }, MainApp(2) with { Kind = AppKind.Gacha }, MainApp(3),
            MergedApp(4, 1), MergedApp(5, 3),
        ]).OnlyGames();

        groups.Values.Select(a => a.Id).Should().BeEquivalentTo([1L, 2L, 4L]);
    }

    [Fact]
    public void The_games_filter_leaves_nothing_when_no_app_is_a_game()
    {
        AppGroups.From([MainApp(1), MainApp(2)]).OnlyGames().Count.Should().Be(0);
    }

    [Fact]
    public void An_unknown_app_is_its_own_group()
    {
        AppGroups.From([MainApp(1)]).GroupOf(99).Should().Be(99);
    }

    [Fact]
    public void Members_lists_the_main_app_first_then_its_merged_apps_by_id()
    {
        var groups = AppGroups.From([MergedApp(5, 1), MainApp(1), MainApp(2), MergedApp(3, 1), MergedApp(4, 2)]);

        groups.Members(1).Select(a => a.Id).Should().Equal(1, 3, 5);
        groups.Members(2).Select(a => a.Id).Should().Equal(2, 4);
    }

    [Fact]
    public void Members_leaves_out_a_merged_app_that_became_its_own_group()
    {
        var groups = AppGroups.From([MainApp(1, state: AppState.Retired), MergedApp(2, 1)]);

        groups.Members(1).Select(a => a.Id).Should().Equal(1);
        groups.Members(2).Select(a => a.Id).Should().Equal(2);
    }

    [Fact]
    public void Loading_leaves_retired_apps_out_unless_asked_for()
    {
        Load().Values.Should().NotContain(a => a.Name == "Old");

        var old = Load(includeRetired: true).Values.Single(a => a.Name == "Old");
        old.State.Should().Be(AppState.Retired);
        Load(includeRetired: true)[_f.Chrome].State.Should().Be(AppState.Tracked);
    }

    [Fact]
    public void Loading_puts_a_merged_app_in_its_canonical_group()
    {
        _f.Exec($"INSERT INTO apps(id, canonical_id, exe_path, display_name, added_utc) VALUES (900, {_f.Chrome}, 'chrome-beta.exe', 'Chrome Beta', 0)");

        Load().GroupOf(900).Should().Be(_f.Chrome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Loading_makes_a_tracked_merged_app_of_a_retired_main_app_its_own_group(bool includeRetired)
    {
        _f.Exec("INSERT INTO apps(id, canonical_id, exe_path, display_name, added_utc) VALUES (901, (SELECT id FROM apps WHERE display_name = 'Old'), 'old2.exe', 'Old Beta', 0)");

        Load(includeRetired).GroupOf(901).Should().Be(901);
    }
}
