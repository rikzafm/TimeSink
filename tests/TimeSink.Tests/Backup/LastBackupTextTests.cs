using TimeSink.Core.Backup;

namespace TimeSink.Tests.Backup;

public sealed class LastBackupTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "No backup yet")]
    [InlineData(-600, "Last backup: just now")]
    [InlineData(-86400 * 1 - 10, "Last backup: 1 day ago")]
    [InlineData(-86400 * 3 - 10, "Last backup: 3 days ago")]
    [InlineData(-3 * 3600, "Last backup: today")]
    public void Describes_how_long_ago_the_last_backup_was(long offsetSeconds, string expected)
    {
        var last = offsetSeconds == 0 ? 0 : Now.ToUnixTimeSeconds() + offsetSeconds;
        LastBackupText.Describe(last, Now).Should().Be(expected);
    }
}
