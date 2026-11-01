using System.Globalization;
using System.Text;

namespace TimeSink.Core.Usage;

/// <summary>CSV export of an app's daily usage: <c>date,app,seconds</c>, one row per day with tracked time.</summary>
public static class AppExport
{
    private const string Eol = "\r\n";

    public static string ToCsv(AppDetailResult app)
    {
        var sb = new StringBuilder("date,app,seconds").Append(Eol);
        if (app.DaySeconds is not null)
        {
            foreach (var (date, seconds) in app.DaySeconds.OrderBy(kv => kv.Key))
            {
                if (seconds <= 0) continue;
                sb.Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Field(app.Name)).Append(',')
                  .Append(seconds.ToString(CultureInfo.InvariantCulture)).Append(Eol);
            }
        }
        else
        {
            foreach (var day in app.Days)
            {
                if (day.Seconds <= 0) continue;
                sb.Append(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Field(app.Name)).Append(',')
                  .Append(day.Seconds.ToString(CultureInfo.InvariantCulture)).Append(Eol);
            }
        }
        return sb.ToString();
    }

    public static void Write(AppDetailResult app, string path) =>
        File.WriteAllText(path, ToCsv(app), new UTF8Encoding(false));

    public static string FileName(AppDetailResult app)
    {
        var safeName = SanitizeFileName(app.Name);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "app";
        return $"timesink-{safeName}.csv";
    }

    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder();
        var prevDash = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }
        var s = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? "app" : s;
    }

    private static string Field(string v) =>
        v.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
