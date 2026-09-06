namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for editing a player's carried pets, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>):
/// exactly the members <c>PlayerCompanionsTab.razor</c> needs, extracted from
/// <see cref="PlayerSaveSession"/>'s existing <see cref="PlayerSaveSession.CarriedPets"/> slice, so
/// that widget binds to either the file-backed session or <see cref="LivePlayerCompanionsSession"/>
/// with only its parameter's declared type changing. Both sessions reuse the same
/// <see cref="CarriedPetEdit"/> row type unchanged.
/// </summary>
public interface IPlayerCompanionsSession
{
    IReadOnlyList<CarriedPetEdit> CarriedPets { get; }

    /// <summary>A stable key that changes only when the save/connection this tab is bound to
    /// actually changes - see <see cref="IPlayerSpawnSession.SessionKey"/> for the same idea.</summary>
    string SessionKey { get; }

    /// <summary>True for the file session: sending a pet to a world pet bed needs a sibling world
    /// save on disk, which a live connection has no equivalent of (there is no live write path
    /// into a world SAVE FILE - only into the running game's own live world state).</summary>
    bool SupportsWorldIntegration { get; }

    /// <summary>True for the live session: every mutator below takes effect in the running game
    /// immediately instead of staging until SAVE.</summary>
    bool AppliesImmediately { get; }

    bool IsDirty { get; }
    string? Status { get; }
    void MarkChanged();
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();

    /// <summary>Commits one pet row's current field values. File: stages the edit (the same as
    /// <see cref="MarkChanged"/>; the real write happens at the workspace SAVE). Live: writes the
    /// row's item id / name / health / XP / mutation to the running game immediately.</summary>
    Task ApplyPetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default);

    /// <summary>Removes a carried pet. File: stages the removal, reversible until SAVE (or until
    /// UNDO REMOVE is pressed). Live: clears the slot in the running game immediately - there is
    /// no undo once this has been pressed, and the row disappears from <see cref="CarriedPets"/>.</summary>
    Task RemovePetAsync(CarriedPetEdit pet, CancellationToken cancellationToken = default);
}
