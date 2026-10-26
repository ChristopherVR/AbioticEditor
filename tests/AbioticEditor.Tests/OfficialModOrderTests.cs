using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

public sealed class OfficialModOrderTests
{
    [Fact]
    public void Dependencies_win_over_selection_order_and_missing_optional_mods_do_not_block()
    {
        var sorted = OfficialModOrder.Sort([
            new("dependent", 2, 0, [1, 99]), new("base", 1, 1, []), new("independent", 3, 2, [])]);
        Assert.Equal(["base", "dependent", "independent"], sorted.Select(entry => entry.Path));
    }

    [Fact]
    public void Cycles_and_their_dependents_are_excluded_without_blocking_independent_mods()
    {
        var sorted = OfficialModOrder.Sort([
            new("a", 1, 0, [2]), new("b", 2, 1, [1]), new("c", 3, 2, [1]), new("d", 4, 3, [])]);
        Assert.Equal("d", Assert.Single(sorted).Path);
    }
}
