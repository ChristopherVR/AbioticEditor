using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// CarriedPetEdit.MutationProgress is now editable (previously read-only display text in both the
/// offline and live COMPANIONS tab - see PetCareGuide.razor). Only negative values are rejected:
/// PetCatalog.ObservedMaxMutationProgress is a hint, not a cap (see that constant's own remarks).
/// PetMutation (the mutation target) stays a plain unclamped pass-through: the UI never offers an
/// editor for it, matching review-features.md's "kept as the game's value" rule. The live-session
/// version of this (LivePlayerCompanionsSession, which loads rows through this same CarriedPetEdit
/// type) is covered in TcpLiveGameChannelTests.LivePlayerCompanionsSession_surfaces_and_commits_MutationProgress.
/// </summary>
public class CarriedPetMutationProgressTests
{
    private static CarriedPetEdit NewEdit(int mutationProgress = 0, int petMutation = 0) =>
        new(new CarriedPet(PetSlotKind.Equipment, 12, "pet_skink", "Sparky", 100, 100, 50, mutationProgress, petMutation));

    [Fact]
    public void MutationProgress_rejects_negatives_but_keeps_values_above_the_observed_max()
    {
        var pet = NewEdit();

        pet.MutationProgress = -5;
        Assert.Equal(0, pet.MutationProgress);

        pet.MutationProgress = PetCatalog.ObservedMaxMutationProgress + 100;
        Assert.Equal(PetCatalog.ObservedMaxMutationProgress + 100, pet.MutationProgress);

        pet.MutationProgress = PetCatalog.ObservedMaxMutationProgress;
        Assert.Equal(PetCatalog.ObservedMaxMutationProgress, pet.MutationProgress);
    }

    [Fact]
    public void PetMutation_is_a_plain_unclamped_pass_through()
    {
        // No UI offers an editor for this field (read-only by design), but the model itself must
        // not silently rewrite whatever value the game already stored.
        var pet = NewEdit(petMutation: 6);
        Assert.Equal(6, pet.PetMutation);
        pet.PetMutation = 9999;
        Assert.Equal(9999, pet.PetMutation);
    }

    [Fact]
    public void ToCarriedPet_carries_the_edited_value_through()
    {
        var pet = NewEdit();
        pet.MutationProgress = PetCatalog.ObservedMaxMutationProgress + 1;
        Assert.Equal(PetCatalog.ObservedMaxMutationProgress + 1, pet.ToCarriedPet().MutationProgress);
    }

    [Fact]
    public void Load_from_an_existing_CarriedPet_keeps_a_higher_saved_value()
    {
        // The constructor/Revert path (CarriedPetEdit.Load) assigns through the same public
        // setter. A pet whose save already holds a value above anything this project has seen
        // must round-trip untouched: silently lowering it on load would rewrite the player's
        // save on the next SAVE with no edit behind it.
        var edit = NewEdit(mutationProgress: PetCatalog.ObservedMaxMutationProgress + 42);
        Assert.Equal(PetCatalog.ObservedMaxMutationProgress + 42, edit.MutationProgress);
    }
}
