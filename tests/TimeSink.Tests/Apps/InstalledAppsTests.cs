using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class InstalledAppsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-inst-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Lnk(string root, string relative)
    {
        var p = Path.Combine(_dir, root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "");
        return p;
    }

    // fake .lnk resolver: the shortcut's file name (without extension) decides the target
    private static string? Resolve(string lnk) => Path.GetFileNameWithoutExtension(lnk) switch
    {
        "Broken" => null,
        "Site" => @"C:\Links\page.url",
        "Uninstall Foo" => @"C:\Foo\unins000.exe",
        var n => @"C:\Apps\" + n.Replace(" ", "") + ".exe",
    };

    [Fact]
    public void Scan_resolves_shortcuts_in_all_roots_recursively_and_keeps_only_exe_targets()
    {
        Lnk("common", @"Zoom.lnk");
        Lnk("common", @"Dev\Visual Studio.lnk");
        Lnk("user", @"Notepad2.lnk");
        Lnk("user", @"Uninstall Foo.lnk");
        Lnk("user", @"Broken.lnk");
        Lnk("user", @"Site.lnk");
        Lnk("user", @"readme.txt");

        var found = StartMenuScanner.Scan([Path.Combine(_dir, "common"), Path.Combine(_dir, "user"), Path.Combine(_dir, "missing")], Resolve);

        found.Select(s => (s.Name, s.TargetPath)).Should().BeEquivalentTo(new[]
        {
            ("Zoom", @"C:\Apps\Zoom.exe"),
            ("Visual Studio", @"C:\Apps\VisualStudio.exe"),
            ("Notepad2", @"C:\Apps\Notepad2.exe"),
            ("Uninstall Foo", @"C:\Foo\unins000.exe"),
        }); // uninstallers are only dropped by the catalogue
    }

    [Fact]
    public void Catalogue_drops_uninstallers_dedupes_by_target_and_merges_packaged_apps_sorted_by_name()
    {
        var shortcuts = new[]
        {
            new ShortcutEntry("Zoom", @"C:\Apps\Zoom.exe"),
            new ShortcutEntry("Zoom Workplace", @"c:\apps\zoom.EXE"),
            new ShortcutEntry("Uninstall Foo", @"C:\Foo\unins000.exe"),
            new ShortcutEntry("Remove Bar", @"C:\Bar\uninstall.exe"),
            new ShortcutEntry("blender", @"C:\Apps\blender.exe"),
        };
        var packaged = new[] { new PackagedApp("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App") };

        var apps = InstalledCatalog.Build(shortcuts, packaged);

        apps.Select(a => a.Name).Should().Equal("blender", "Calculator", "Zoom");
        var calc = apps.Single(a => a.Name == "Calculator");
        calc.ExePath.Should().Be(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        calc.PackageFamily.Should().Be("Microsoft.WindowsCalculator_8wekyb3d8bbwe");
        calc.Aumid.Should().Be("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        apps.Single(a => a.Name == "Zoom").PackageFamily.Should().BeNull();
    }

    private static readonly InstalledApp[] Sample =
    [
        new("Blender", @"C:\b.exe", null, null, "Blender Foundation"),
        new("OBS Studio", @"C:\o.exe", null, null, "OBS Project"),
        new("Obsidian", @"C:\ob.exe", null, null, "Dynalist"),
        new("7-Zip", @"C:\7.exe", null, null, null),
        new("Affinity Photo", @"C:\a.exe", null, null, "Serif"),
        new("ábaco", @"C:\ab.exe", null, null, null),
    ];

    [Fact]
    public void Groups_are_A_to_Z_with_digits_and_symbols_under_hash_last_and_accents_folded()
    {
        var groups = InstalledCatalog.Group(Sample);

        groups.Select(g => g.Letter).Should().Equal("A", "B", "O", "#");
        groups[0].Apps.Select(a => a.Name).Should().Equal("ábaco", "Affinity Photo");
        groups[2].Apps.Select(a => a.Name).Should().Equal("OBS Studio", "Obsidian");
        groups[3].Apps.Select(a => a.Name).Should().Equal("7-Zip");
    }

    [Fact]
    public void Search_matches_name_or_publisher_case_insensitively_and_blank_returns_everything()
    {
        InstalledCatalog.Search(Sample, "  OBS ").Select(a => a.Name).Should().BeEquivalentTo("OBS Studio", "Obsidian"); // publisher "OBS Project"
        InstalledCatalog.Search(Sample, "serif").Select(a => a.Name).Should().Equal("Affinity Photo");
        InstalledCatalog.Search(Sample, "").Should().HaveCount(6);
        InstalledCatalog.Search(Sample, "nothing like this").Should().BeEmpty();
    }

    [Fact]
    public void Letter_rail_lists_every_letter_with_apps_present_ones_flagged_and_hash_only_when_needed()
    {
        var groups = InstalledCatalog.Group(Sample);
        var rail = InstalledCatalog.Rail(groups);

        rail.Should().Equal("A", "B", "O", "#");
        InstalledCatalog.Rail(InstalledCatalog.Group(Sample.Take(2))).Should().Equal("B", "O");
        InstalledCatalog.Rail([]).Should().BeEmpty();
    }
}
