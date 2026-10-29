using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

/// <summary>The merge picker's filter and search (<see cref="MergePlan.Shown"/>) and the Tracked tab's search (<see cref="TrackedSearch"/>).</summary>
public sealed class MergeListTests
{
    private static MergeCandidate Tracked(string name) => new("t" + name, CandidateKind.Tracked, name, @"C:\" + name + ".exe", 1, 60, null, null);
    private static MergeCandidate Merged(string name) => new("m" + name, CandidateKind.Merged, name, @"C:\" + name + ".exe", 2, 60, null, null);
    private static MergeCandidate Retired(string name) => new("s" + name, CandidateKind.Retired, name, @"C:\" + name + ".exe", 3, 0, null, null);
    private static MergeCandidate Installed(string name, string? publisher = null) =>
        new("i" + name, CandidateKind.Installed, name, @"C:\" + name + ".exe", 0, 0, new InstalledApp(name, @"C:\" + name + ".exe", null, null, publisher), null);

    private static MergePlan Plan(params MergeCandidate[] candidates) =>
        new(null!, "", null!, 9, "Main", candidates.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());

    private static string[] Names(IEnumerable<MergeCandidate> c) => c.Select(x => x.Name).ToArray();

    [Fact]
    public void A_new_plan_starts_on_the_Tracked_filter_with_no_query()
    {
        var plan = Plan(Tracked("A"));
        plan.Filter.Should().Be(MergeFilter.Tracked);
        plan.Query.Should().BeEmpty();
    }

    [Fact]
    public void Each_filter_lists_its_Candidates_in_name_order()
    {
        var plan = Plan(Installed("Zed"), Tracked("Beta"), Merged("Alpha"), Retired("Gamma"));

        plan.Filter = MergeFilter.Tracked;
        Names(plan.Shown()).Should().Equal("Alpha", "Beta");
        plan.Filter = MergeFilter.Untracked;
        Names(plan.Shown()).Should().Equal("Gamma", "Zed");
        plan.Filter = MergeFilter.All;
        Names(plan.Shown()).Should().Equal("Alpha", "Beta", "Gamma", "Zed");
    }

    [Fact]
    public void Counts_follow_the_filter_and_ignore_the_query()
    {
        var plan = Plan(Installed("Zed"), Tracked("Beta"), Merged("Alpha"), Retired("Gamma"));
        plan.Query = "zed";

        plan.CountOf(MergeFilter.Tracked).Should().Be(2);
        plan.CountOf(MergeFilter.Untracked).Should().Be(2);
        plan.CountOf(MergeFilter.All).Should().Be(4);
    }

    [Fact]
    public void A_blank_or_whitespace_query_keeps_every_Candidate()
    {
        var plan = Plan(Tracked("Beta"), Tracked("Alpha"));
        plan.Query = "   ";
        Names(plan.Shown()).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void The_query_searches_within_the_filter_only()
    {
        var plan = Plan(Tracked("Chrome"), Installed("Chromium"));
        plan.Query = "chrom";
        Names(plan.Shown()).Should().Equal("Chrome");
        plan.Filter = MergeFilter.All;
        Names(plan.Shown()).Should().BeEquivalentTo("Chrome", "Chromium");
    }

    [Fact]
    public void The_search_is_fuzzy_so_a_typo_still_matches_and_the_best_match_comes_first()
    {
        var plan = Plan(Tracked("Discord PTB"), Tracked("Discord"), Tracked("Notepad"));
        plan.Filter = MergeFilter.All;
        plan.Query = "discrod";
        Names(plan.Shown()).Should().Equal("Discord", "Discord PTB");
    }

    [Fact]
    public void An_installed_app_also_matches_on_its_publisher()
    {
        var plan = Plan(Installed("Photoshop", "Adobe"), Installed("Paint", "Microsoft"));
        plan.Filter = MergeFilter.Untracked;
        plan.Query = "adobe";
        Names(plan.Shown()).Should().Equal("Photoshop");
    }

    // ---- TrackedSearch ----

    private static TrackedEntry App(string name, params string[] merged) =>
        new(1, @"C:\" + name + ".exe", name, 0, 0, merged.Select((m, i) => new MergedApp(10 + i, @"C:\" + m + ".exe", m, 0)).ToList());

    private static string[] Names(IEnumerable<TrackedEntry> a) => a.Select(x => x.DisplayName).ToArray();

    [Fact]
    public void Tracked_search_with_a_blank_query_keeps_list_order()
    {
        var apps = new[] { App("Zed"), App("Alpha") };
        Names(TrackedSearch.Apply(apps, "  ", _ => null)).Should().Equal("Zed", "Alpha");
        Names(TrackedSearch.Apply(apps, null, _ => null)).Should().Equal("Zed", "Alpha");
    }

    [Fact]
    public void Tracked_search_matches_merged_names_and_the_publisher()
    {
        var apps = new[] { App("Browser", "Chromium"), App("Editor"), App("Mail") };
        Names(TrackedSearch.Apply(apps, "chromium", _ => null)).Should().Equal("Browser");
        Names(TrackedSearch.Apply(apps, "acme", p => p.Contains("Editor") ? "Acme Corp" : null)).Should().Equal("Editor");
    }

    [Fact]
    public void Tracked_search_orders_equal_matches_by_name()
    {
        var apps = new[] { App("Tool B"), App("Tool A") };
        Names(TrackedSearch.Apply(apps, "tool", _ => null)).Should().Equal("Tool A", "Tool B");
    }
}
