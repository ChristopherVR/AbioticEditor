using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Locks in the containment-tab art mapping: each creature resolves its OWN portrait and
/// never borrows another's. Regression guard for the old bug where every unmatched row
/// (notably the Krasue) fell back to the Leyak compendium texture.
/// </summary>
public class ContainmentCreatureCatalogTests
{
    [Fact]
    public void Leyak_ResolvesItsOwnCompendiumArt()
    {
        var refs = ContainmentCreatureCatalog.TextureRefs("Leyak");
        Assert.Contains(refs, r => r.EndsWith("T_Compendium_Leyak", StringComparison.Ordinal));
    }

    [Fact]
    public void Krasue_HasNoInPakPortrait_AndNeverBorrowsLeyakArt()
    {
        // The game ships no compendium portrait for the Krasue, so the catalog returns no
        // in-pak refs (the App then uses its bundled wiki image). Critically it must not
        // hand back a Leyak texture as a stand-in.
        var refs = ContainmentCreatureCatalog.TextureRefs("Krasue");
        Assert.Empty(refs);
        Assert.DoesNotContain(refs, r => r.Contains("Leyak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownCreature_GuessesOwnNameNotLeyak()
    {
        var refs = ContainmentCreatureCatalog.TextureRefs("Wraith");
        Assert.Contains(refs, r => r.EndsWith("T_Compendium_Wraith", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.Contains("Leyak", StringComparison.OrdinalIgnoreCase));
    }
}
