using System.Text;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

public sealed class MonthExportTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private MonthResult March() => Q.Month(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 3, DayOfWeek.Monday);

    [Fact]
    public void Csv_has_one_row_per_day_and_app_with_RFC4180_quoting()
    {
        var expected = string.Join("\r\n",
            "date,app,seconds",
            "2026-03-01,Code,1800",
            "2026-03-02,Chrome,10800",
            "2026-03-02,Code,7200",
            "2026-03-02,Disc,3600",
            "2026-03-03,Code,28800",
            "2026-03-03,Chrome,7200",
            "2026-03-04,Chrome,7200",
            "2026-03-05,\"Zed, \"\"Editor\"\"\",1800",
            "2026-03-10,Code,18000",
            "2026-03-10,Disc,5400",
            "2026-03-11,Chrome,12000",
            "2026-03-11,Mus,600") + "\r\n";
        MonthExport.ToCsv(March()).Should().Be(expected);
    }

    [Fact]
    public void Csv_rows_add_up_to_the_month_total()
    {
        var lines = MonthExport.ToCsv(March()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1);
        lines.Sum(l => long.Parse(l[(l.LastIndexOf(',') + 1)..])).Should().Be(March().Days.Sum(d => d.Apps.Sum(a => a.Seconds))); // app seconds, not uptime
    }

    [Fact]
    public void Write_creates_a_utf8_file_and_suggests_a_month_file_name()
    {
        var path = Path.Combine(Path.GetTempPath(), "timesink-month-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            MonthExport.Write(March(), path);
            File.ReadAllText(path, Encoding.UTF8).Should().Be(MonthExport.ToCsv(March()));
            MonthExport.FileName(March()).Should().Be("timesink-2026-03.csv");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_empty_month_exports_just_the_header()
    {
        var r = Q.Month(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 4, DayOfWeek.Monday);
        MonthExport.ToCsv(r).Should().Be("date,app,seconds\r\n");
    }
}
