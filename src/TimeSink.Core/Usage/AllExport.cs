using System.Globalization;
using System.Text;

namespace TimeSink.Core.Usage;

/// <summary>
/// CSV export of the All table (implementation.md 8.3 footer): <c>app,status,seconds,days_used,avg_seconds_per_day,share_percent</c>.
/// Tracked rows in the table's current sort order, then retired rows (no share), then the tracked total.
/// RFC 4180: CRLF, fields with commas, quotes or line breaks quoted; UTF-8 without BOM. The total row is labelled
/// "Total uptime" with status <c>total</c>.
/// </summary>
public static class AllExport
{
    private const string Eol = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string FileName = "timesink-all-time.csv";

    public static string ToCsv(AllResult result, AllSort sort)
    {
        var sb = new StringBuilder("app,status,seconds,days_used,avg_seconds_per_day,share_percent").Append(Eol);
        foreach (var a in AllSorting.Apply(result.Apps, sort))
            Row(sb, a.Name, "tracked", a.Seconds, a.DaysUsed, a.AverageSecondsPerDay, (a.Share * 100).ToString("0.0", Inv));
        foreach (var r in result.Retired)
            Row(sb, r.Name, "retired", r.Seconds, r.DaysUsed, r.AverageSecondsPerDay, "");
        var t = result.Totals;
        Row(sb, "Total uptime", "total", t.Seconds, t.DaysUsed, t.AverageSecondsPerDay, "100.0");
        return sb.ToString();
    }

    public static void Write(AllResult result, AllSort sort, string path) =>
        File.WriteAllText(path, ToCsv(result, sort), new UTF8Encoding(false));

    private static void Row(StringBuilder sb, string name, string status, long seconds, int days, double avg, string share) =>
        sb.Append(Field(name)).Append(',').Append(status).Append(',')
          .Append(seconds.ToString(Inv)).Append(',').Append(days.ToString(Inv)).Append(',')
          .Append(((long)Math.Round(avg, MidpointRounding.AwayFromZero)).ToString(Inv)).Append(',')
          .Append(share).Append(Eol);

    private static string Field(string v) =>
        v.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
