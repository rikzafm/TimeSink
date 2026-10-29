using TimeSink.Core.Apps;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class AppKindTests : IDisposable
{
    private readonly WrappedFixture _f = new();
    public void Dispose() => _f.Dispose();

    [Theory]
    [InlineData(@"D:\Games\Genshin Impact\GenshinImpact.exe", "Genshin Impact", AppKind.Gacha)]
    [InlineData(@"C:\Steam\steamapps\common\Hades\Hades.exe", "Hades", AppKind.Game)]
    [InlineData(@"C:\Windows\notepad.exe", "Notepad", AppKind.Other)]
    public void Detect_finds_gacha_names_then_steam_folders(string exe, string name, AppKind expected) =>
        AppKinds.Detect(exe, name).Should().Be(expected);

    [Fact]
    public void A_stored_kind_beats_detection_and_next_cycles_other_game_gacha()
    {
        AppKinds.Resolve(0, @"C:\Steam\steamapps\common\Hades\Hades.exe", "Hades").Should().Be(AppKind.Other);
        AppKinds.Resolve(null, @"C:\Steam\steamapps\common\Hades\Hades.exe", "Hades").Should().Be(AppKind.Game);
        AppKinds.Next(AppKind.Other).Should().Be(AppKind.Game);
        AppKinds.Next(AppKind.Game).Should().Be(AppKind.Gacha);
        AppKinds.Next(AppKind.Gacha).Should().Be(AppKind.Other);
    }

    [Fact]
    public void Wrapped_splits_games_and_gacha_into_their_own_podiums()
    {
        using (var lib = AppLibrary.Open(_f.DbPath))
        {
            lib.SetKind(_f.Beta, AppKind.Game);
            lib.SetKind(_f.Gamma, AppKind.Gacha);
            lib.SetKind(_f.Delta, AppKind.Game);
        }

        var r = Q.Wrapped(_f.DbPath, WrappedFixture.Now, WrappedFixture.Zone);

        r.GamesTop!.Podium.Select(a => a.Name).Should().Equal("Beta", "Delta");
        r.GamesTop.Seconds.Should().Be(10800 + 600);
        r.GachaTop!.Podium.Select(a => a.Name).Should().Equal("Gamma");
        r.GachaTop.Others.Should().BeEmpty();
    }
}
