using TimeSink.Core.Tracking;

namespace TimeSink.Core.Ipc;

/// <summary>The rule for when the Agent tells the UI that the running apps changed.</summary>
public static class RunningState
{
    /// <summary>
    /// The state to push for the apps whose session is open, or null when it equals <paramref name="current"/>: same apps,
    /// same session starts. The list is ordered by app id, so the order the store returns them in never counts as a change.
    /// </summary>
    public static StateMessage? Next(StateMessage current, IEnumerable<SessionRecord> open)
    {
        var running = open.Select(s => new RunningApp(s.AppId, s.StartUtc)).OrderBy(r => r.AppId).ToList();
        var old = current.Running;
        if (old.Count == running.Count && old.SequenceEqual(running)) return null;
        return new StateMessage(false, running);
    }
}
