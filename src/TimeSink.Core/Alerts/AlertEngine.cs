using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;
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
/// A NULL app id means the combined time of all tracked apps. Merged apps count toward their canonical app, and
/// the time they are open together counts once (<see cref="UsageDays.Runs"/>), as in the views.</item>
/// <item>The weekly summary recaps the last full week once per week, from 08:00 on the first day of the week (or at the
/// first check after that, e.g. the PC was off), and never when the week has no tracked time.</item>
/// <item>Once-per-period dedupe is persisted in the <c>settings</c> table (<c>alerts.sent</c>, <c>alerts.weeklyLast</c>),
/// so a restart does not repeat an alert.</item>
/// </list>
/// </summary>
public sealed class AlertEngine(Db db, TimeZoneInfo zone)
{
    public const int WeeklyHour = 8;

    private readonly AppSettings _settings = AppSettings.ForDb(db);

    public IReadOnlyList<AlertNotice> Check(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var notices = new List<AlertNotice>();
        var nowUtc = now.ToUnixTimeSeconds();
        if (_settings.Get(Setting.GoalAlerts)) CheckGoals(today, nowUtc, notices);
        if (_settings.Get(Setting.WeeklySummary)) CheckWeekly(today, local.TimeOfDay, nowUtc, notices);
        return notices;
    }

    // ---- goals and limits ---------------------------------------------------------------------------------

    private sealed record Goal(long? AppId, string Kind, long Seconds);

    private void CheckGoals(DateOnly today, long nowUtc, List<AlertNotice> notices)
    {
        var goals = LoadGoals();
        if (goals.Count == 0) return;

        var day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var groups = AppGroups.Load(db.Connection);
        var usage = LoadUsage(groups, today, today.AddDays(1), nowUtc).GetValueOrDefault(today) ?? [];
        var total = usage.Values.Sum();
        var names = groups.Values.ToDictionary(a => a.Id, a => a.Name);
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
        if (changed) _settings.Set(Setting.AlertsSent, day + "|" + string.Join("|", sent));
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

    /// <summary>
    /// Tracked-app seconds per canonical app on each local day in [<paramref name="from"/>, <paramref name="to"/>), read
    /// from the sessions like the views (decision 56) and not from <c>daily_usage</c>, which is per app and so cannot
    /// tell that two Merged apps were open at the same time. Retired and unknown apps are left out; an open session
    /// counts to now.
    /// </summary>
    private Dictionary<DateOnly, Dictionary<long, long>> LoadUsage(AppGroups groups, DateOnly from, DateOnly to, long nowUtc)
    {
        var start = LocalDays.StartOf(from, zone);
        var end = LocalDays.StartOf(to, zone);
        var segs = UsageLoader.LoadSegments(db.Connection, start, nowUtc, groups, running: null, to: end)
            .Select(s => s with { Start = Math.Max(s.Start, start), End = Math.Min(s.End, end) });
        return UsageDays.PerDayGroup(segs, zone);
    }

    private HashSet<string> LoadSent(string day)
    {
        var parts = (_settings.Get(Setting.AlertsSent) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts[0] == day ? new HashSet<string>(parts.Skip(1)) : [];
    }

    // ---- weekly summary -----------------------------------------------------------------------------------

    private void CheckWeekly(DateOnly today, TimeSpan timeOfDay, long nowUtc, List<AlertNotice> notices)
    {
        var startDay = _settings.Get(Setting.WeekStartsOn).ToDayOfWeek();
        var thisWeek = WeekStarts.Containing(today, startDay);
        if (today == thisWeek && timeOfDay < TimeSpan.FromHours(WeeklyHour)) return; // wait for the morning

        var marker = thisWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_settings.Get(Setting.AlertsWeeklyLast) == marker) return;
        _settings.Set(Setting.AlertsWeeklyLast, marker); // decided for this week whether or not there is anything to say

        var from = thisWeek.AddDays(-7);
        var perApp = new Dictionary<long, long>();
        var perDay = new Dictionary<DateOnly, long>();
        var groups = AppGroups.Load(db.Connection);
        foreach (var (day, amounts) in LoadUsage(groups, from, thisWeek, nowUtc))
        {
            foreach (var (group, seconds) in amounts) perApp[group] = perApp.GetValueOrDefault(group) + seconds;
            perDay[day] = amounts.Values.Sum();
        }
        var total = perApp.Values.Sum();
        if (total <= 0) return;

        var names = groups.Values.ToDictionary(a => a.Id, a => a.Name);
        var top = perApp.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First();
        var best = perDay.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First();
        var bestDay = best.Key.DayOfWeek;
        var apps = perApp.Count;
        notices.Add(new AlertNotice(AlertKind.Weekly, "Your week in TimeSink",
            $"Last week: {UsageText.HoursMinutes(total)} across {apps} {(apps == 1 ? "app" : "apps")}. " +
            $"Most used: {names.GetValueOrDefault(top.Key, "?")} ({UsageText.HoursMinutes(top.Value)}). " +
            $"Busiest day: {bestDay} ({UsageText.HoursMinutes(best.Value)})."));
    }
}
