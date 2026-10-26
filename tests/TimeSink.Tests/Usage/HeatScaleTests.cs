using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class HeatScaleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3599, 0)]
    [InlineData(3600, 1)]
    [InlineData(10799, 1)]
    [InlineData(10800, 2)]
    [InlineData(17999, 2)]
    [InlineData(18000, 3)]
    [InlineData(25199, 3)]
    [InlineData(25200, 4)]
    [InlineData(90000, 4)]
    public void Levels_step_at_1h_3h_5h_7h(long seconds, int level) =>
        HeatScale.Level(seconds).Should().Be(level);
}
