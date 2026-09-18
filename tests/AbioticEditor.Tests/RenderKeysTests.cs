using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Unit tests for <see cref="RenderKeys"/>, the shared helper that turns any sequence into
/// collision-proof Blazor <c>@key</c> values - see that type's own remarks and
/// <see cref="RenderKeySafetyContractTests"/> for the round-122 crash this exists to make
/// impossible.
/// </summary>
public sealed class RenderKeysTests
{
    [Fact]
    public void With_keeps_unique_ids_unchanged()
    {
        var items = new[] { "alpha", "bravo", "charlie" };

        var result = RenderKeys.With(items, id => id);

        Assert.Equal(["alpha", "bravo", "charlie"], result.Select(pair => pair.Key));
        Assert.Equal(items, result.Select(pair => pair.Item));
    }

    [Fact]
    public void With_suffixes_repeated_ids_starting_at_number_2()
    {
        var items = new[] { "dup", "dup", "dup" };

        var result = RenderKeys.With(items, id => id);

        Assert.Equal(["dup", "dup#2", "dup#3"], result.Select(pair => pair.Key));
    }

    [Fact]
    public void With_produces_no_duplicate_keys_even_with_many_repeats()
    {
        var items = Enumerable.Repeat("CA_PunchCard_TutorialPanelTrigger", 5).ToList();

        var result = RenderKeys.With(items, id => id);

        var keys = result.Select(pair => pair.Key).ToList();
        Assert.Equal(5, keys.Count);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void With_preserves_input_order()
    {
        var items = new[] { "b", "a", "b", "c", "a" };

        var result = RenderKeys.With(items, id => id);

        Assert.Equal(["b", "a", "b#2", "c", "a#2"], result.Select(pair => pair.Key));
        Assert.Equal(items, result.Select(pair => pair.Item));
    }

    [Fact]
    public void With_treats_a_null_selector_result_as_an_empty_string_id()
    {
        var items = new string?[] { null, null, "x" };

        var result = RenderKeys.With(items, id => id);

        Assert.Equal(["", "#2", "x"], result.Select(pair => pair.Key));
    }

    [Fact]
    public void With_does_not_mix_distinct_ids_that_share_a_suffix_looking_value()
    {
        // "dup" then a literal "dup#2" must not collide with the auto-suffixed second "dup".
        var items = new[] { "dup", "dup#2" };

        var result = RenderKeys.With(items, id => id);

        Assert.Equal(["dup", "dup#2"], result.Select(pair => pair.Key));
        // Both keys textually equal is a theoretical residual collision (two independently
        // "real" ids colliding with an auto-suffix), acceptable because it requires an id that
        // already contains the "#N" convention verbatim - astronomically unlikely for any of
        // this app's real id shapes (GUIDs, actor paths, catalog row names).
    }

    // A reference type with no Equals/GetHashCode override, so two distinct instances that
    // happen to carry the same Id are still two distinct dictionary keys - the real shape
    // KeyLookup is for (two different rows whose underlying data id happens to repeat).
    private sealed class Item(string id)
    {
        public string Id { get; } = id;
    }

    [Fact]
    public void KeyLookup_gives_every_item_a_unique_key_reachable_by_lookup()
    {
        var first = new Item("a");
        var second = new Item("b");
        var third = new Item("a");
        var items = new[] { first, second, third };

        var lookup = RenderKeys.KeyLookup(items, i => i.Id);

        Assert.Equal(3, lookup.Count);
        Assert.Equal("a", lookup[first]);
        Assert.Equal("b", lookup[second]);
        Assert.Equal("a#2", lookup[third]);
    }
}
