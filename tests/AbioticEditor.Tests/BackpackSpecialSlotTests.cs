using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="BackpackSpecialSlotCatalog"/>: the built-in table and the dynamic
/// DT_ItemCosmetics scan that future-proofs it against new backpacks.
/// </summary>
public class BackpackSpecialSlotTests
{
    [Fact]
    public void Fallback_covers_the_four_known_packs()
    {
        var c = BackpackSpecialSlotCatalog.Fallback;
        Assert.Equal(4, c.Count);

        var glitch = c.For("backpack_voidpack_U1b");
        Assert.Equal("FREEZER", glitch[1]);
        Assert.Equal("SHIELDED", glitch[6]);
        Assert.Equal("COLD", glitch[8]);
        Assert.Equal("WARM", glitch[13]);

        Assert.Empty(c.For("backpack_huge"));
        Assert.Empty(c.For(null));
        Assert.Empty(c.For("not_a_backpack"));
    }

    [Fact]
    public void Dynamic_scan_matches_the_verified_table()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var dynamic = BackpackSpecialSlotCatalog.LoadFrom(provider);

        // The scan walks DT_ItemCosmetics -> data assets. Against the validated game
        // build it must reproduce the verified table exactly; on a future build it may
        // contain MORE packs, so assert the known subset rather than equality of counts.
        foreach (var packId in new[]
                 {
                     "backpack_large", "backpack_research_U1a",
                     "backpack_research_U1b", "backpack_voidpack_U1b",
                 })
        {
            var expected = BackpackSpecialSlotCatalog.Fallback.For(packId);
            var actual = dynamic.For(packId);
            Assert.Equal(expected.Count, actual.Count);
            foreach (var (slot, label) in expected)
            {
                Assert.True(actual.TryGetValue(slot, out var got), $"{packId} slot {slot} missing");
                Assert.Equal(label, got);
            }
        }

        Assert.True(dynamic.Count >= 4, $"expected at least the 4 known packs, got {dynamic.Count}");
        Assert.Empty(dynamic.For("backpack_medium"));
    }
}
