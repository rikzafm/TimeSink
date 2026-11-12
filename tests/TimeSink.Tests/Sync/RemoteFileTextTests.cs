using TimeSink.Core.Sync;

namespace TimeSink.Tests.Sync;

public sealed class RemoteFileTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static long Ago(long seconds) => Now.ToUnixTimeSeconds() - seconds;

    [Fact]
    public void Fresh_file_is_not_stale()
    {
        var t = RemoteFileText.Describe(Ago(20), Now);
        t.Line.Should().Be("File from just now");
        t.StaleHint.Should().BeNull();
    }

    [Fact]
    public void Minutes_old_file_reads_in_minutes()
    {
        RemoteFileText.Describe(Ago(5 * 60), Now).Line.Should().Be("File from 5 min ago");
    }

    [Fact]
    public void Hours_old_file_reads_in_hours_and_is_not_stale()
    {
        var t = RemoteFileText.Describe(Ago(5 * 3600), Now);
        t.Line.Should().Be("File from 5 h ago");
        t.StaleHint.Should().BeNull();
    }

    [Fact]
    public void Just_under_24_hours_is_not_stale()
    {
        RemoteFileText.Describe(Ago(24 * 3600 - 1), Now).StaleHint.Should().BeNull();
    }

    [Fact]
    public void Days_old_file_reads_in_days_and_carries_a_neutral_hint()
    {
        var t = RemoteFileText.Describe(Ago(2 * 86400 + 600), Now);
        t.Line.Should().Be("File from 2 days ago");
        t.StaleHint.Should().StartWith("Other device hasn't synced since ");
    }

    [Fact]
    public void One_day_is_singular()
    {
        RemoteFileText.Describe(Ago(86400 + 5), Now).Line.Should().Be("File from 1 day ago");
    }

    [Fact]
    public void Never_read_has_no_hint()
    {
        var t = RemoteFileText.Describe(0, Now);
        t.Line.Should().Be("File not read yet");
        t.StaleHint.Should().BeNull();
    }
}
