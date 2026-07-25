using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The web sidebar's stand-in for the native pixel-fitted MiddleTruncation label mode:
/// long save names keep their start and end (the extension) around a middle ellipsis.
/// </summary>
public sealed class MiddleTruncationTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Player_1.sav", "Player_1.sav")]
    public void Short_names_pass_through_unchanged(string? value, string expected)
        => Assert.Equal(expected, MiddleTruncation.Truncate(value, 36));

    [Fact]
    public void A_name_exactly_at_the_budget_is_not_truncated()
    {
        var value = new string('a', 36);
        Assert.Same(value, MiddleTruncation.Truncate(value, 36));
    }

    [Fact]
    public void A_long_name_keeps_its_start_and_extension_around_a_middle_ellipsis()
    {
        var value = "Player_76561197993781479 - backup copy with a very long name.sav";
        var truncated = MiddleTruncation.Truncate(value, 36);

        Assert.Equal(36, truncated.Length);
        Assert.Equal(1, truncated.Count(c => c == MiddleTruncation.Ellipsis));
        Assert.StartsWith("Player_7656119799", truncated, StringComparison.Ordinal);
        Assert.EndsWith("name.sav", truncated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ellipsis_sits_in_the_middle_of_the_budget()
    {
        var truncated = MiddleTruncation.Truncate(new string('a', 50) + new string('b', 50), 21);
        Assert.Equal(new string('a', 10) + MiddleTruncation.Ellipsis + new string('b', 10), truncated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Tiny_budgets_collapse_to_a_single_ellipsis(int budget)
        => Assert.Equal(MiddleTruncation.Ellipsis.ToString(), MiddleTruncation.Truncate("WorldSave_Facility.sav", budget));

    [Fact]
    public void An_even_budget_gives_the_head_the_extra_character()
        => Assert.Equal("abc" + MiddleTruncation.Ellipsis + "yz", MiddleTruncation.Truncate("abcdefghijklmnopqrstuvwxyz", 6));
}
