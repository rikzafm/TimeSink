using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Apps;

public sealed class AppLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public AppLibraryTests()
    {
        _path = Path.Combine(_dir, "timesink.db");
        Db.Open(_path).Dispose();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public void Adding_an_app_makes_it_tracked_from_now_with_its_icon_and_package_family()
    {
        using var lib = AppLibrary.Open(_path);
        var r = lib.AddTracked(new NewApp(@"C:\Tools\Calc.exe", "Calculator", "Microsoft.Calc_8wekyb"), 1_700_000_000, Png);

        r.Outcome.Should().Be(AddOutcome.Added);
        using var db = Db.Open(_path);
        db.GetApps().Should().ContainSingle().Which.Should().Be(new AppRow(r.AppId, @"C:\Tools\Calc.exe", "Calculator", "tracked"));
        // what the Agent's per-tick reload sees
        var store = new SqliteSessionStore(db);
        store.GetTrackedApps().Should().ContainSingle()
            .Which.Should().Be(new TrackedApp(r.AppId, @"C:\Tools\Calc.exe", "Microsoft.Calc_8wekyb", 1_700_000_000));
        lib.GetIcon(r.AppId).Should().Equal(Png);
    }

    [Fact]
    public void Adding_an_already_tracked_app_changes_nothing()
    {
        using var lib = AppLibrary.Open(_path);
        var first = lib.AddTracked(new NewApp(@"C:\Tools\App.exe", "App", null), 100, null);
        var again = lib.AddTracked(new NewApp(@"c:\tools\app.EXE", "App 2", null), 200, null);

        again.Outcome.Should().Be(AddOutcome.AlreadyTracked);
        again.AppId.Should().Be(first.AppId);
        using var db = Db.Open(_path);
        db.GetApps().Should().ContainSingle().Which.DisplayName.Should().Be("App");
    }

    [Fact]
    public void Adding_a_retired_app_restores_it_and_time_counts_from_the_new_add_time()
    {
        long id;
        using (var db = Db.Open(_path))
        {
            id = db.InsertApp(@"C:\Tools\Old.exe", "Old", 100);
            db.SetAppState(id, "retired", 150);
        }
        using var lib = AppLibrary.Open(_path);
        var r = lib.AddTracked(new NewApp(@"C:\Tools\Old.exe", "Old", null), 500, null);

        r.Should().Be(new AddResult(id, AddOutcome.Restored));
        using var check = Db.Open(_path);
        check.GetApps().Single().State.Should().Be("tracked");
        var store = new SqliteSessionStore(check);
        store.GetTrackedApps().Single().AddedUtc.Should().Be(500);
    }

    [Fact]
    public void Excluded_lists_cover_tracked_apps_and_the_ignore_list_but_not_retired_apps()
    {
        using (var db = Db.Open(_path))
        {
            db.InsertApp(@"C:\a\tracked.exe", "T", 1);
            var retired = db.InsertApp(@"C:\a\retired.exe", "R", 1);
            db.SetAppState(retired, "retired", 2);
        }
        using var lib = AppLibrary.Open(_path);
        lib.AddTracked(new NewApp(@"C:\a\calc.exe", "Calc", "Fam.Calc_x"), 3, null);
        lib.Ignore(@"C:\a\Noise.exe", 4);
        lib.Ignore(@"c:\a\noise.EXE", 5); // idempotent, case-insensitive

        var ex = lib.ExcludedFromRunning();
        ex.Paths.Should().BeEquivalentTo([@"C:\a\tracked.exe", @"C:\a\calc.exe", @"C:\a\Noise.exe"]);
        ex.PackageFamilies.Should().BeEquivalentTo(["Fam.Calc_x"]);
    }

    [Fact]
    public void Icon_cache_returns_null_when_empty_and_replaces_the_blob_with_a_new_hash()
    {
        using var lib = AppLibrary.Open(_path);
        var id = lib.AddTracked(new NewApp(@"C:\a\x.exe", "X", null), 1, null).AppId;
        lib.GetIcon(id).Should().BeNull();

        lib.PutIcon(id, Png);
        lib.PutIcon(id, [9, 9]);
        lib.GetIcon(id).Should().Equal(9, 9);
        lib.GetIconHash(id).Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([9, 9])).ToLowerInvariant());
    }

    [Fact]
    public void Icons_can_be_read_for_several_apps_at_once_leaving_out_those_without_one()
    {
        using var lib = AppLibrary.Open(_path);
        var one = lib.AddTracked(new NewApp(@"C:\a\one.exe", "One", null), 1, [1, 1]).AppId;
        var two = lib.AddTracked(new NewApp(@"C:\a\two.exe", "Two", null), 1, null).AppId;
        var three = lib.AddTracked(new NewApp(@"C:\a\three.exe", "Three", null), 1, [3, 3]).AppId;

        var icons = lib.GetIcons([one, two, 999]);

        icons.Keys.Should().BeEquivalentTo([one]);
        icons[one].Should().Equal(1, 1);
        lib.GetIcons([]).Should().BeEmpty();
        lib.GetIcons([three])[three].Should().Equal(3, 3);
    }

    [Fact]
    public void Letter_tile_uses_the_first_letter_or_digit_upper_cased_and_a_stable_palette_slot()
    {
        LetterTile.Letter("  steam").Should().Be("S");
        LetterTile.Letter("(beta) 7zip").Should().Be("B");
        LetterTile.Letter("").Should().Be("?");
        LetterTile.PaletteIndex(7, 6).Should().Be(1);
        LetterTile.PaletteIndex(LetterTile.KeyFor(@"C:\A\x.exe"), 6)
            .Should().Be(LetterTile.PaletteIndex(LetterTile.KeyFor(@"c:\a\X.EXE"), 6)).And.BeInRange(0, 5);
    }
}
