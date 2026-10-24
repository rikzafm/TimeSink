using TimeSink.Core.Data;
using TimeSink.Core.Settings;

namespace TimeSink.Tests.Settings;

public class TrackingSummaryTests
{
    [Fact]
    public void Summary_counts_tracked_apps_and_finds_the_first_added_date()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = Db.Open(path))
            {
                var t1 = new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                var t2 = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                db.InsertApp(@"c:\a.exe", "A", t2);
                var b = db.InsertApp(@"c:\b.exe", "B", t1);
                var c = db.InsertApp(@"c:\c.exe", "C", t2);
                db.SetAppState(c, "retired", t2);
            }
            var s = TrackingSummary.Read(path);
            s.AppCount.Should().Be(2);
            s.FirstAddedUtc.Should().Be(new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero));
            s.Describe().Should().Be("Since 12 Mar 2026 · 2 apps tracked");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_database_reads_as_nothing_tracked()
        => TrackingSummary.Read(Path.Combine(Path.GetTempPath(), "ts-nope.db")).Describe().Should().Be("Nothing tracked yet");
}
