namespace TimeSink.Core.Usage;

public enum AllColumn { App, Hours, DaysUsed, AvgPerDay, Share, AvgSession, LastOpened }

/// <summary>
/// The table's sort state. Tapping a column sorts by it (App A-Z, numbers high to low, last opened newest first);
/// tapping the active column flips the direction. <see cref="Next"/> steps through the columns the All tab offers.
/// Default is hours, high to low.
/// </summary>
public readonly record struct AllSort(AllColumn Column, bool Descending)
{
    public static AllSort Default => new(AllColumn.Hours, true);

    private static readonly AllColumn[] Cycle = [AllColumn.Hours, AllColumn.App, AllColumn.AvgSession, AllColumn.LastOpened];

    /// <summary>The next column in Hours, App, Avg session, Last opened order (wrapping), in that column's natural direction.</summary>
    public AllSort Next()
    {
        var i = Array.IndexOf(Cycle, Column);
        var column = Cycle[(i + 1) % Cycle.Length]; // not in the cycle (-1) starts at Hours
        return new AllSort(column, Descending: column != AllColumn.App);
    }

    public AllSort Tap(AllColumn column) =>
        column == Column ? this with { Descending = !Descending } : new AllSort(column, Descending: column != AllColumn.App);
}

public static class AllSorting
{
    /// <summary>
    /// Sorts the tracked rows; ties fall back to the app name (A-Z) so the order is stable. The default sort groups
    /// apps that have time first, then Background apps with time, then apps that never ran; a header sort is purely by its column.
    /// </summary>
    public static IReadOnlyList<AllAppRow> Apply(IEnumerable<AllAppRow> rows, AllSort sort)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        if (sort == AllSort.Default)
            return rows.OrderBy(r => r.Seconds <= 0 ? 2 : r.Background ? 1 : 0)
                .ThenByDescending(r => r.Seconds).ThenBy(r => r.Name, byName).ToList();
        IOrderedEnumerable<AllAppRow> ordered = sort.Column switch
        {
            AllColumn.App => Order(rows, r => r.Name, byName, sort.Descending),
            AllColumn.Hours => Order(rows, r => r.Seconds, Comparer<long>.Default, sort.Descending),
            AllColumn.DaysUsed => Order(rows, r => r.DaysUsed, Comparer<int>.Default, sort.Descending),
            AllColumn.AvgPerDay => Order(rows, r => r.AverageSecondsPerDay, Comparer<double>.Default, sort.Descending),
            AllColumn.AvgSession => Order(rows, r => r.AverageSessionSeconds, Comparer<double>.Default, sort.Descending),
            AllColumn.LastOpened => Order(rows, r => r.LastUsedUtc, Comparer<long>.Default, sort.Descending),
            _ => Order(rows, r => r.Share, Comparer<double>.Default, sort.Descending),
        };
        return ordered.ThenBy(r => r.Name, byName).ToList();
    }

    private static IOrderedEnumerable<AllAppRow> Order<TKey>(IEnumerable<AllAppRow> rows, Func<AllAppRow, TKey> key, IComparer<TKey> cmp, bool desc) =>
        desc ? rows.OrderByDescending(key, cmp) : rows.OrderBy(key, cmp);
}
