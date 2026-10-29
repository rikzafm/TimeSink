namespace TimeSink.Core.Apps;

/// <summary>The Tracked tab's search box.</summary>
public static class TrackedSearch
{
    /// <summary>
    /// Fuzzy match on an app's name, the names of the apps merged into it, and its publisher (<paramref name="publisherOf"/>
    /// by exe path, null when unknown), best match first and equal matches by name; a blank query returns every app in list order.
    /// </summary>
    public static IReadOnlyList<TrackedEntry> Apply(IEnumerable<TrackedEntry> apps, string? query, Func<string, string?> publisherOf)
    {
        if (string.IsNullOrWhiteSpace(query)) return apps.ToList();
        return apps
            .Select(a => (App: a, Score: FuzzySearch.Score(query, a.Merged.Select(m => m.DisplayName).Prepend(a.DisplayName), publisherOf(a.ExePath))))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score).ThenBy(x => x.App.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.App).ToList();
    }
}
