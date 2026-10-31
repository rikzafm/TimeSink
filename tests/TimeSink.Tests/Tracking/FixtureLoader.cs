using System.Text.Json;
using System.Text.Json.Serialization;
using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Tracking;

public static class FixtureLoader
{
    public static string FindFixturePath(string filename)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "fixtures", filename);
        if (File.Exists(local)) return local;

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "shared", "fixtures", filename);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Fixture file '{filename}' was not found.");
    }

    public static T Load<T>(string filename)
    {
        var path = FindFixturePath(filename);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Failed to deserialize fixture {filename}");
    }

    public static void RunTrackingCase(TrackingFixtureDocument doc, FixtureCase c)
    {
        var zoneId = c.TimeZoneId ?? doc.TimeZoneId ?? "UTC";
        var zone = zoneId == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var baseTime = DateTimeOffset.Parse(c.BaseTimeUtc ?? doc.BaseTimeUtc ?? "2026-03-10T12:00:00Z");

        using var rig = new Rig(zone, baseTime: baseTime);

        var minSec = c.Settings?.MinSessionSeconds ?? doc.Settings?.MinSessionSeconds ?? 15;
        rig.Settings.MinSessionSeconds = minSec;
        rig.Settings.PauseOnSleep = c.Settings?.PauseOnSleep ?? doc.Settings?.PauseOnSleep ?? true;
        rig.Settings.CountWhenLocked = c.Settings?.CountWhenLocked ?? doc.Settings?.CountWhenLocked ?? true;

        if (c.Events != null)
        {
            foreach (var evt in c.Events)
            {
                switch (evt.Type)
                {
                    case "proc_start":
                        rig.Procs.Running.Add(new ProcInfo(
                            evt.Pid,
                            "notepad.exe",
                            Rig.NotepadPath,
                            rig.AtTime(evt.ProcStartOffset ?? evt.Offset)));
                        break;

                    case "proc_exit":
                        rig.Procs.Running.RemoveAll(p => p.Pid == evt.Pid);
                        break;

                    case "tick":
                        rig.Tick(evt.Offset);
                        break;

                    case "tick_range":
                        var step = evt.Step > 0 ? evt.Step : 5;
                        for (var s = evt.From; s <= evt.To; s += step)
                        {
                            rig.Tick(s);
                        }
                        break;

                    case "clock_set":
                        rig.Clock.UtcNow = rig.AtTime(evt.Offset);
                        break;

                    case "suspend":
                        rig.Power.RaiseSuspend(rig.AtTime(evt.Offset));
                        break;

                    case "resume":
                        rig.Power.RaiseResume(rig.AtTime(evt.Offset));
                        break;

                    case "lock":
                        rig.Power.RaiseLock(rig.AtTime(evt.Offset));
                        break;

                    case "unlock":
                        rig.Power.RaiseUnlock(rig.AtTime(evt.Offset));
                        break;

                    case "shutdown":
                        rig.Power.RaiseShutdown(rig.AtTime(evt.Offset));
                        break;

                    case "crash_and_restart":
                        rig.Clock.UtcNow = rig.AtTime(evt.Offset);
                        rig.NewEngine();
                        break;

                    case "recover_crash":
                        rig.Engine.RecoverFromCrash(rig.AtTime(evt.Offset));
                        break;
                }
            }
        }

        if (c.ExpectedSessions != null)
        {
            var actual = rig.Sessions;
            actual.Count.Should().Be(c.ExpectedSessions.Count, $"Expected {c.ExpectedSessions.Count} session(s) in case '{c.Name}'");

            for (var i = 0; i < c.ExpectedSessions.Count; i++)
            {
                var exp = c.ExpectedSessions[i];
                var act = actual[i];

                if (exp.AppId.HasValue) act.AppId.Should().Be(exp.AppId.Value);
                if (exp.StartUtc.HasValue) act.StartUtc.Should().Be(exp.StartUtc.Value);
                if (exp.EndUtc.HasValue) act.EndUtc.Should().Be(exp.EndUtc.Value);
                if (exp.EndReason != null) act.EndReason.Should().Be(exp.EndReason);
                if (exp.Pid.HasValue) act.Pid.Should().Be(exp.Pid.Value);
                if (exp.ProcStartUtc.HasValue) act.ProcStartUtc.Should().Be(exp.ProcStartUtc.Value);
            }
        }

        if (c.ExpectedDailyUsage != null)
        {
            var daily = rig.Daily();
            daily.Should().Equal(c.ExpectedDailyUsage, $"Daily usage should match expected in case '{c.Name}'");
        }
    }
}

public sealed class TrackingFixtureDocument
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? TimeZoneId { get; set; }
    public string? BaseTimeUtc { get; set; }
    public FixtureSettings? Settings { get; set; }
    public List<FixtureApp> Apps { get; set; } = new();
    public List<FixtureCase> Cases { get; set; } = new();
}

public sealed class FixtureSettings
{
    public int? MinSessionSeconds { get; set; }
    public bool? PauseOnSleep { get; set; }
    public bool? CountWhenLocked { get; set; }
}

public sealed class FixtureApp
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? BundleId { get; set; }
    public long AddedUtc { get; set; }
}

public sealed class FixtureCase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? TimeZoneId { get; set; }
    public string? BaseTimeUtc { get; set; }
    public FixtureSettings? Settings { get; set; }
    public List<FixtureEvent> Events { get; set; } = new();
    public List<FixtureSession>? ExpectedSessions { get; set; }
    public Dictionary<string, long>? ExpectedDailyUsage { get; set; }
}

public sealed class FixtureEvent
{
    public string Type { get; set; } = "";
    public int Offset { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public int Step { get; set; }
    public int Pid { get; set; }
    public long? AppId { get; set; }
    public int? ProcStartOffset { get; set; }
}

public sealed class FixtureSession
{
    public long? AppId { get; set; }
    public long? StartUtc { get; set; }
    public long? EndUtc { get; set; }
    public string? EndReason { get; set; }
    public int? Pid { get; set; }
    public long? ProcStartUtc { get; set; }
}

public sealed class AccumulatorRebuildDocument
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public string BaseTimeUtc { get; set; } = "";
    public List<FixtureApp> Apps { get; set; } = new();
    public List<AccumulatorSession> Sessions { get; set; } = new();
    public List<AccumulatorDailyUsage> ExpectedDailyUsage { get; set; } = new();
}

public sealed class AccumulatorSession
{
    public long Id { get; set; }
    public long AppId { get; set; }
    public long StartUtc { get; set; }
    public long EndUtc { get; set; }
    public string EndReason { get; set; } = "exit";
}

public sealed class AccumulatorDailyUsage
{
    public long AppId { get; set; }
    public string Day { get; set; } = "";
    public long Seconds { get; set; }
}

public sealed class BackupRestoreDocument
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public string BaseTimeUtc { get; set; } = "";
    public List<BackupRestoreCase> Cases { get; set; } = new();
}

public sealed class BackupRestoreCase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<BackupAppInfo>? LiveApps { get; set; }
    public List<BackupSessionInfo>? LiveSessions { get; set; }
    public List<BackupAppInfo>? IncomingApps { get; set; }
    public List<BackupSessionInfo>? IncomingSessions { get; set; }
    public int? ExpectedAppsCount { get; set; }
    public int? ExpectedSessionsCount { get; set; }
    public long? ExpectedTotalDailySeconds { get; set; }
    public BackupMergeResultDto? FirstMergeResult { get; set; }
    public BackupMergeResultDto? SecondMergeResult { get; set; }
    public long? ExpectedFinalTotalDailySeconds { get; set; }
    public int? IncomingSchemaVersion { get; set; }
    public string? ExpectedError { get; set; }
}

public sealed class BackupAppInfo
{
    public string ExePath { get; set; } = "";
    public string? Category { get; set; }
}

public sealed class BackupSessionInfo
{
    public string ExePath { get; set; } = "";
    public long StartUtc { get; set; }
    public long EndUtc { get; set; }
}

public sealed class BackupMergeResultDto
{
    public int AppsAdded { get; set; }
    public int SessionsAdded { get; set; }
    public int SessionsSkipped { get; set; }
}
