using TimeSink.Core.Icons;

namespace TimeSink.Tests.Icons;

public class IconCatalogueTests
{
    [Fact]
    public void The_default_icon_is_Ticks_Comic()
    {
        IconCatalogue.DefaultId.Should().Be("comic.ticks");
        IconCatalogue.Find(IconCatalogue.DefaultId).Name.Should().Be("Ticks");
        IconCatalogue.Find(IconCatalogue.DefaultId).Style.Should().Be(IconStyle.Comic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("clean.nope")]
    [InlineData("COMIC.TICKS ")]
    public void A_missing_or_unknown_stored_id_resolves_to_the_default(string? stored)
        => IconCatalogue.Resolve(stored).Should().Be("comic.ticks");

    [Fact]
    public void A_known_stored_id_resolves_to_itself()
        => IconCatalogue.Resolve("misprint.burst").Should().Be("misprint.burst");
}

public class IconAssetTests
{
    public static IEnumerable<object[]> Ids() => IconCatalogue.All.Select(i => new object[] { i.Id });

    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_icon_ships_an_ico_with_the_tray_sizes(string id)
    {
        using var s = IconCatalogue.OpenIcon(id);
        var header = new byte[6];
        s.ReadExactly(header);
        BitConverter.ToUInt16(header, 0).Should().Be(0); // reserved
        BitConverter.ToUInt16(header, 2).Should().Be(1); // type: icon
        var count = BitConverter.ToUInt16(header, 4);

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = new byte[16];
            s.ReadExactly(entry);
            sizes.Add(entry[0] == 0 ? 256 : entry[0]);
        }
        sizes.Should().Contain([16, 20, 24, 32, 48, 256]);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 20)]
    [InlineData(22, 24)]
    [InlineData(40, 48)]
    [InlineData(64, 256)]
    [InlineData(1000, 256)]
    public void ReadFrame_returns_the_smallest_png_frame_at_least_the_requested_size(int wanted, int expectedSize)
    {
        var frame = IconCatalogue.ReadFrame("comic.ticks", wanted);
        frame.Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47); // PNG signature
        BitConverter.ToUInt32(frame.Skip(16).Take(4).Reverse().ToArray(), 0).Should().Be((uint)expectedSize); // IHDR width
    }
}
