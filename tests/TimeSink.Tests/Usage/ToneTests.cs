using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 164: how a change is drawn is Core's, so no view decides gain or loss from text.</summary>
public sealed class ToneTests
{
    [Theory]
    [InlineData(25.0, Tone.Gain)]
    [InlineData(0.0, Tone.Gain)]
    [InlineData(-0.3, Tone.Loss)]   // reads "0%", but is below its baseline: a Loss
    [InlineData(-50.0, Tone.Loss)]
    public void A_change_is_a_gain_at_or_above_its_baseline(double percent, Tone tone) =>
        Tones.Of(Change.Between(100 + percent, 100)).Should().Be(tone);

    [Fact]
    public void No_change_to_compare_with_is_muted() => Tones.Of(null).Should().Be(Tone.Muted);

    [Fact]
    public void The_text_of_a_small_drop_does_not_decide_the_tone()
    {
        var change = Change.Between(99.7, 100)!.Value;
        UsageText.Delta(change).Should().Be("0%");
        Tones.Of(change).Should().Be(Tone.Loss);
    }
}
