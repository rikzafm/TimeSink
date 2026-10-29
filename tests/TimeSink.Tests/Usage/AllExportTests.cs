using System.Text;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

public sealed class AllExportTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private AllResult Result() => Q.All(_f.DbPath, Now, TimeZoneInfo.Utc, null);

    [Fact]
    public void Csv_follows_the_table_with_RFC4180_quoting_retired_rows_and_a_total()
    {
        var expected = string.Join("\r\n",
            "app,status,seconds,days_used,avg_seconds_per_day,share_percent",
            "Chrome,tracked,98400,8,12300,54.7",
            "Code,tracked,63000,5,12600,35.0",
            "Disc,tracked,16200,3,5400,9.0",
            "\"Zed, \"\"Editor\"\"\",tracked,1800,1,1800,1.0",
            "Mus,tracked,600,1,600,0.3",
            "Old,retired,82800,1,82800,",
            "Total uptime,total,242400,13,18646,100.0") + "\r\n";
        AllExport.ToCsv(Result(), AllSort.Default).Should().Be(expected);
    }

    [Fact]
    public void Csv_rows_are_in_the_current_sort_order()
    {
        var lines = AllExport.ToCsv(Result(), new AllSort(AllColumn.App, Descending: false)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Select(l => l.Split(',')[0]).Should().Equal("app", "Chrome", "Code", "Disc", "Mus", "\"Zed", "Old", "Total uptime");
    }

    [Fact]
    public void Nothing_tracked_exports_zero_rows_without_dividing_by_zero()
    {
        _f.Exec("DELETE FROM sessions");
        var csv = AllExport.ToCsv(Result(), AllSort.Default);
        csv.Should().Contain("Chrome,tracked,0,0,0,0.0\r\n").And.EndWith("Total uptime,total,0,0,0,100.0\r\n");
    }

    [Fact]
    public void Write_creates_a_utf8_file_without_bom_and_suggests_a_file_name()
    {
        var path = Path.Combine(Path.GetTempPath(), "timesink-all-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            AllExport.Write(Result(), AllSort.Default, path);
            var bytes = File.ReadAllBytes(path);
            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
            Encoding.UTF8.GetString(bytes).Should().Be(AllExport.ToCsv(Result(), AllSort.Default));
            AllExport.FileName.Should().Be("timesink-all-time.csv");
        }
        finally { File.Delete(path); }
    }
}
