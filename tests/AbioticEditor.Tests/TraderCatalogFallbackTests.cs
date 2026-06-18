using System.Linq;
using AbioticEditor.Core.Codex;

namespace AbioticEditor.Tests;

/// <summary>
/// Guards the built-in trader snapshot that lets the editor show traders and their per-trade
/// unlock flags without the game installed (see AbioticEditor.Probes TraderFallbackProbe).
/// </summary>
public sealed class TraderCatalogFallbackTests
{
    [Fact]
    public void Fallback_IsPopulated()
    {
        Assert.NotEmpty(TraderCatalog.Fallback);
        // The eight known traders plus the Fili non-trader row.
        Assert.True(TraderCatalog.Fallback.Count >= 8, "expected the full shipped trader roster");
    }

    [Fact]
    public void Fallback_HasNoDuplicateIds()
    {
        var ids = TraderCatalog.Fallback.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Fallback_CarsonCarriesHisGatingFlags()
    {
        // Dr. Carson is the "Chef" row; the snapshot must carry the flags that gate him and
        // his stock so the unlock UI works offline (the whole point of shipping it).
        var carson = TraderCatalog.Fallback.SingleOrDefault(t => t.Id == "Chef");
        Assert.NotNull(carson);
        Assert.NotEmpty(carson!.Sells);
        Assert.Contains(carson.Sells, o => o.RequiredFlag is not null);
    }

    [Fact]
    public void Fallback_EveryOfferHasAnItemId()
    {
        foreach (var t in TraderCatalog.Fallback)
        {
            Assert.All(t.Sells, o => Assert.False(string.IsNullOrWhiteSpace(o.ItemId)));
            Assert.All(t.Accepts, o => Assert.False(string.IsNullOrWhiteSpace(o.ItemId)));
        }
    }
}
