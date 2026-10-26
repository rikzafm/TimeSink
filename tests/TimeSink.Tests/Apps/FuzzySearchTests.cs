using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class FuzzySearchTests
{
    [Theory]
    [InlineData("vsc", "Visual Studio Code")]
    [InlineData("chorme", "Chrome")]
    [InlineData("photoshp", "Adobe Photoshop")]
    [InlineData("studio visual", "Visual Studio Code")]
    public void Matches_subsequences_typos_and_word_order(string query, string name) =>
        FuzzySearch.Score(query, name).Should().NotBeNull();

    [Theory]
    [InlineData("zzz", "Chrome")]
    [InlineData("firefox", "Chrome")]
    public void Rejects_unrelated_names(string query, string name) =>
        FuzzySearch.Score(query, name).Should().BeNull();

    [Fact]
    public void Name_match_outranks_publisher_match_and_exact_outranks_fuzzy()
    {
        FuzzySearch.Score("adobe", "Adobe Reader", null)!.Value.Should().BeGreaterThan(FuzzySearch.Score("adobe", "Photoshop", "Adobe")!.Value);
        FuzzySearch.Score("chrome", "Chrome")!.Value.Should().BeGreaterThan(FuzzySearch.Score("chorme", "Chrome")!.Value);
    }

    [Fact]
    public void Publisher_is_searched() =>
        FuzzySearch.Score("serif", "Affinity Photo", "Serif").Should().NotBeNull();
}
