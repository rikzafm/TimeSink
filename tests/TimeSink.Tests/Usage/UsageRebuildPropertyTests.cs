using TimeSink.Tests.Tracking;

namespace TimeSink.Tests.Usage;

public sealed class UsageRebuildPropertyTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Incremental_accumulation_equals_a_full_rebuild_on_randomized_activity(int seed)
    {
        var rng = new Random(seed);
        var zone = seed % 2 == 0 ? TimeZoneInfo.FindSystemTimeZoneById("America/New_York") : TimeZoneInfo.Utc;
        using var r = new Rig(zone);
        var chrome = r.Db.InsertApp(@"c:\apps\chrome.exe", "Chrome", 0);
        int[] mins = { 5, 15, 30 };
        var pids = 100;
        var t = -3600;                                 // start an hour before Base
        var suspended = false;

        // ~4 days of 5 s ticks with random process churn, sleeps, locks and clock jumps.
        for (var i = 0; i < 70_000; i++)
        {
            t += 5;
            var roll = rng.NextDouble();
            if (roll < 0.004) ToggleProc(r, rng, ref pids, t, "notepad.exe", Rig.NotepadPath);
            else if (roll < 0.008) ToggleProc(r, rng, ref pids, t, "chrome.exe", @"c:\apps\chrome.exe");
            else if (roll < 0.0085) r.Settings.MinSessionSeconds = mins[rng.Next(3)];
            else if (roll < 0.0090) r.Settings.CountWhenLocked = !r.Settings.CountWhenLocked;
            else if (roll < 0.0095 && !suspended) { r.Power.RaiseSuspend(Rig.At(t)); suspended = true; }
            else if (roll < 0.0105 && suspended) { t += rng.Next(60, 20_000); r.Clock.UtcNow = Rig.At(t); r.Power.RaiseResume(Rig.At(t)); suspended = false; }
            else if (roll < 0.0110) r.Power.RaiseLock(Rig.At(t));
            else if (roll < 0.0115) r.Power.RaiseUnlock(Rig.At(t));
            else if (roll < 0.0118) t -= rng.Next(1, 900);          // clock set back
            else if (roll < 0.0121) t += rng.Next(21, 5000);        // missed sleep
            r.Clock.UtcNow = Rig.At(t);
            if (!suspended) r.Engine.OnTick(Rig.At(t));
        }

        var incremental = r.Store.GetDailyUsage();
        r.Sessions.Count.Should().BeGreaterThan(10, "the scenario must actually produce sessions");
        r.Usage.RebuildAll();
        r.Store.GetDailyUsage().Should().Equal(incremental);
        incremental.Should().OnlyContain(x => x.Seconds > 0);
        _ = chrome;
    }

    private static void ToggleProc(Rig r, Random rng, ref int pids, int t, string name, string path)
    {
        var existing = r.Procs.Running.Where(p => p.Name == name).ToList();
        if (existing.Count > 0 && rng.Next(3) != 0) r.Procs.Running.Remove(existing[rng.Next(existing.Count)]);
        else r.Procs.Running.Add(new Core.Tracking.ProcInfo(pids++, name, path, Rig.At(t)));
    }
}
