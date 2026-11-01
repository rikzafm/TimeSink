using FluentAssertions;
using TimeSink.Core.Usage;
using Xunit;

namespace TimeSink.Tests.Usage;

public class AppExportTests
{
    [Fact]
    public void ToCsv_formats_daily_app_seconds()
    {
        var days = new Dictionary<DateOnly, long>
        {
            [new DateOnly(2026, 3, 10)] = 3600,
            [new DateOnly(2026, 3, 11)] = 0,
            [new DateOnly(2026, 3, 12)] = 7200,
        };

        var result = new AppDetailResult(
            1, "Visual Studio, Code", "code.exe", new DateOnly(2026, 3, 10),
            null, 10800, 2, 5400, 7200, 2026, 2026, 2026, 10800, 7200,
            [], false, days);

        var csv = AppExport.ToCsv(result);
        csv.Should().Be(
            "date,app,seconds\r\n" +
            "2026-03-10,\"Visual Studio, Code\",3600\r\n" +
            "2026-03-12,\"Visual Studio, Code\",7200\r\n");
    }

    [Fact]
    public void FileName_sanitizes_app_name()
    {
        var result = new AppDetailResult(
            1, "Rider / IDE *2026*", "rider.exe", null,
            null, 0, 0, 0, 0, 2026, 2026, 2026, 0, 0, []);

        AppExport.FileName(result).Should().Be("timesink-rider-ide-2026.csv");
    }
}

