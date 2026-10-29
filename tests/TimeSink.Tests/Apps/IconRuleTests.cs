using System.Text;
using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

/// <summary>Decision 154: the exe's icon, Steam's art only when the exe icon is tiny or unreadable.</summary>
public sealed class IconRuleTests
{
    private const string Exe = @"c:\games\game.exe";
    private const string Art = @"c:\steam\art.jpg";
    private static readonly byte[] ExePng = Encoding.UTF8.GetBytes("png:" + Exe);
    private static readonly byte[] ArtPng = Encoding.UTF8.GetBytes("art:" + Art);

    private static byte[]? Run(FakeIcons icons, string? aumid = null, string? image = Art) => IconRule.Extract(icons, Exe, aumid, image, 64);

    [Fact]
    public void The_start_tile_wins_when_there_is_one()
    {
        var icons = new FakeIcons { Tiles = { "Pkg!App" } };

        Run(icons, aumid: "Pkg!App").Should().Equal(Encoding.UTF8.GetBytes("tile:Pkg!App"));
        icons.IconCalls.Should().BeEmpty();
    }

    [Fact]
    public void Without_a_tile_the_exe_icon_is_used()
    {
        var icons = new FakeIcons { Tiles = { } };

        Run(icons, aumid: "Pkg!App", image: null).Should().Equal(ExePng);
    }

    [Theory]
    [InlineData(256)] // sharp
    [InlineData(128)] // exactly sharp enough
    public void A_sharp_exe_icon_beats_the_art(int embedded)
    {
        var icons = new FakeIcons { Embedded = { [Exe] = embedded } };

        Run(icons).Should().Equal(ExePng);
        icons.ImageCalls.Should().BeEmpty();
    }

    [Fact]
    public void An_exe_whose_icon_sizes_cannot_be_read_is_trusted()
    {
        var icons = new FakeIcons { Embedded = { [Exe] = 0 } };

        Run(icons).Should().Equal(ExePng);
    }

    [Theory]
    [InlineData(32, 1024, true)]   // tiny exe icon, big art
    [InlineData(127, 128, true)]   // just under the line, art has more pixels
    [InlineData(64, 64, false)]    // art no bigger
    [InlineData(64, 32, false)]    // art smaller
    public void A_tiny_exe_icon_gives_way_only_to_art_with_more_pixels(int embedded, int artSide, bool artWins)
    {
        var icons = new FakeIcons { Embedded = { [Exe] = embedded }, ImageSides = { [Art] = artSide } };

        Run(icons).Should().Equal(artWins ? ArtPng : ExePng);
    }

    [Fact]
    public void Art_of_unknown_size_does_not_replace_a_tiny_exe_icon()
    {
        var icons = new FakeIcons { Embedded = { [Exe] = 32 } };
        icons.ImageSides.Clear();
        var unknown = new UnknownSide(icons);

        IconRule.Extract(unknown, Exe, null, Art, 64).Should().Equal(ExePng);
    }

    [Fact]
    public void Art_stands_in_when_the_exe_cannot_be_read()
    {
        var icons = new FakeIcons { NoIcon = { Exe } };

        Run(icons).Should().Equal(ArtPng);
    }

    [Fact]
    public void Nothing_is_found_when_neither_can_be_read()
    {
        var icons = new FakeIcons { NoIcon = { Exe } };

        Run(icons, image: null).Should().BeNull();
    }

    private sealed class UnknownSide(FakeIcons inner) : IIconExtractor
    {
        public byte[]? FromTile(string aumid, int size) => inner.FromTile(aumid, size);
        public byte[]? FromExe(string exePath, int size) => inner.FromExe(exePath, size);
        public int EmbeddedSide(string exePath) => inner.EmbeddedSide(exePath);
        public int? ImageSide(string imageFile) => null;
        public byte[]? FromImage(string imageFile, int size) => inner.FromImage(imageFile, size);
    }
}
