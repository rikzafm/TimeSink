using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>The Ranking rule on its own: Background apps last, then most time first, then name A to Z ignoring case.</summary>
public sealed class UsageRankTests
{
    private static UsageApp App(long id, string name, bool background = false, long group = 0) =>
        new(id, group == 0 ? id : group, name, 0, background);

    private static Dictionary<long, UsageApp> Apps(params UsageApp[] apps) => apps.ToDictionary(a => a.Id);

    private static List<long> Order(Dictionary<long, UsageApp> apps, IEnumerable<(long Group, long Seconds)> items) =>
        UsageRank.Rank(items, apps, i => i.Group, i => i.Seconds).Select(i => i.Group).ToList();

    [Fact]
    public void More_time_ranks_first()
    {
        var apps = Apps(App(1, "A"), App(2, "B"), App(3, "C"));
        Assert.Equal([2L, 3L, 1L], Order(apps, [(1, 10), (2, 30), (3, 20)]));
    }

    [Fact]
    public void A_background_app_ranks_after_every_other_app_however_much_time_it_has()
    {
        var apps = Apps(App(1, "Steam", background: true), App(2, "Notepad"), App(3, "Chrome"));
        Assert.Equal([3L, 2L, 1L], Order(apps, [(1, 9000), (2, 5), (3, 60)]));
    }

    [Fact]
    public void Background_apps_rank_among_themselves_by_time_then_name()
    {
        var apps = Apps(App(1, "Wallpaper", background: true), App(2, "Steam", background: true), App(3, "Chrome"));
        Assert.Equal([3L, 2L, 1L], Order(apps, [(1, 10), (2, 10), (3, 1)]));
    }

    [Fact]
    public void Equal_time_falls_back_to_the_name_ignoring_case()
    {
        var apps = Apps(App(1, "zed"), App(2, "Alpha"), App(3, "beta"));
        Assert.Equal([2L, 3L, 1L], Order(apps, [(1, 10), (2, 10), (3, 10)]));
    }

    [Fact]
    public void The_amount_selector_decides_which_time_counts()
    {
        var apps = Apps(App(1, "A"), App(2, "B"));
        var rows = new[] { (Group: 1L, Total: 100L, Month: 1L), (Group: 2L, Total: 50L, Month: 9L) };
        Assert.Equal([1L, 2L], UsageRank.Rank(rows, apps, r => r.Group, r => r.Total).Select(r => r.Group));
        Assert.Equal([2L, 1L], UsageRank.Rank(rows, apps, r => r.Group, r => r.Month).Select(r => r.Group));
    }

    [Fact]
    public void A_group_missing_from_the_apps_is_named_by_its_id_and_does_not_rank_as_background()
    {
        var apps = Apps(App(1, "A"));
        Assert.Equal("7", UsageRank.NameOf(apps, 7));
        Assert.False(UsageRank.IsBackground(apps, 7));
        Assert.Equal([7L, 1L], Order(apps, [(1, 1), (7, 2)]));
    }

    [Fact]
    public void NameOf_returns_the_name_of_the_group_app()
    {
        var apps = Apps(App(1, "Chrome"), App(2, "Chrome Beta", group: 1));
        Assert.Equal("Chrome", UsageRank.NameOf(apps, 1));
    }

    [Fact]
    public void IsBackground_reads_the_flag_of_the_group_app_only()
    {
        var apps = Apps(App(1, "Steam", background: true), App(2, "Steam alias", group: 1), App(3, "Notepad"));
        Assert.True(UsageRank.IsBackground(apps, 1));
        Assert.False(UsageRank.IsBackground(apps, 3));
    }

    [Fact]
    public void Equal_items_keep_their_input_order()
    {
        var apps = Apps(App(1, "Same"), App(2, "Same"));
        Assert.Equal([2L, 1L], Order(apps, [(2, 5), (1, 5)]));
    }
}
