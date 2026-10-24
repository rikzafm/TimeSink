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
    public void Cached_art_prefers_the_community_icon_then_capsule_then_header()
    {
        var cache = Path.Combine(_root, "appcache", "librarycache");
        Directory.CreateDirectory(Path.Combine(cache, "7"));
        Directory.CreateDirectory(Path.Combine(cache, "8"));
        File.WriteAllBytes(Path.Combine(cache, "7", "header.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "7", "library_capsule.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "7", new string('a', 40) + ".jpg"), [1]);
        File.WriteAllBytes(Path.Combine(cache, "8", "header.jpg"), [1]);

        Path.GetFileName(SteamLibrary.CachedArt(_root, "7")).Should().Be(new string('a', 40) + ".jpg");
        Path.GetFileName(SteamLibrary.CachedArt(_root, "8")).Should().Be("header.jpg");
        SteamLibrary.CachedArt(_root, "9").Should().BeNull();
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
}
