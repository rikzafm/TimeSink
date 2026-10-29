using FluentAssertions;
using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class SteamLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ts-steam-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Game(string steamapps, int id, string name, string dir, params (string File, int Size)[] exes)
    {
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{id}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"name\"\t\t\"{name}\"\n\t\"installdir\"\t\t\"{dir}\"\n}}\n");
        foreach (var (file, size) in exes)
        {
            var p = Path.Combine(steamapps, "common", dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[size]);
        }
    }

    [Fact]
    public void Finds_games_in_every_library_folder_and_picks_the_largest_non_helper_exe()
    {
        var second = Path.Combine(_root, "Lib2");
        Directory.CreateDirectory(second);
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + second.Replace("\\", "\\\\") + "\"\n\t}\n}\n");

        Game(Path.Combine(_root, "steamapps"), 1, "Hades", "Hades", ("Hades.exe", 500), ("UnityCrashHandler64.exe", 900));
        Game(Path.Combine(second, "steamapps"), 2, "Deep Rock", "DRG", (@"Binaries\Win64\FSD.exe", 800), ("launcher.exe", 100));

        var games = SteamLibrary.Scan(_root);

        games.Select(g => g.Name).Should().BeEquivalentTo("Hades", "Deep Rock");
        Path.GetFileName(games.Single(g => g.Name == "Hades").ExePath).Should().Be("Hades.exe");
        Path.GetFileName(games.Single(g => g.Name == "Deep Rock").ExePath).Should().Be("FSD.exe");
    }

    [Fact]
    public void Cached_art_prefers_the_big_capsule_then_header_then_the_old_names_and_the_small_icon_last()
    {
        var cache = Path.Combine(_root, "appcache", "librarycache");
        void Put(string app, params string[] relative)
        {
            foreach (var r in relative)
            {
                var file = Path.Combine(cache, app, r);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, [1]);
            }
        }
        var icon = new string('a', 40) + ".jpg";
        Put("7", icon, "library_header.jpg", "library_600x900.jpg", "header.jpg");
        Put("8", icon, Path.Combine("abc123", "library_header.jpg"));
        Put("9", icon, "header.jpg", "library_capsule.jpg");
        Put("10", "header.jpg");
        Put("11", icon);

        Path.GetFileName(SteamLibrary.CachedArt(_root, "7")).Should().Be("library_600x900.jpg");
        SteamLibrary.CachedArt(_root, "8").Should().EndWith(Path.Combine("abc123", "library_header.jpg"));
        Path.GetFileName(SteamLibrary.CachedArt(_root, "9")).Should().Be("library_capsule.jpg");
        Path.GetFileName(SteamLibrary.CachedArt(_root, "10")).Should().Be("header.jpg");
        Path.GetFileName(SteamLibrary.CachedArt(_root, "11")).Should().Be(icon);
        SteamLibrary.CachedArt(_root, "12").Should().BeNull();
    }

    [Fact]
    public void Skips_redistributables_and_missing_roots()
    {
        Game(Path.Combine(_root, "steamapps"), 228980, "Steamworks Common Redistributables", "Steamworks Shared", ("tool.exe", 10));

        SteamLibrary.Scan(_root).Should().BeEmpty();
        SteamLibrary.Scan(null).Should().BeEmpty();
        SteamLibrary.Scan(Path.Combine(_root, "nope")).Should().BeEmpty();
    }

    [Fact]
    public void Catalog_lists_steam_games_with_the_steam_title_and_source()
    {
        var apps = InstalledCatalog.Build([new ShortcutEntry("Hades shortcut", @"C:\g\Hades.exe")], [], [new SteamGame("Hades", @"C:\g\Hades.exe"), new SteamGame("Celeste", @"C:\g\Celeste.exe")]);

        apps.Should().HaveCount(2);
        apps.Should().OnlyContain(a => a.IsSteam);
        apps.Select(a => a.Name).Should().Equal("Celeste", "Hades");
    }

    [Theory]
    [InlineData("appmanifest_730.acf", true)]
    [InlineData("APPMANIFEST_730.ACF", true)]
    [InlineData("libraryfolders.vdf", true)]
    [InlineData("LibraryFolders.vdf", true)]
    [InlineData("appmanifest_730.acf.tmp", false)]
    [InlineData("appmanifest_730.vdf", false)]
    [InlineData("libraryfolders.vdf.bak", false)]
    [InlineData("downloading", false)]
    [InlineData(null, false)]
    public void IsLibraryChange_is_true_only_for_app_manifests_and_the_library_list(string? name, bool expected)
    {
        SteamLibrary.IsLibraryChange(name).Should().Be(expected);
    }
}
