namespace TimeSink.Core.Usage;

/// <summary>
/// A string-table key with its arguments, chosen by Core from a result (glossary: Phrase). An argument is a finished
/// string or number from <see cref="UsageText"/>, or another Phrase the view turns into text first. The view looks the
/// key up in the theme and formats it.
/// </summary>
public sealed record Phrase(string Key, IReadOnlyList<object> Args)
{
    public Phrase(string key, params object[] args) : this(key, (IReadOnlyList<object>)args) { }

    public bool Equals(Phrase? other) => other is not null && Key == other.Key && Args.SequenceEqual(other.Args);

    public override int GetHashCode() => HashCode.Combine(Key, Args.Count);
}

/// <summary>One comparison line of a detail panel: its label, its value and whether it is drawn as a gain.</summary>
public sealed record PanelLine(Phrase Label, Phrase Value, bool Good);
