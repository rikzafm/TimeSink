using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 158: a change against a baseline has one unit (percent) and no value without a baseline.</summary>
public sealed class ChangeTests
{
    [Theory]
    [InlineData(150.0, 100.0, 50.0)]
    [InlineData(50.0, 100.0, -50.0)]
    [InlineData(100.0, 100.0, 0.0)]
    [InlineData(0.0, 100.0, -100.0)]
    [InlineData(32400.0 + 134400.0, 32400.0, 414.8148148148148)]
    public void The_change_is_the_percentage_from_the_baseline(double current, double baseline, double percent) =>
        Change.Between(current, baseline)!.Value.Percent.Should().BeApproximately(percent, 1e-9);

    [Theory]
    [InlineData(10.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(10.0, -5.0)]
    [InlineData(10.0, double.NaN)]
    public void Without_a_baseline_above_zero_there_is_no_change_not_infinity(double current, double baseline) =>
        Change.Between(current, baseline).Should().BeNull();

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(25.0, true)]
    [InlineData(-0.3, false)]   // judged before rounding: the pill is a drop even though its text reads 0%
    public void A_gain_is_judged_on_the_raw_percentage(double percent, bool gain) =>
        Change.Between(100 + percent, 100)!.Value.IsGain.Should().Be(gain);
}
