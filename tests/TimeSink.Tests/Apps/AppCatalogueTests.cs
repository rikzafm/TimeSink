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
            OnIcon?.Invoke(exePath);
            if (IconFails.TryGetValue(exePath, out var fail)) throw fail;
            return Encoding.UTF8.GetBytes("png:" + exePath);
        }

        public List<InstalledApp> Installed { get; } = [];

        /// <summary>When set, a scan signals <see cref="ScanEntered"/> and then waits for <see cref="ScanRelease"/>.</summary>
        public bool SlowScan { get; set; }
        public ManualResetEventSlim ScanEntered { get; } = new(false);
        public ManualResetEventSlim ScanRelease { get; } = new(false);
        public Action<string>? OnIcon { get; set; }

        public IReadOnlyList<InstalledApp> ScanInstalled()
        {
            Interlocked.Increment(ref _scanCalls);
            if (SlowScan)
            {
                ScanEntered.Set();
                ScanRelease.Wait(TimeSpan.FromSeconds(30));
            }
            return Installed.ToList();
        }

        private int _scanCalls;
        public int ScanCalls => _scanCalls;
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

    private AppCatalogue Catalogue(string? path = null) => new(path ?? _path, _desktop, new AppIconSource(path ?? _path, _desktop));

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
    public void The_installed_list_and_the_merge_plan_share_one_scan_per_visit()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var cat = Catalogue();

        cat.Installed().Select(i => i.Name).Should().Equal("Fresh");
        cat.OpenMergePlan(main);
        cat.Installed();

        _desktop.ScanCalls.Should().Be(1);
        cat.NewVisit();
        cat.Installed();
        _desktop.ScanCalls.Should().Be(2);
    }

    [Fact]
    public void The_last_scan_stays_available_into_the_next_visit_and_an_empty_rescan_does_not_replace_it()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var cat = Catalogue();
        cat.LastInstalled.Should().BeNull();
        cat.Installed();

        cat.NewVisit();
        cat.LastInstalled!.Select(i => i.Name).Should().Equal("Fresh");
        _desktop.Installed.Clear(); // the scan failed
        cat.Installed().Select(i => i.Name).Should().Equal("Fresh");
    }

    [Fact]
    public void An_installed_app_is_found_by_exe_path_ignoring_case_once_a_scan_has_run()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var cat = Catalogue();
        cat.InstalledBy(@"c:\b\fresh.exe").Should().BeNull();

        cat.Installed();

        cat.InstalledBy(@"C:\B\FRESH.EXE")!.Name.Should().Be("Fresh");
        cat.InstalledBy(@"c:\b\other.exe").Should().BeNull();
    }

    [Fact]
    public void The_first_of_two_installed_apps_with_one_exe_path_wins()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "First"));
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Second"));
        var cat = Catalogue();
        cat.Installed();

        cat.InstalledBy(@"c:\b\fresh.exe")!.Name.Should().Be("First");
    }

    [Fact]
    public void The_installed_lookup_follows_the_last_scan_into_the_next_visit_and_survives_an_empty_rescan()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var cat = Catalogue();
        cat.Installed();

        cat.NewVisit();
        cat.InstalledBy(@"c:\b\fresh.exe")!.Name.Should().Be("Fresh");
        _desktop.Installed.Clear(); // the scan failed
        cat.Installed();
        cat.InstalledBy(@"c:\b\fresh.exe")!.Name.Should().Be("Fresh");
    }

    [Fact]
    public async Task The_last_scan_and_a_new_visit_do_not_wait_for_a_scan_in_progress()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        _desktop.SlowScan = true;
        var cat = Catalogue();
        var scan = Task.Run(() => cat.Installed());
        _desktop.ScanEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        try
        {
            IReadOnlyList<InstalledApp>? during = null;
            var quick = Task.Run(() =>
            {
                during = cat.LastInstalled;
                cat.NewVisit();
            });

            (await Task.WhenAny(quick, Task.Delay(TimeSpan.FromSeconds(3)))).Should().BeSameAs(quick, "neither call should wait for the scan");
            during.Should().BeNull();
        }
        finally { _desktop.ScanRelease.Set(); }
        await scan.WaitAsync(TimeSpan.FromSeconds(10));
        cat.LastInstalled!.Select(i => i.Name).Should().Equal("Fresh");
    }

    [Fact]
    public async Task Callers_arriving_during_a_scan_share_it()
    {
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        _desktop.SlowScan = true;
        var cat = Catalogue();
        var first = Task.Run(() => cat.Installed());
        _desktop.ScanEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var second = Task.Run(() => cat.Installed());
        await Task.Delay(200);

        _desktop.ScanRelease.Set();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        (await second).Select(i => i.Name).Should().Equal("Fresh");
        _desktop.ScanCalls.Should().Be(1);
    }

    [Fact]
    public void The_library_tells_tracked_retired_and_unknown_apps_apart()
    {
        Track(@"c:\a\live.exe", "Live");
        Retire(@"c:\a\gone.exe", "Gone");

        var library = Catalogue().Library();

        library.Outcome.Should().Be(CatalogueOutcome.Done);
        library.StateOf(@"C:\A\LIVE.EXE", null)!.State.Should().Be(AppState.Tracked);
        library.StateOf(@"c:\a\gone.exe", null)!.State.Should().Be(AppState.Retired);
        library.StateOf(@"c:\a\never.exe", null).Should().BeNull();
    }

    [Fact]
    public void A_merged_app_reads_as_the_app_it_counts_under_by_path_or_package_family()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var old = Track(@"c:\a\old.exe", "Old", family: "Fam.Old");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(old, main);
        var other = Track(@"c:\a\other.exe", "Other", family: "Fam.Other");

        var library = Catalogue().Library();

        library.StateOf(@"c:\a\old.exe", null)!.Id.Should().Be(main);
        library.StateOf(@"c:\z\old2.exe", "Fam.Old")!.Id.Should().Be(main);
        library.StateOf(@"c:\z\other2.exe", "fam.other")!.Id.Should().Be(other);
    }

    [Fact]
    public void A_package_family_does_not_match_a_stopped_app()
    {
        var id = Track(@"c:\a\fam.exe", "Fam", family: "Fam.X");
        using (var lib = AppLibrary.Open(_path)) lib.Retire(id, 300);

        Catalogue().Library().StateOf(@"c:\z\fam2.exe", "Fam.X").Should().BeNull();
    }

    [Fact]
    public void The_library_lists_stopped_apps_with_real_history_most_recently_stopped_first()
    {
        var first = Track(@"c:\a\first.exe", "First", 7200);
        var second = Track(@"c:\a\second.exe", "Second", 7200);
        Track(@"c:\a\blip.exe", "Blip", 60);
        using (var lib = AppLibrary.Open(_path))
        {
            lib.Retire(first, 500);
            lib.Retire(second, 900);
            lib.Retire(lib.Library().Single(a => a.DisplayName == "Blip").Id, 950);
        }

        Catalogue().Library().Retired.Select(a => a.DisplayName).Should().Equal("Second", "First");
    }

    [Fact]
    public void A_busy_or_missing_database_gives_an_empty_library()
    {
        var library = Catalogue(Path.Combine(_dir, "nope", "missing.db")).Library();

        library.Outcome.Should().Be(CatalogueOutcome.Unavailable);
        library.Retired.Should().BeEmpty();
        library.StateOf(@"c:\a\x.exe", null).Should().BeNull();
    }

    [Fact]
    public void Adding_an_app_tracks_it_with_its_icon_and_counts_it_as_added()
    {
        var cat = Catalogue();

        cat.AddApp(new NewApp(@"c:\b\fresh.exe", "Fresh", null), [9, 8]).Should().Be(CatalogueOutcome.Done);

        var app = cat.Library().StateOf(@"c:\b\fresh.exe", null)!;
        app.State.Should().Be(AppState.Tracked);
        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(app.Id).Should().Equal(9, 8);
        cat.AddedCount.Should().Be(1);
    }

    [Fact]
    public void Retiring_and_restoring_change_the_state_and_the_count()
    {
        var id = Track(@"c:\a\one.exe", "One");
        var cat = Catalogue();
        cat.AddApp(new NewApp(@"c:\a\one.exe", "One", null), null); // counted as added this visit
        var app = cat.Library().StateOf(@"c:\a\one.exe", null)!;

        cat.ApplyChange(AppChange.Retire(app)).Should().Be(CatalogueOutcome.Done);
        cat.Library().StateOf(@"c:\a\one.exe", null)!.State.Should().Be(AppState.Retired);
        cat.AddedCount.Should().Be(0);

        cat.ApplyChange(AppChange.Restore(app)).Should().Be(CatalogueOutcome.Done);
        cat.Library().StateOf(@"c:\a\one.exe", null)!.Should().Match<LibraryApp>(a => a.Id == id && a.State == AppState.Tracked);
        cat.AddedCount.Should().Be(1);
    }

    [Fact]
    public void Adding_retiring_and_restoring_with_the_database_missing_give_an_outcome_and_change_nothing()
    {
        var cat = Catalogue(Path.Combine(_dir, "nope", "missing.db"));
        var app = new LibraryApp(1, @"c:\a\one.exe", null, "One", AppState.Tracked, null);

        cat.AddApp(new NewApp(@"c:\a\one.exe", "One", null), null).Should().Be(CatalogueOutcome.Unavailable);
        cat.ApplyChange(AppChange.Retire(app)).Should().Be(CatalogueOutcome.Unavailable);
        cat.ApplyChange(AppChange.Restore(app)).Should().Be(CatalogueOutcome.Unavailable);
        cat.AddedCount.Should().Be(0);
    }

    [Fact]
    public void The_kind_and_background_changes_apply()
    {
        var id = Track(@"c:\a\one.exe", "One");
        var cat = Catalogue();

        cat.ApplyChange(AppChange.SetKind(id, AppKind.Gacha)).Should().Be(CatalogueOutcome.Done);
        cat.ApplyChange(AppChange.SetBackground(id, true)).Should().Be(CatalogueOutcome.Done);

        cat.Library().StateOf(@"c:\a\one.exe", null).Should().Match<LibraryApp>(a => a.Kind == AppKind.Gacha && a.Background);
    }

    [Fact]
    public void A_chosen_icon_is_stored_and_read_back()
    {
        var id = Track(@"c:\one.exe", "One");
        var cat = Catalogue();
        var png = Encoding.UTF8.GetBytes("chosen");

        cat.ApplyChange(AppChange.SetIcon(id, png)).Should().Be(CatalogueOutcome.Done);

        cat.StoredIcon(id).Should().Equal(png);
    }

    [Fact]
    public void Renaming_trims_the_name_and_a_blank_name_is_rejected_leaving_the_old_one()
    {
        var id = Track(@"c:\a\one.exe", "One");
        var cat = Catalogue();

        cat.ApplyChange(AppChange.Rename(id, "  Uno  ")).Should().Be(CatalogueOutcome.Done);
        cat.ApplyChange(AppChange.Rename(id, "   ")).Should().Be(CatalogueOutcome.Rejected);
        cat.ApplyChange(AppChange.Rename(999, "Nobody")).Should().Be(CatalogueOutcome.Rejected);

        cat.Library().StateOf(@"c:\a\one.exe", null)!.DisplayName.Should().Be("Uno");
    }

    [Fact]
    public void Making_a_merged_app_main_unmerging_and_clearing_history_apply()
    {
        var main = Track(@"c:\a\main.exe", "Main", 100);
        var alias = Track(@"c:\a\alias.exe", "Alias", 50);
        var other = Track(@"c:\a\other.exe", "Other");
        using (var lib = AppLibrary.Open(_path)) { lib.Merge(alias, main); lib.Merge(other, main); }
        var cat = Catalogue();

        cat.ApplyChange(AppChange.MakeMain(alias)).Should().Be(CatalogueOutcome.Done);
        cat.Tracked().Apps.Single(a => a.Id == alias).Merged.Select(m => m.Id).Should().BeEquivalentTo([main, other]);

        cat.ApplyChange(AppChange.Unmerge(other)).Should().Be(CatalogueOutcome.Done);
        cat.Tracked().Apps.Select(a => a.Id).Should().BeEquivalentTo([alias, other]);

        cat.ApplyChange(AppChange.ClearHistory(alias)).Should().Be(CatalogueOutcome.Done);
        cat.Tracked().Apps.Single(a => a.Id == alias).Seconds.Should().Be(0);
    }

    [Fact]
    public void Making_main_an_app_that_is_not_merged_is_rejected()
    {
        var id = Track(@"c:\a\one.exe", "One");

        Catalogue().ApplyChange(AppChange.MakeMain(id)).Should().Be(CatalogueOutcome.Rejected);
    }

    [Fact]
    public void Retiring_a_tracked_entry_stops_it_and_everything_merged_into_it_counting_as_added()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var alias = Track(@"c:\a\alias.exe", "Alias");
        using (var lib = AppLibrary.Open(_path)) lib.Merge(alias, main);
        var cat = Catalogue();
        cat.AddApp(new NewApp(@"c:\a\main.exe", "Main", null), null);
        cat.AddApp(new NewApp(@"c:\a\alias.exe", "Alias", null), null);

        cat.ApplyChange(AppChange.Retire(MainEntry(main))).Should().Be(CatalogueOutcome.Done);

        cat.AddedCount.Should().Be(0);
        cat.Library().StateOf(@"c:\a\main.exe", null)!.State.Should().Be(AppState.Retired);
    }

    [Fact]
    public void Every_change_reports_a_missing_database()
    {
        var cat = Catalogue(Path.Combine(_dir, "nope", "missing.db"));
        var app = new LibraryApp(1, @"c:\a\one.exe", null, "One", AppState.Tracked, null);
        var entry = new TrackedEntry(1, @"c:\a\one.exe", "One", 0, 0, []);
        AppChange[] changes =
        [
            AppChange.Rename(1, "X"), AppChange.SetKind(1, AppKind.Game), AppChange.SetBackground(1, true), AppChange.Retire(app),
            AppChange.Retire(entry), AppChange.Restore(app), AppChange.MakeMain(1), AppChange.Unmerge(1), AppChange.ClearHistory(1),
        ];

        changes.Select(cat.ApplyChange).Should().OnlyContain(o => o == CatalogueOutcome.Unavailable);
    }

    [Fact]
    public void The_tracked_snapshot_lists_tracked_apps_with_stored_icons_except_for_those_the_caller_has()
    {
        var one = Track(@"c:\a\one.exe", "One", 60);
        var two = Track(@"c:\a\two.exe", "Two");
        var cat = Catalogue();

        var all = cat.Tracked();
        var rest = cat.Tracked(iconsKnown: new HashSet<long> { one });

        all.Outcome.Should().Be(CatalogueOutcome.Done);
        all.Apps.Select(a => a.Id).Should().BeEquivalentTo([one, two]);
        all.Icons[one].Should().Equal(Encoding.UTF8.GetBytes(@"stored:c:\a\one.exe"));
        rest.Icons.Keys.Should().BeEquivalentTo([two]);
        cat.StoredIcon(two).Should().Equal(Encoding.UTF8.GetBytes(@"stored:c:\a\two.exe"));
    }

    [Fact]
    public void The_tracked_snapshot_and_stored_icon_report_a_missing_database()
    {
        var cat = Catalogue(Path.Combine(_dir, "nope", "missing.db"));

        var snapshot = cat.Tracked();

        snapshot.Outcome.Should().Be(CatalogueOutcome.Unavailable);
        snapshot.Apps.Should().BeEmpty();
        cat.StoredIcon(1).Should().BeNull();
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
    public void Confirm_uses_the_icon_already_extracted_at_tile_size_instead_of_extracting_one()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        var icons = new AppIconSource(_path, _desktop);
        icons.ForPath(@"C:\B\FRESH.EXE", null, null, 256, cachedOnly: false, out _);
        _desktop.IconCalls.Clear();
        var plan = new AppCatalogue(_path, _desktop, icons).OpenMergePlan(main, iconPx: 256)!;
        plan.Candidates.Single().Png.Should().Equal(Encoding.UTF8.GetBytes(@"png:C:\B\FRESH.EXE"));
        plan.Toggle(plan.Candidates.Single().Key);

        plan.Confirm();

        _desktop.IconCalls.Should().BeEmpty();
        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(MainEntry(main).Merged.Single().Id).Should().Equal(Encoding.UTF8.GetBytes(@"png:C:\B\FRESH.EXE"));
    }

    [Fact]
    public void Opening_a_merge_plan_gives_an_installed_app_no_icon_until_one_was_extracted_and_never_extracts()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));

        var plan = Catalogue().OpenMergePlan(main)!;

        plan.Candidates.Single().Png.Should().BeNull();
        _desktop.IconCalls.Should().BeEmpty();
    }

    private const string BlockInserts = "CREATE TRIGGER block_inserts BEFORE INSERT ON apps BEGIN SELECT RAISE(ABORT, 'disk gone'); END";

    private void Exec(string sql)
    {
        using var db = Db.Open(_path);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long AddedUtc(long appId)
    {
        using var db = Db.Open(_path);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT added_utc FROM apps WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", appId);
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Confirm_failing_midway_reports_unavailable_keeps_the_plan_and_leaves_earlier_steps_in_the_database()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var first = Track(@"c:\a\first.exe", "A first");   // sorted first: merged before the failure
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Z fresh"));
        _desktop.OnIcon = _ => Exec(BlockInserts); // adding the installed app then fails in the database
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
    public void Confirming_again_after_a_partial_failure_finishes_the_job_without_restoring_a_stopped_app_twice()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        var gone = Retire(@"c:\a\gone.exe", "A gone");   // restored and merged before the failure
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Z fresh"));
        _desktop.OnIcon = _ => Exec(BlockInserts);
        var cat = Catalogue();
        var plan = cat.OpenMergePlan(main)!;
        foreach (var c in plan.Candidates) plan.Toggle(c.Key);
        plan.Confirm().Should().Be(CatalogueOutcome.Unavailable);
        _desktop.OnIcon = null;
        Exec("DROP TRIGGER block_inserts");
        Exec($"UPDATE apps SET added_utc = 12345 WHERE id = {gone}"); // a Restore now would reset this

        plan.Confirm().Should().Be(CatalogueOutcome.Done);

        AddedUtc(gone).Should().Be(12345);
        MainEntry(main).Merged.Select(m => m.ExePath).Should().BeEquivalentTo([@"c:\a\gone.exe", @"c:\b\fresh.exe"]);
        cat.AddedCount.Should().Be(2);
    }

    [Fact]
    public void An_icon_that_cannot_be_extracted_only_means_the_added_app_has_no_icon()
    {
        var main = Track(@"c:\a\main.exe", "Main");
        _desktop.Installed.Add(Installed(@"c:\b\fresh.exe", "Fresh"));
        _desktop.IconFails[@"c:\b\fresh.exe"] = new IOException("icon file locked");
        var plan = Catalogue().OpenMergePlan(main)!;
        plan.Toggle(plan.Candidates.Single().Key);

        plan.Confirm().Should().Be(CatalogueOutcome.Done);

        var merged = MainEntry(main).Merged.Should().ContainSingle().Subject;
        using var lib = AppLibrary.Open(_path);
        lib.GetIcon(merged.Id).Should().BeNull();
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
