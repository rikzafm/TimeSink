using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public sealed class AppPaletteTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void The_slot_is_the_id_modulo_the_colour_count_and_stays_in_range(int count)
    {
        for (long id = 1; id <= 40; id++)
        {
            var slot = AppPalette.Slot(id, count);
            slot.Should().Be((int)(id % count)).And.BeInRange(0, count - 1);
            AppPalette.Slot(id, count).Should().Be(slot);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_missing_palette_still_gives_slot_zero(int count) => AppPalette.Slot(17, count).Should().Be(0);

    [Fact]
    public void A_negative_key_wraps_into_range() => AppPalette.Slot(-1, 6).Should().Be(5);

    [Fact]
    public void The_exe_path_hash_of_an_untracked_app_uses_the_same_function() =>
        TimeSink.Core.Apps.LetterTile.PaletteIndex(-7, 6).Should().Be(AppPalette.Slot(-7, 6));
}
