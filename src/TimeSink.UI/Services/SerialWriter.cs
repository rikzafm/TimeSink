namespace TimeSink.UI.Services;

/// <summary>Runs small background writes (settings) one after another in call order, so the last click is the value that sticks.</summary>
internal static class SerialWriter
{
    private static readonly object Gate = new();
    private static Task _tail = Task.CompletedTask;

    public static void Run(Action write)
    {
        lock (Gate)
            _tail = _tail.ContinueWith(_ =>
            {
                try { write(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); } // a busy database drops this write, as before
            }, TaskScheduler.Default);
    }
}
