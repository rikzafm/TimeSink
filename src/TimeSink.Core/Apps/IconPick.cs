namespace TimeSink.Core.Apps;

/// <summary>
/// The state of the icon picker (decision 153) for one app: the icons found for it plus any files the user picked, which of
/// them the app uses now, and which one is selected. Choices that draw the same picture are listed once.
/// </summary>
public sealed class IconPick
{
    private readonly List<IconChoice> _choices;
    private readonly byte[]? _current;

    /// <param name="found">The icons <see cref="IconChoices.Find"/> returned.</param>
    /// <param name="current">The icon the app uses now; the choice that draws it is "in use" and starts selected.</param>
    public IconPick(IReadOnlyList<IconChoice> found, byte[]? current)
    {
        _choices = [.. found];
        _current = current;
        Selected = Math.Max(InUse, 0);
    }

    /// <summary>The picker is only worth opening when there is more than one icon to choose from.</summary>
    public static bool Offered(IReadOnlyList<IconChoice> found) => found.Count > 1;

    /// <summary>The found icons, then the picked files in the order they were added.</summary>
    public IReadOnlyList<IconChoice> Choices => _choices;

    /// <summary>Index of the choice that draws the app's current icon, or -1 when none does.</summary>
    public int InUse => _current is null ? -1 : _choices.FindIndex(c => Same(c.Png, _current));

    public int Selected { get; private set; }

    /// <summary>True when the selection is not the icon in use, so Apply has something to store.</summary>
    public bool Changed => Selected != InUse;

    public byte[] SelectedPng => _choices[Selected].Png;

    /// <summary>Previews a choice; an index outside the list does nothing.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _choices.Count) Selected = index;
    }

    /// <summary>
    /// Offers a file the user picked and selects it. A file that draws the same picture as a listed choice (found or
    /// picked earlier) selects that choice instead of adding a second tile.
    /// </summary>
    public void Add(IconChoice picked)
    {
        var at = _choices.FindIndex(c => Same(c.Png, picked.Png));
        if (at < 0) { _choices.Add(picked); at = _choices.Count - 1; }
        Selected = at;
    }

    private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
}
