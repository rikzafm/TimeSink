using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Apps;

public sealed class TrackedTabTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public TrackedTabTests()
    {
        _path = Path.Combine(_dir, "timesink.db");
        Db.Open(_path).Dispose();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private long Add(AppLibrary lib, string exe, string name, long seconds)
    {
        var id = lib.AddTracked(new NewApp(exe, name, null), 100, null).AppId;
        if (seconds > 0)
        {
            using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO daily_usage (app_id, day, seconds) VALUES ($a, '2026-01-01', $s)";
            cmd.Parameters.AddWithValue("$a", id);
            cmd.Parameters.AddWithValue("$s", seconds);
            cmd.ExecuteNonQuery();
        }
        return id;
    }

    [Fact]
    public void Tracked_lists_apps_by_name_with_lifetime_seconds()
    {
        using var lib = AppLibrary.Open(_path);
        Add(lib, @"C:\b.exe", "Beta", 60);
        Add(lib, @"C:\a.exe", "alpha", 0);

        lib.Tracked().Select(a => (a.DisplayName, a.Seconds)).Should().Equal(("alpha", 0L), ("Beta", 60L));
    }

    [Fact]
    public void Rename_changes_the_display_name_and_ignores_blank_names()
    {
        using var lib = AppLibrary.Open(_path);
        var id = Add(lib, @"C:\a.exe", "A", 5);

        lib.Rename(id, "  Work editor ").Should().BeTrue();
        lib.Rename(id, "   ").Should().BeFalse();

        lib.Tracked().Should().ContainSingle().Which.Should().Match<TrackedEntry>(a => a.DisplayName == "Work editor" && a.Seconds == 5);
    }

    [Fact]
    public void Merge_counts_the_source_under_the_target_and_unmerge_separates_them_again()
    {
        using var lib = AppLibrary.Open(_path);
        var chrome = Add(lib, @"C:\chrome.exe", "Chrome", 100);
        var beta = Add(lib, @"C:\beta.exe", "Chrome Beta", 40);

        lib.Merge(beta, chrome).Should().BeTrue();

        var merged = lib.Tracked().Should().ContainSingle().Subject;
        merged.Id.Should().Be(chrome);
        merged.Seconds.Should().Be(140);
        merged.Merged.Should().ContainSingle().Which.Should().Be(new MergedApp(beta, @"C:\beta.exe", "Chrome Beta", 40));

        lib.Unmerge(beta);
        lib.Tracked().Select(a => a.Seconds).Should().BeEquivalentTo([100L, 40L]);
    }

    [Fact]
    public void A_merged_app_whose_main_app_is_retired_is_listed_as_its_own_app()
    {
        using var lib = AppLibrary.Open(_path);
        var chrome = Add(lib, @"C:\chrome.exe", "Chrome", 100);
        var beta = Add(lib, @"C:\beta.exe", "Chrome Beta", 40);
        lib.Merge(beta, chrome).Should().BeTrue();
        using (var c = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"UPDATE apps SET state = 'retired', retired_utc = 1 WHERE id = {chrome}";
            cmd.ExecuteNonQuery();
        }

        var listed = lib.Tracked().Should().ContainSingle().Subject;

        listed.Id.Should().Be(beta);
        listed.Seconds.Should().Be(40);
        listed.Merged.Should().BeEmpty();
    }

    [Fact]
    public void Merging_an_app_that_has_aliases_moves_them_along_and_rejects_bad_targets()
    {
        using var lib = AppLibrary.Open(_path);
        var a = Add(lib, @"C:\a.exe", "A", 1);
        var b = Add(lib, @"C:\b.exe", "B", 2);
        var c = Add(lib, @"C:\c.exe", "C", 4);

        lib.Merge(a, b).Should().BeTrue();
        lib.Merge(a, c).Should().BeFalse("a is already merged into b, so it is no longer a top-level app");
        lib.Merge(c, a).Should().BeFalse("a is an alias and cannot be a target");
        lib.Merge(c, c).Should().BeFalse();
        lib.Merge(b, c).Should().BeTrue();

        var only = lib.Tracked().Should().ContainSingle().Subject;
        only.Id.Should().Be(c);
        only.Seconds.Should().Be(7);
        only.Merged.Select(m => m.Id).Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public void MakeMain_swaps_the_keeper_and_carries_the_other_aliases_and_the_background_flag()
    {
        using var lib = AppLibrary.Open(_path);
        var a = Add(lib, @"C:\a.exe", "A", 1);
        var b = Add(lib, @"C:\b.exe", "B", 2);
        var c = Add(lib, @"C:\c.exe", "C", 4);
        lib.Merge(b, a);
        lib.Merge(c, a);
        lib.SetBackground(a, true);

        lib.MakeMain(a).Should().BeFalse("a is already the main app");
        lib.MakeMain(b).Should().BeTrue();

        var only = lib.Tracked().Should().ContainSingle().Subject;
        only.Id.Should().Be(b);
        only.Seconds.Should().Be(7);
        only.Background.Should().BeTrue();
        only.Merged.Select(m => m.Id).Should().BeEquivalentTo([a, c]);
    }

    [Fact]
    public void Stopping_and_restoring_a_merged_app_carries_its_aliases()
    {
        using var lib = AppLibrary.Open(_path);
        var a = Add(lib, @"C:\a.exe", "A", 1);
        var b = Add(lib, @"C:\b.exe", "B", 2);
        lib.Merge(b, a);

        lib.Retire(a, 200);
        lib.Tracked().Should().BeEmpty();
        lib.ExcludedFromRunning().Paths.Should().NotContain(@"C:\b.exe");

        lib.Restore(a, 300);
        lib.Tracked().Should().ContainSingle().Which.Merged.Should().ContainSingle();
    }

    [Fact]
    public void A_merged_app_counts_as_tracked_with_its_keeper()
    {
        using var lib = AppLibrary.Open(_path);
        var a = Add(lib, @"C:\a.exe", "A", 1);
        var b = Add(lib, @"C:\b.exe", "B", 2);
        lib.Merge(b, a);

        lib.MergedAliases().Should().ContainSingle().Which.Should().Be(new MergedAlias(@"C:\b.exe", null, a));
        lib.AddTracked(new NewApp(@"C:\b.exe", "B", null), 50, null).Should().Be(new AddResult(a, AddOutcome.AlreadyTracked));

        lib.Retire(a, 200);
        lib.AddTracked(new NewApp(@"C:\b.exe", "B", null), 300, null).Should().Be(new AddResult(a, AddOutcome.Restored));
        lib.Tracked().Should().ContainSingle().Which.Merged.Should().ContainSingle();
    }
}
