using TimeSink.Core.Settings;
using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class DensityTests
{
    private static double[] Values(Density d) =>
        [d.Tile, d.TileSmall, d.TileLarge, d.Button, d.TableRow, d.TableRetiredRow, d.TableHead, d.TableTotals,
         d.CardPadY, d.ListGapY, d.AddTileGapY, d.AddTileGapYTight];

    [Fact]
    public void Compact_shrinks_every_row_metric_and_keeps_them_positive()
    {
        var regular = Values(Density.Regular);
        var compact = Values(Density.Compact);
        for (var i = 0; i < regular.Length; i++)
        {
            compact[i].Should().BePositive();
            compact[i].Should().BeLessThan(regular[i], $"metric #{i}");
        }
    }

    [Fact]
    public void For_picks_by_the_flag_and_regular_keeps_the_board_values()
    {
        Density.For(true).Should().Be(Density.Compact);
        Density.For(false).Should().Be(Density.Regular);
        Density.Regular.TableRow.Should().Be(40); // board PulseAllTable rows
        Density.Regular.CardPadY.Should().Be(9);
    }

    [Fact]
    public void The_compact_setting_is_off_by_default_and_reads_as_a_switch()
    {
        var row = SettingsCatalogue.Find("compact");
        row.Key.Should().Be("ui.compact");
        SettingsCatalogue.Resolve(row, null).Should().Be("false");
        SettingsCatalogue.Resolve(row, "true").Should().Be("true");
    }
}
