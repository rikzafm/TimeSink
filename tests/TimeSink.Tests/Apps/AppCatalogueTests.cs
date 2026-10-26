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

        public WindowCapture CaptureWindows() => CaptureFails is { } e ? throw e : new WindowCapture(Windows.ToList(), Aumids);

        public byte[]? ExtractIcon(string exePath, string? aumid, string? imageFile, int size)
        {
            IconCalls.Add((exePath, size));
            return Encoding.UTF8.GetBytes("png:" + exePath);
        }

        public IReadOnlyList<InstalledApp> ScanInstalled() => [];
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
}
