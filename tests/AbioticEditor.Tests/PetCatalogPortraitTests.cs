using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Locks in the pet -> bestiary portrait mapping used by the PETS tab. The first candidate
/// must be the real in-pak texture name for each variant (verified against the mounted paks:
/// T_Compendium_ElectroPest, T_Compendium_PeccarySow, T_Compendium_RattusPestis, ...).
/// </summary>
public class PetCatalogPortraitTests
{
    [Theory]
    [InlineData("NPC_Monster_Pest", "T_Compendium_Pest")]
    [InlineData("NPC_Monster_Pest_Electro", "T_Compendium_ElectroPest")]
    [InlineData("NPC_Monster_Pest_Rattus", "T_Compendium_RattusPestis")]
    [InlineData("NPC_Peccary", "T_Compendium_Peccary")]
    [InlineData("NPC_Peccary_Sow", "T_Compendium_PeccarySow")]
    [InlineData("NPC_Peccary_Electro", "T_Compendium_ElectroPeccary")]
    [InlineData("NPC_Skink_Magma", "T_Compendium_MagmaSkink")]
    public void FirstCandidate_ResolvesToRealPortrait(string shortClass, string expectedTextureName)
    {
        var refs = PetCatalog.CompendiumTextureRefs(shortClass);
        Assert.NotEmpty(refs);
        Assert.Contains(refs, r => r.EndsWith(expectedTextureName, StringComparison.Ordinal));
    }

    [Fact]
    public void FullClassPath_IsAccepted()
    {
        var refs = PetCatalog.CompendiumTextureRefs(
            "/Game/Blueprints/Characters/NPCs/NPC_Peccary_Sow.NPC_Peccary_Sow_C");
        Assert.Contains(refs, r => r.EndsWith("T_Compendium_PeccarySow", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyInput_YieldsNoRefs()
        => Assert.Empty(PetCatalog.CompendiumTextureRefs(null));
}
