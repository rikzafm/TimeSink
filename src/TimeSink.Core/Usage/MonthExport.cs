using System.Globalization;
using System.Text;

namespace TimeSink.Core.Usage;

/// <summary>CSV export of one month (implementation.md 8.3 footer): <c>date,app,seconds</c>, one row per day and app with time.</summary>
public static class MonthExport
{
    private const string Eol = "\r\n";

    public static string ToCsv(MonthResult month)
    {
        var sb = new StringBuilder("date,app,seconds").Append(Eol);
        foreach (var day in month.Days)
            foreach (var app in day.Apps)
            {
                if (app.Seconds <= 0) continue;
                sb.Append(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Field(app.Name)).Append(',')
                  .Append(app.Seconds.ToString(CultureInfo.InvariantCulture)).Append(Eol);
            }
        return sb.ToString();
    }

    public static void Write(MonthResult month, string path) =>
        File.WriteAllText(path, ToCsv(month), new UTF8Encoding(false));

    public static string FileName(MonthResult month) =>
        string.Create(CultureInfo.InvariantCulture, $"timesink-{month.Year:0000}-{month.Month:00}.csv");

    private static string Field(string v) =>
        v.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
