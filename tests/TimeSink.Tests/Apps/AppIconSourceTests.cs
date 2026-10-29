using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Icons;

namespace TimeSink.Tests.Apps;

public sealed class AppIconSourceTests : IDisposable
{
    private sealed class FakeDesktop : IDesktop
    {
        public List<(string Exe, int Size)> IconCalls { get; } = [];
        public List<(string? Aumid, string? ImageFile)> Args { get; } = [];
        public HashSet<string> NoIcon { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? IconFails { get; set; }

        public WindowCapture CaptureWindows() => new([], new Dictionary<string, string>());

        public byte[]? ExtractIcon(string exePath, string? aumid, string? imageFile, int size)
        {
            IconCalls.Add((exePath, size));
            Args.Add((aumid, imageFile));
            if (IconFails is { } fail) throw fail;
            return NoIcon.Contains(exePath) ? null : Encoding.UTF8.GetBytes("png:" + exePath);
        }

        public IReadOnlyList<InstalledApp> ScanInstalled() => [];
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly FakeDesktop _desktop = new();

    public AppIconSourceTests()
    {
        _path = Path.Combine(_dir, "timesink.db");
        Db.Open(_path).Dispose();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AppIconSource Source() => new(_path, _desktop);

    private long Track(string exe, byte[]? png)
    {
        using var lib = AppLibrary.Open(_path);
        return lib.AddTracked(new NewApp(exe, Path.GetFileNameWithoutExtension(exe), null), 100, png).AppId;
    }

    private byte[]? Stored(long id)
    {
        using var lib = AppLibrary.Open(_path);
        return lib.GetIcon(id);
    }

    [Fact]
    public void A_stored_icon_is_returned_without_extracting()
    {
        var id = Track(@"c:\a\one.exe", [1, 2, 3]);

        var icons = Source().ForApps([id]);

        icons[id].Should().Equal(1, 2, 3);
        _desktop.IconCalls.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_icon_is_extracted_and_stored()
    {
        var id = Track(@"c:\a\one.exe", null);

        var icons = Source().ForApps([id]);

        var expected = Encoding.UTF8.GetBytes(@"png:c:\a\one.exe");
        icons[id].Should().Equal(expected);
        Stored(id).Should().Equal(expected);
        _desktop.IconCalls.Should().Equal((@"c:\a\one.exe", 64));
    }

    [Fact]
    public void An_app_with_no_icon_is_left_out_and_tried_once()
    {
        var id = Track(@"c:\a\one.exe", null);
        _desktop.NoIcon.Add(@"c:\a\one.exe");
        var source = Source();

        source.ForApps([id]).Should().BeEmpty();
        source.ForApps([id]).Should().BeEmpty();

        _desktop.IconCalls.Should().HaveCount(1);
    }

    [Fact]
    public void A_found_icon_is_answered_from_memory_on_the_next_request()
    {
        var id = Track(@"c:\a\one.exe", [1, 2, 3]);
        var source = Source();
        source.ForApps([id]);
        using (var lib = AppLibrary.Open(_path)) lib.PutIcon(id, [9, 9]);

        source.ForApps([id])[id].Should().Equal(1, 2, 3);
    }

    [Fact]
    public void TimeSinks_own_row_shows_the_chosen_tray_icon_and_replaces_a_stale_stored_one()
    {
        var id = Track(@"c:\app\TimeSink.UI.exe", [7, 7]);
        SettingsStore.Set(_path, "ui.icon", "comic.ticks");
        var expected = IconCatalogue.ReadFrame("comic.ticks", 64);

        var icons = Source().ForApps([id]);

        icons[id].Should().Equal(expected);
        Stored(id).Should().Equal(expected);
        _desktop.IconCalls.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_chosen_tray_icon_falls_back_to_the_default()
    {
        var id = Track(@"c:\app\TimeSink.UI.exe", null);
        SettingsStore.Set(_path, "ui.icon", "no.such.icon");

        Source().ForApps([id])[id].Should().Equal(IconCatalogue.ReadFrame(IconCatalogue.DefaultId, 64));
    }

    private const string Balatro = @"c:\games\steamapps\common\Balatro\Balatro.exe";
    private static readonly byte[] BalatroExe = Encoding.UTF8.GetBytes("png:" + Balatro);

    private string? RepairFlag() => SettingsStore.Get(_path, "icons.steamRepaired");

    [Fact]
    public void A_stored_Steam_icon_is_re_extracted_from_the_exe_alone_and_stored()
    {
        var id = Track(Balatro, [7, 7]);

        Source().ForApps([id])[id].Should().Equal(BalatroExe);

        Stored(id).Should().Equal(BalatroExe);
        _desktop.IconCalls.Should().Equal((Balatro, 64));
        _desktop.Args.Single().ImageFile.Should().BeNull();
        RepairFlag().Should().Be("1");
    }

    [Fact]
    public void The_Steam_repair_covers_every_Steam_app_not_only_the_ones_asked_for()
    {
        var one = Track(Balatro, [7, 7]);
        var two = Track(@"c:\games\steamapps\common\Atri\ATRI.exe", [8, 8]);

        Source().ForApps([one]);

        Stored(two).Should().Equal(Encoding.UTF8.GetBytes(@"png:c:\games\steamapps\common\Atri\ATRI.exe"));
    }

    [Fact]
    public void The_Steam_repair_does_not_run_again_in_a_later_run()
    {
        var id = Track(Balatro, [7, 7]);
        Source().ForApps([id]);

        var icons = Source().ForApps([id]);

        icons[id].Should().Equal(BalatroExe);
        _desktop.IconCalls.Should().HaveCount(1);
    }

    [Fact]
    public void A_Steam_exe_that_cannot_be_read_keeps_its_icon_and_the_repair_is_still_marked_done()
    {
        var id = Track(Balatro, [7, 7]);
        _desktop.NoIcon.Add(Balatro);

        Source().ForApps([id])[id].Should().Equal(7, 7);

        Stored(id).Should().Equal(7, 7);
        RepairFlag().Should().Be("1");
    }

    [Fact]
    public void The_Steam_repair_leaves_other_apps_alone()
    {
        var id = Track(@"c:\a\one.exe", [1, 2, 3]);

        Source().ForApps([id])[id].Should().Equal(1, 2, 3);

        _desktop.IconCalls.Should().BeEmpty();
        RepairFlag().Should().Be("1");
    }

    [Fact]
    public void A_failed_extraction_by_id_leaves_the_app_without_an_icon()
    {
        var id = Track(@"c:\a\one.exe", null);
        _desktop.IconFails = new IOException("icon file locked");

        Source().ForApps([id]).Should().BeEmpty();
        Stored(id).Should().BeNull();
    }

    [Fact]
    public void A_missing_database_leaves_apps_without_icons()
    {
        var source = new AppIconSource(Path.Combine(_dir, "nope", "timesink.db"), _desktop);

        source.ForApps([1, 2]).Should().BeEmpty();
        source.Stored([1, 2]).Should().BeEmpty();
    }

    [Fact]
    public void Stored_reads_what_is_kept_and_never_extracts()
    {
        var one = Track(@"c:\a\one.exe", [1]);
        var two = Track(@"c:\a\two.exe", null);

        var stored = Source().Stored([one, two]);

        stored.Should().ContainKey(one).WhoseValue.Should().Equal(1);
        stored.Should().NotContainKey(two);
        _desktop.IconCalls.Should().BeEmpty();
    }

    [Fact]
    public void A_path_is_extracted_once_per_size_whatever_its_case()
    {
        var source = Source();

        var small = source.ForPath(@"c:\a\One.exe", null, null, 64, cachedOnly: false, out var first);
        source.ForPath(@"C:\A\ONE.EXE", null, null, 64, cachedOnly: false, out var again);
        var large = source.ForPath(@"c:\a\one.exe", null, null, 256, cachedOnly: false, out var other);

        small.Should().Equal(Encoding.UTF8.GetBytes(@"png:c:\a\One.exe"));
        (first, again, other).Should().Be((true, false, true));
        large.Should().NotBeNull();
        _desktop.IconCalls.Select(c => c.Size).Should().Equal(64, 256);
    }

    [Fact]
    public void Cached_only_extracts_nothing_and_gives_null_for_an_unknown_path()
    {
        var source = Source();

        source.ForPath(@"c:\a\one.exe", null, null, 64, cachedOnly: true, out var extracted).Should().BeNull();

        extracted.Should().BeFalse();
        _desktop.IconCalls.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_extraction_by_path_is_remembered_as_no_icon()
    {
        var source = Source();
        _desktop.IconFails = new IOException("icon file locked");

        source.ForPath(@"c:\a\one.exe", null, null, 64, cachedOnly: false, out var extracted).Should().BeNull();
        source.ForPath(@"c:\a\one.exe", null, null, 64, cachedOnly: false, out var again).Should().BeNull();

        (extracted, again).Should().Be((true, false));
        _desktop.IconCalls.Should().HaveCount(1);
    }

    [Fact]
    public void The_aumid_and_image_file_reach_the_extraction_and_do_not_change_the_cache_key()
    {
        var source = Source();

        source.ForPath(@"c:\a\one.exe", "Pkg!App", @"c:\art\logo.png", 256, cachedOnly: false, out _);
        source.ForPath(@"c:\a\one.exe", null, null, 256, cachedOnly: false, out var again);

        _desktop.Args.Should().Equal(("Pkg!App", @"c:\art\logo.png"));
        again.Should().BeFalse();
    }
}
