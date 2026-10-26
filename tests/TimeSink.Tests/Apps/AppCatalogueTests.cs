using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Apps;

public sealed class AppCatalogueTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private sealed class FakeDesktop : IDesktop
    {
        public List<OpenWindow> Windows { get; } = [];
        public Dictionary<string, string> Aumids { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Exe, int Size)> IconCalls { get; } = [];
        public Exception? CaptureFails { get; set; }
        public Dictionary<string, Exception> IconFails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WindowCapture CaptureWindows() => CaptureFails is { } e ? throw e : new WindowCapture(Windows.ToList(), Aumids);

        public byte[]? ExtractIcon(string exePath, string? aumid, string? imageFile, int size)
        {
            IconCalls.Add((exePath, size));
            if (IconFails.TryGetValue(exePath, out var fail)) throw fail;
            return Encoding.UTF8.GetBytes("png:" + exePath);
        }

        public List<InstalledApp> Installed { get; } = [];
        public int ScanCalls { get; private set; }

        public IReadOnlyList<InstalledApp> ScanInstalled()
        {
            ScanCalls++;
            return Installed.ToList();
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly FakeDesktop _desktop = new();

    public AppCatalogueTests()
    {
        _path = Path.Combine(_dir, "timesink.db");
        Db.Open(_path).Dispose();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AppCatalogue Catalogue(string? path = null) => new(path ?? _path, _desktop);

    private void Open(string exe, string name = "App", int minutesAgo = 10, string? family = null) =>
        _desktop.Windows.Add(new OpenWindow(exe, name, T0.AddMinutes(-minutesAgo), true, false, false, family));

    private static IEnumerable<string> Paths(RunningList l) => l.Candidates.Select(c => c.App.ExePath);


    private long Track(string exe, string name, long seconds = 0, string? family = null)
    {
        using var lib = AppLibrary.Open(_path);
        var id = lib.AddTracked(new NewApp(exe, name, family), 100, Encoding.UTF8.GetBytes("stored:" + exe)).AppId;
        if (seconds > 0) Usage(id, seconds);
        return id;
    }

    private void Usage(long appId, long seconds)
    {
        using var db = Db.Open(_path);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO daily_usage (app_id, day, seconds) VALUES ($a, '2026-10-01', $s)";
        cmd.Parameters.AddWithValue("$a", appId);
        cmd.Parameters.AddWithValue("$s", seconds);
        cmd.ExecuteNonQuery();
    }

    private long Retire(string exe, string name, long seconds = 7200)
    {
        var id = Track(exe, name, seconds);
        using var lib = AppLibrary.Open(_path);
        lib.Retire(id, 200);
        return id;
    }

    private static InstalledApp Installed(string exe, string name, string? publisher = "Acme") => new(name, exe, null, null, publisher);

    private static string[] Names(MergePlan p) => p.Candidates.Select(c => c.Name).ToArray();

    [Fact]
    public void Running_candidates_exclude_tracked_and_ignored_apps()
    {
        Open(@"c:\a\free.exe", "Free");
        Open(@"c:\a\tracked.exe", "Tracked");
        Open(@"c:\a\ignored.exe", "Ignored");
        using (var lib = AppLibrary.Open(_path))
        {
            lib.AddTracked(new NewApp(@"c:\a\tracked.exe", "Tracked", null), 100, null);
            lib.Ignore(@"c:\a\ignored.exe", 100);
        }

        var running = Catalogue().Running();

        running.Outcome.Should().Be(CatalogueOutcome.Done);
        Paths(running).Should().Equal(@"c:\a\free.exe");
    }

    [Fact]
    public void A_row_tracked_this_visit_stays_in_the_list_marked_as_tracking()
    {
        Open(@"c:\a\one.exe", "One", 20);
        Open(@"c:\a\two.exe", "Two", 10);
        var cat = Catalogue();
        var one = cat.Running().Candidates.First();

        cat.Track(one).Should().Be(CatalogueOutcome.Done);

        cat.Running().Candidates.Select(c => (c.App.ExePath, c.Tracking))
            .Should().Equal((@"c:\a\one.exe", true), (@"c:\a\two.exe", false));
    }

    [Fact]
    public void A_new_visit_forgets_rows_tracked_in_the_last_one()
    {
        Open(@"c:\a\one.exe");
        var cat = Catalogue();
        cat.Track(cat.Running().Candidates.Single());

        cat.NewVisit();

        cat.Running().Candidates.Should().BeEmpty();
        cat.AddedCount.Should().Be(0);
    }

    [Fact]
    public void Added_count_includes_apps_added_elsewhere_without_double_counting()
    {
        Open(@"c:\a\one.exe");
        var cat = Catalogue();
        cat.Track(cat.Running().Candidates.Single());
        cat.NoteAddedElsewhere(@"c:\b\installed.exe");
        cat.NoteAddedElsewhere(@"C:\A\ONE.EXE");

        cat.AddedCount.Should().Be(2);
    }

    [Fact]
    public void Stopping_an_app_takes_it_out_of_the_count_and_the_tracking_rows()
    {
        Open(@"c:\a\one.exe");
        var cat = Catalogue();
        cat.Track(cat.Running().Candidates.Single());
        cat.NoteAddedElsewhere(@"c:\b\installed.exe");

        cat.NoteStopped(@"c:\a\one.exe");
        cat.NoteStopped(@"c:\b\installed.exe");

        cat.AddedCount.Should().Be(0);
        cat.Running().Candidates.Should().BeEmpty(); // still tracked in the database, so not offered either
    }

    [Fact]
    public void Dismissing_ignores_the_app_for_good()
    {
        Open(@"c:\a\one.exe");
        var cat = Catalogue();

        cat.Dismiss(cat.Running().Candidates.Single()).Should().Be(CatalogueOutcome.Done);

        Catalogue().Running().Candidates.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_database_gives_an_outcome_and_leaves_the_row_untracked()
    {
        var cat = Catalogue(Path.Combine(_dir, "nope", "missing.db"));
        var row = new RunningCandidate(new RunningApp(@"c:\a\one.exe", "One", T0, null), null, null, false);

        cat.Track(row).Should().Be(CatalogueOutcome.Unavailable);
        cat.Dismiss(row).Should().Be(CatalogueOutcome.Unavailable);
        cat.AddedCount.Should().Be(0);
        var running = cat.Running();
        running.Outcome.Should().Be(CatalogueOutcome.Unavailable);
        running.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Failures_that_are_not_database_failures_propagate()
    {
        _desktop.CaptureFails = new InvalidOperationException("boom");

        var act = () => Catalogue().Running();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Icons_are_extracted_once_per_app_at_the_requested_size_and_carried_on_the_candidate()
    {
        Open(@"c:\a\one.exe");
        var cat = Catalogue();

        var first = cat.Running(iconPx: 256).Candidates.Single();
        cat.Running(iconPx: 256);

        _desktop.IconCalls.Should().Equal((@"c:\a\one.exe", 256));
        first.Png.Should().Equal(Encoding.UTF8.GetBytes(@"png:c:\a\one.exe"));
    }

    [Fact]
    public void Icons_are_only_extracted_for_the_first_few_candidates_when_limited()
    {
        for (var i = 0; i < 4; i++) Open($@"c:\a\app{i}.exe", "App" + i, 40 - i);

        var running = Catalogue().Running(iconPx: 64, iconLimit: 2);

        running.Candidates.Select(c => c.Png is not null).Should().Equal(true, true, false, false);
    }

    [Fact]
    public void Tracking_stores_the_candidates_icon()
    {
        Open(@"c:\a\one.exe", "One");
        var cat = Catalogue();
        cat.Track(cat.Running().Candidates.Single());

        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(lib.Library().Single().Id).Should().NotBeNull();
    }

    [Fact]
    public void A_merge_plan_offers_all_four_kinds_of_Candidate_sorted_by_name()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        Track(@"c:\a\other.exe", "other", 600);
        var merged = Track(@"c:\a\old.exe", "Old name");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);
        Retire(@"c:\a\gone.exe", "Gone");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));

        var plan = Catalogue().OpenMergePlan(main)!;

        plan.Candidates.Select(c => (c.Name, c.Kind)).Should().Equal(
            ("Fresh", CandidateKind.Installed), ("Gone", CandidateKind.Retired),
            ("Old name", CandidateKind.Merged), ("other", CandidateKind.Tracked));
        plan.Candidates.Single(c => c.Kind == CandidateKind.Tracked).Seconds.Should().Be(600);
    }

    [Fact]
    public void Already_merged_apps_start_ticked_and_nothing_else_does()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var merged = Track(@"c:\a\old.exe", "Old");
        Track(@"c:\a\other.exe", "Other");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);

        var plan = Catalogue().OpenMergePlan(main)!;

        plan.Candidates.Where(c => plan.IsTicked(c.Key)).Select(c => c.Name).Should().Equal("Old");
    }

    [Fact]
    public void Installed_apps_already_tracked_stopped_or_merged_are_not_offered_again()
    {
        var main = Track(@"c:\a\main.exe", "Main", family: "Fam.Main");
        Track(@"c:\a\other.exe", "Other");
        var merged = Track(@"c:\a\old.exe", "Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);
        Retire(@"c:\a\gone.exe", "Gone");
        _desktop.Installed.AddRange([Installed(@"C:\A\OTHER.EXE", "Other"), Installed(@"c:\a\old.exe", "Old"),
            Installed(@"c:\a\gone.exe", "Gone"), Installed(@"c:\z\main2.exe", "Main 2") with { PackageFamily = "Fam.Main" },
            Installed(@"c:\b\fresh.exe", "Fresh")]);

        var plan = Catalogue().OpenMergePlan(main)!;

        plan.Candidates.Where(c => c.Kind == CandidateKind.Installed).Select(c => c.Name).Should().Equal("Fresh");
    }

    [Fact]
    public void Trivial_stopped_apps_and_Windows_directory_processes_are_not_offered()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        Retire(@"c:\a\blip.exe", "Blip", seconds: 60);
        Retire(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "Explorer");
        Retire(@"c:\a\real.exe", "Real");

        var plan = Catalogue().OpenMergePlan(main)!;

        Names(plan).Should().Equal("Real");
    }

    [Fact]
    public void The_installed_scan_is_cached_for_the_visit_and_repeated_in_the_next()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var cat = Catalogue();

        cat.OpenMergePlan(main);
        cat.OpenMergePlan(main);
        _desktop.ScanCalls.Should().Be(1);

        cat.NewVisit();
        cat.OpenMergePlan(main);
        _desktop.ScanCalls.Should().Be(2);
    }

    [Fact]
    public void Opening_a_plan_for_an_app_that_is_not_a_Main_app_or_with_the_database_missing_gives_nothing()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var merged = Track(@"c:\a\old.exe", "Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);

        Catalogue().OpenMergePlan(merged).Should().BeNull();
        Catalogue().OpenMergePlan(999).Should().BeNull();
        Catalogue(Path.Combine(_dir, "nope", "missing.db")).OpenMergePlan(main).Should().BeNull();
    }

    [Fact]
    public void Toggling_a_Candidate_ticks_and_unticks_it_by_key()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        Track(@"c:\a\other.exe", "Other");
        var plan = Catalogue().OpenMergePlan(main)!;
        var key = plan.Candidates.Single().Key;

        plan.Toggle(key);
        plan.IsTicked(key).Should().BeTrue();
        plan.Toggle(key);
        plan.IsTicked(key).Should().BeFalse();
    }

    [Fact]
    public void The_summary_counts_adds_and_removes_and_flags_apps_that_are_not_tracked_yet()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        Track(@"c:\a\other.exe", "Other");
        var merged = Track(@"c:\a\old.exe", "Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);
        Retire(@"c:\a\gone.exe", "Gone");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var plan = Catalogue().OpenMergePlan(main)!;
        string Key(string name) => plan.Candidates.Single(c => c.Name == name).Key;

        plan.Summary.Should().Be(new MergeSummary(0, 0, false));
        plan.Summary.Changed.Should().BeFalse();

        plan.Toggle(Key("Other"));
        plan.Summary.Should().Be(new MergeSummary(1, 0, false));

        plan.Toggle(Key("Old")); // untick a Merged app
        plan.Summary.Should().Be(new MergeSummary(1, 1, false));

        plan.Toggle(Key("Gone"));
        plan.Toggle(Key("Fresh"));
        plan.Summary.Should().Be(new MergeSummary(3, 1, true));
        plan.Summary.Changed.Should().BeTrue();
    }

    [Fact]
    public void Unticking_a_Merged_app_and_ticking_it_again_is_no_change()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var merged = Track(@"c:\a\old.exe", "Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);
        var plan = Catalogue().OpenMergePlan(main)!;
        var key = plan.Candidates.Single().Key;

        plan.Toggle(key);
        plan.Toggle(key);

        plan.Summary.Changed.Should().BeFalse();
    }

    private TrackedEntry MainEntry(long id)
    {
        using var lib = AppLibrary.Open(_path);
        return lib.Tracked().Single(a => a.Id == id);
    }

    [Fact]
    public void Confirm_merges_ticked_tracked_apps_and_unmerges_unticked_merged_ones()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var other = Track(@"c:\a\other.exe", "Other");
        var merged = Track(@"c:\a\old.exe", "Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(merged, main);
        var cat = Catalogue();
        var plan = cat.OpenMergePlan(main)!;
        plan.Toggle(plan.Candidates.Single(c => c.Name == "Other").Key);
        plan.Toggle(plan.Candidates.Single(c => c.Name == "Old").Key);

        plan.Confirm().Should().Be(CatalogueOutcome.Done);

        MainEntry(main).Merged.Select(m => m.Id).Should().Equal(other);
        using var check = AppLibrary.Open(_path);
        check.Tracked().Select(a => a.Id).Should().BeEquivalentTo([main, merged]);
        cat.AddedCount.Should().Be(0); // both were tracked already
    }

    [Fact]
    public void Confirm_restores_a_stopped_app_and_merges_it_and_counts_it_as_added()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var gone = Retire(@"c:\a\gone.exe", "Gone", seconds: 7200);
        var cat = Catalogue();
        var plan = cat.OpenMergePlan(main)!;
        plan.Toggle(plan.Candidates.Single().Key);

        plan.Confirm().Should().Be(CatalogueOutcome.Done);

        MainEntry(main).Merged.Should().ContainSingle().Which.Should().Match<MergedApp>(m => m.Id == gone && m.Seconds == 7200);
        cat.AddedCount.Should().Be(1);
    }

    [Fact]
    public void Confirm_adds_an_installed_app_and_merges_it_extracting_the_icon_through_the_seam()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var cat = Catalogue();
        var plan = cat.OpenMergePlan(main)!;
        plan.Toggle(plan.Candidates.Single().Key);

        plan.Confirm().Should().Be(CatalogueOutcome.Done);

        var merged = MainEntry(main).Merged.Should().ContainSingle().Subject;
        merged.ExePath.Should().Be(@"c:\b\fresh.exe");
        _desktop.IconCalls.Should().Equal((@"c:\b\fresh.exe", 64));
        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(merged.Id).Should().Equal(Encoding.UTF8.GetBytes(@"png:c:\b\fresh.exe"));
        cat.AddedCount.Should().Be(1);
    }

    [Fact]
    public void Confirm_uses_the_icon_the_caller_already_knew_instead_of_extracting_one()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var known = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase) { [@"C:\B\FRESH.EXE"] = [1, 2, 3] };
        var plan = Catalogue().OpenMergePlan(main, known)!;
        plan.Toggle(plan.Candidates.Single().Key);

        plan.Confirm();

        _desktop.IconCalls.Should().BeEmpty();
        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(MainEntry(main).Merged.Single().Id).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Confirm_failing_midway_reports_unavailable_keeps_the_plan_and_leaves_earlier_steps_in_the_database()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var first = Track(@"c:\a\first.exe", "A first");   // sorted first: merged before the failure
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Z fresh"));
        _desktop.IconFails[@"c:\b\fresh.exe"] = new IOException("disk gone");
        var cat = Catalogue();
        var plan = cat.OpenMergePlan(main)!;
        foreach (var c in plan.Candidates) plan.Toggle(c.Key);

        plan.Confirm().Should().Be(CatalogueOutcome.Unavailable);

        plan.Candidates.Should().OnlyContain(c => plan.IsTicked(c.Key)); // the plan is as the user left it
        plan.Summary.Should().Be(new MergeSummary(2, 0, true));
        MainEntry(main).Merged.Select(m => m.Id).Should().Equal(first); // partial: no transaction spans the steps
        cat.AddedCount.Should().Be(0);
    }

    [Fact]
    public void Confirm_failures_that_are_not_database_failures_propagate()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        _desktop.IconFails[@"c:\b\fresh.exe"] = new InvalidOperationException("boom");
        var plan = Catalogue().OpenMergePlan(main)!;
        plan.Toggle(plan.Candidates.Single().Key);

        var act = () => plan.Confirm();

        act.Should().Throw<InvalidOperationException>();
    }
}
