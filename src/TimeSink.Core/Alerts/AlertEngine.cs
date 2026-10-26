using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;

namespace TimeSink.Core.Alerts;

public enum AlertKind { Limit, Goal, Weekly }

/// <summary>A notification the Agent should show (notify only: nothing is ever blocked, story 41).</summary>
public sealed record AlertNotice(AlertKind Kind, string Title, string Body);

/// <summary>
/// Decides which goal/limit alerts and weekly summaries are due (story 41, decision 116). Pure logic over the database
/// and a supplied "now" (tests use a fake clock); the Agent only shows what <see cref="Check"/> returns.
/// <list type="bullet">
/// <item>Goal/limit rows come from the <c>goals</c> table: <c>limit</c> and <c>target</c> are both "daily seconds"; a limit
/// notifies once per local day when the day's time reaches it, a target once per day when it is reached.
/// A NULL app id means the combined time of all tracked apps. Merged apps count toward their canonical app.</item>
/// <item>The weekly summary recaps the last full week once per week, from 08:00 on the first day of the week (or at the
/// first check after that, e.g. the PC was off), and never when the week has no tracked time.</item>
/// <item>Once-per-period dedupe is persisted in the <c>settings</c> table (<c>alerts.sent</c>, <c>alerts.weeklyLast</c>),
/// so a restart does not repeat an alert.</item>
/// </list>
/// </summary>
public sealed class AlertEngine(Db db, TimeZoneInfo zone)
{
    public const string GoalsKey = "alerts.goals", WeeklyKey = "alerts.weekly";
    public const string SentKey = "alerts.sent", WeeklyLastKey = "alerts.weeklyLast";
    public const int WeeklyHour = 8;

    public IReadOnlyList<AlertNotice> Check(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var notices = new List<AlertNotice>();
        if (db.GetSetting(GoalsKey) != "false") CheckGoals(today, notices);
        if (db.GetSetting(WeeklyKey) == "true") CheckWeekly(today, local.TimeOfDay, notices);
        return notices;
    }

    // ---- goals and limits ---------------------------------------------------------------------------------

    private sealed record Goal(long? AppId, string Kind, long Seconds);

    private void CheckGoals(DateOnly today, List<AlertNotice> notices)
    {
        var goals = LoadGoals();
        if (goals.Count == 0) return;

        var day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var usage = LoadDay(day, out var total, out var names);
        var sent = LoadSent(day);
        var changed = false;

        foreach (var g in goals)
        {
            long seconds;
            string name;
            if (g.AppId is null) { seconds = total; name = "All apps"; }
            else if (names.TryGetValue(g.AppId.Value, out var n)) { seconds = usage.GetValueOrDefault(g.AppId.Value); name = n; }
            else continue; // retired, merged-away or unknown app: nothing to watch

            if (seconds < g.Seconds) continue;
            var token = (g.Kind == "limit" ? "L" : "T") + ":" + (g.AppId ?? 0);
            if (!sent.Add(token)) continue;
            changed = true;
            notices.Add(g.Kind == "limit"
                ? new AlertNotice(AlertKind.Limit, "Daily limit reached",
                    $"{name}: {UsageText.HoursMinutes(seconds)} today (limit {UsageText.HoursMinutes(g.Seconds)}).")
                : new AlertNotice(AlertKind.Goal, "Goal reached",
                    $"{name}: {UsageText.HoursMinutes(seconds)} today (goal {UsageText.HoursMinutes(g.Seconds)})."));
        }
        if (changed) db.SetSetting(SentKey, day + "|" + string.Join("|", sent));
    }

    private List<Goal> LoadGoals()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT app_id, kind, seconds_per_day FROM goals WHERE seconds_per_day > 0 ORDER BY app_id, kind";
        using var r = cmd.ExecuteReader();
        var list = new List<Goal>();
        while (r.Read()) list.Add(new Goal(r.IsDBNull(0) ? null : r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    /// <summary>Tracked-app seconds on a local day, per canonical app; also the combined total and each app's name.</summary>
    private Dictionary<long, long> LoadDay(string day, out long total, out Dictionary<long, string> names)
    {
        var usage = new Dictionary<long, long>();
        names = [];
        total = 0;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(a.canonical_id, a.id), SUM(d.seconds)
                FROM daily_usage d JOIN apps a ON a.id = d.app_id
                WHERE d.day = $day AND a.state = 'tracked' GROUP BY 1
                """;
            cmd.Parameters.AddWithValue("$day", day);
            using var r = cmd.ExecuteReader();
            while (r.Read()) { usage[r.GetInt64(0)] = r.GetInt64(1); total += r.GetInt64(1); }
        }
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, display_name FROM apps WHERE state = 'tracked'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names[r.GetInt64(0)] = r.GetString(1);
        }
        return usage;
    }

    private HashSet<string> LoadSent(string day)
    {
        var parts = (db.GetSetting(SentKey) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts[0] == day ? new HashSet<string>(parts.Skip(1)) : [];
    }

    // ---- weekly summary -----------------------------------------------------------------------------------

    private void CheckWeekly(DateOnly today, TimeSpan timeOfDay, List<AlertNotice> notices)
    {
        var startDay = WeekStarts.Parse(db.GetSetting(WeekStarts.SettingKey)).ToDayOfWeek();
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek - (int)startDay + 7) % 7));
        if (today == thisWeek && timeOfDay < TimeSpan.FromHours(WeeklyHour)) return; // wait for the morning

        var marker = thisWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (db.GetSetting(WeeklyLastKey) == marker) return;
        db.SetSetting(WeeklyLastKey, marker); // decided for this week whether or not there is anything to say

        var from = thisWeek.AddDays(-7);
        var perApp = new Dictionary<long, long>();
        var perDay = new Dictionary<string, long>();
        var names = new Dictionary<long, string>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(a.canonical_id, a.id), d.day, d.seconds
                FROM daily_usage d JOIN apps a ON a.id = d.app_id
                WHERE a.state = 'tracked' AND d.day >= $from AND d.day < $to
                """;
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$to", marker);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                perApp[r.GetInt64(0)] = perApp.GetValueOrDefault(r.GetInt64(0)) + r.GetInt64(2);
                perDay[r.GetString(1)] = perDay.GetValueOrDefault(r.GetString(1)) + r.GetInt64(2);
            }
        }
        var total = perApp.Values.Sum();
        if (total <= 0) return;

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, display_name FROM apps";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names[r.GetInt64(0)] = r.GetString(1);
        }
        var top = perApp.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First();
        var best = perDay.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First();
        var bestDay = DateOnly.ParseExact(best.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek;
        var apps = perApp.Count;
        notices.Add(new AlertNotice(AlertKind.Weekly, "Your week in TimeSink",
            $"Last week: {UsageText.HoursMinutes(total)} across {apps} {(apps == 1 ? "app" : "apps")}. " +
            $"Most used: {names.GetValueOrDefault(top.Key, "?")} ({UsageText.HoursMinutes(top.Value)}). " +
            $"Busiest day: {bestDay} ({UsageText.HoursMinutes(best.Value)})."));
    }
}
