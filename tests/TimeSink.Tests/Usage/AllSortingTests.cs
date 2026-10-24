using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

/// <summary>Order of the fixture rows: hours Chrome 98400, Code 63000, Disc 16200, Zed 1800, Mus 600; days 8, 5, 3, 1, 1; avg 12300, 12600, 5400, 1800, 600.</summary>
public sealed class AllSortingTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private AllResult Result() => AllQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, null);

    private static string[] Names(IEnumerable<AllAppRow> rows) => rows.Select(r => r.Name).ToArray();

    [Fact]
    public void The_default_order_is_hours_high_to_low()
    {
        AllSort.Default.Should().Be(new AllSort(AllColumn.Hours, Descending: true));
        Names(AllSorting.Apply(Result().Apps, AllSort.Default)).Should().Equal("Chrome", "Code", "Disc", ZedName, "Mus");
    }

    [Fact]
    public void Tapping_a_new_numeric_column_sorts_high_to_low_and_tapping_again_flips()
    {
        var s = AllSort.Default.Tap(AllColumn.AvgPerDay);
        s.Should().Be(new AllSort(AllColumn.AvgPerDay, true));
        Names(AllSorting.Apply(Result().Apps, s)).Should().Equal("Code", "Chrome", "Disc", ZedName, "Mus");

        s = s.Tap(AllColumn.AvgPerDay);
        s.Should().Be(new AllSort(AllColumn.AvgPerDay, false));
        Names(AllSorting.Apply(Result().Apps, s)).Should().Equal("Mus", ZedName, "Disc", "Chrome", "Code");
    }

    [Fact]
    public void Tapping_the_app_column_sorts_a_to_z_first_and_ignores_case()
    {
        var s = AllSort.Default.Tap(AllColumn.App);
        s.Should().Be(new AllSort(AllColumn.App, false));
        Names(AllSorting.Apply(Result().Apps, s)).Should().Equal("Chrome", "Code", "Disc", "Mus", ZedName);
        Names(AllSorting.Apply(Result().Apps, s.Tap(AllColumn.App))).Should().Equal(ZedName, "Mus", "Disc", "Code", "Chrome");
    }

    [Fact]
    public void Days_and_share_sort_by_their_values_and_ties_fall_back_to_the_name()
    {
        var days = AllSorting.Apply(Result().Apps, new AllSort(AllColumn.DaysUsed, true));
        Names(days).Should().Equal("Chrome", "Code", "Disc", "Mus", ZedName); // Mus and Zed tie on 1 day: A-Z
        var share = AllSorting.Apply(Result().Apps, new AllSort(AllColumn.Share, false));
        Names(share).Should().Equal("Mus", ZedName, "Disc", "Code", "Chrome");
    }

    [Fact]
    public void Retired_rows_keep_their_order_whatever_the_sort_is()
    {
        _f.Exec("INSERT INTO apps(id, exe_path, display_name, added_utc, state, retired_utc) VALUES (901, 'c:\\a.exe', 'Aaa', 0, 'retired', " + U(2026, 2, 1, 0) + ")");
        // Most recently stopped first: Old (Mar 1) then Aaa (Feb 1).
        Result().Retired.Select(r => r.Name).Should().Equal("Old", "Aaa");
    }
}
