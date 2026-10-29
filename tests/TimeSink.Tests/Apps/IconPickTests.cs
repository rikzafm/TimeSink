using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class IconPickTests
{
    private static IconChoice Choice(string name, params byte[] png) => new(IconChoiceKind.Exe, name, png);

    private static readonly IconChoice A = Choice("a", 1, 1);
    private static readonly IconChoice B = Choice("b", 2, 2);
    private static readonly IconChoice C = Choice("c", 3, 3);

    [Fact]
    public void The_picker_is_offered_only_for_more_than_one_icon()
    {
        IconPick.Offered([]).Should().BeFalse();
        IconPick.Offered([A]).Should().BeFalse();
        IconPick.Offered([A, B]).Should().BeTrue();
    }

    [Fact]
    public void The_icon_in_use_starts_selected_and_is_not_a_change()
    {
        var pick = new IconPick([A, B, C], B.Png);

        pick.InUse.Should().Be(1);
        pick.Selected.Should().Be(1);
        pick.Changed.Should().BeFalse();
    }

    [Fact]
    public void With_no_icon_in_use_the_first_is_selected_and_is_a_change()
    {
        foreach (var current in new byte[]?[] { null, [9, 9] })
        {
            var pick = new IconPick([A, B], current);
            pick.InUse.Should().Be(-1);
            pick.Selected.Should().Be(0);
            pick.Changed.Should().BeTrue();
        }
    }

    [Fact]
    public void Selecting_another_choice_is_a_change_and_selecting_the_one_in_use_again_is_not()
    {
        var pick = new IconPick([A, B], A.Png);

        pick.Select(1);
        pick.Changed.Should().BeTrue();
        pick.SelectedPng.Should().Equal(B.Png);

        pick.Select(0);
        pick.Changed.Should().BeFalse();
    }

    [Fact]
    public void Selecting_outside_the_list_does_nothing()
    {
        var pick = new IconPick([A, B], A.Png);
        pick.Select(5);
        pick.Select(-1);
        pick.Selected.Should().Be(0);
    }

    [Fact]
    public void A_picked_file_is_added_last_and_selected()
    {
        var pick = new IconPick([A, B], A.Png);

        pick.Add(Choice("file", 7, 7));

        pick.Choices.Select(c => c.Detail).Should().Equal("a", "b", "file");
        pick.Selected.Should().Be(2);
        pick.Changed.Should().BeTrue();
    }

    [Fact]
    public void A_picked_file_that_draws_a_listed_icon_selects_it_instead_of_adding_a_duplicate()
    {
        var pick = new IconPick([A, B], A.Png);

        pick.Add(Choice("copy of b", 2, 2));
        pick.Choices.Should().HaveCount(2);
        pick.Selected.Should().Be(1);

        pick.Add(Choice("file", 7, 7));
        pick.Add(Choice("same file again", 7, 7));
        pick.Choices.Should().HaveCount(3);
        pick.Selected.Should().Be(2);
    }

    [Fact]
    public void A_picked_file_that_draws_the_current_icon_becomes_the_one_in_use()
    {
        var pick = new IconPick([A, B], [5, 5]);

        pick.Add(Choice("file", 5, 5));

        pick.InUse.Should().Be(2);
        pick.Changed.Should().BeFalse();
    }
}
