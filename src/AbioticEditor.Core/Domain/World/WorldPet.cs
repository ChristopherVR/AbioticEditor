namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One entry of a world save's <c>PetNPC</c> map - a tamed companion. Pets share the
/// <c>SaveData_NPCState_Struct</c> with narrative NPCs but, unlike them, actually fill
/// the pet-specific fields: a player name, a creature class, per-limb health, and an XP
/// counter (level is derived from XP - see <see cref="PetCatalog.LevelForXp"/>).
/// </summary>
/// <param name="Id">Map key - the pet's GUID.</param>
/// <param name="IsDead">The only persisted life flag. "Downed" is not stored; it is
/// derived from <see cref="LimbHealth"/> at runtime by the game.</param>
/// <param name="NpcClass">The creature blueprint soft path
/// (<c>/Game/Blueprints/Characters/NPCs/NPC_Monster_Pest_Electro.NPC_Monster_Pest_Electro_C</c>).
/// Swapping this is the editor's "upgrade / downgrade" (the game's mutation).</param>
/// <param name="CustomName">Player-given name (e.g. "Rex"); null when unnamed.</param>
/// <param name="LimbHealth">Per-limb current health, keyed by the full
/// <c>EBodyLimbs::*</c> enum string (e.g. <c>EBodyLimbs::Head</c>).</param>
/// <param name="Xp">The pet's experience (<c>EDynamicProperty::XP</c>).</param>
/// <param name="State">The <c>NarrativeState_</c> enum value as stored (game-internal).</param>
/// <param name="Matched">True when this row can be tied to the save's own <c>PetNPC</c> record by
/// <paramref name="Id"/> - always true for the file session and for a live Pest/Skink-family
/// pet (its <c>Guid</c> field). False only for a live row read via a Peccary/Lamogi (or any other
/// creature family that still carries no <c>Guid</c>/<c>PetName</c>/<c>DynamicProperties</c> of its
/// own) - see <c>areas/pets.lua</c>'s remarks on the tamed-marker sweep. Those rows use the live
/// actor's own full path as <see cref="Id"/>, which is stable only for that actor's lifetime, not a
/// save key, so <see cref="CustomName"/>/<see cref="Xp"/> can never be read or written for them
/// (the class exposes no such fields at all); only <see cref="IsDead"/>/<see cref="LimbHealth"/>
/// are real, universal <c>AbioticCharacter</c> fields and stay editable.</param>
public sealed record WorldPet(
    string Id,
    bool IsDead,
    string? NpcClass,
    double X,
    double Y,
    double Z,
    string? CustomName,
    IReadOnlyDictionary<string, double> LimbHealth,
    int Xp,
    string? State,
    bool Matched = true)
{
    /// <summary>
    /// The class tail without the <c>_C</c> suffix, e.g. <c>NPC_Monster_Pest_Electro</c>.
    /// Empty when the class is unknown.
    /// </summary>
    public string ShortClass
    {
        get
        {
            if (string.IsNullOrEmpty(NpcClass)) return string.Empty;
            var tail = NpcClass![(NpcClass.LastIndexOf('.') + 1)..];
            return tail.EndsWith("_C", StringComparison.Ordinal) ? tail[..^2] : tail;
        }
    }

    /// <summary>The player name when set, otherwise the friendly catalog name, otherwise the class tail.</summary>
    public string DisplayName
        => !string.IsNullOrWhiteSpace(CustomName)
            ? CustomName!
            : PetCatalog.FriendlyName(NpcClass) ?? (ShortClass.Length > 0 ? ShortClass : Id);

    /// <summary>Sum of all limb health values (the pet's effective total HP).</summary>
    public double TotalHealth => LimbHealth.Values.Sum();

    /// <summary>Derived level (0-20) from <see cref="Xp"/>.</summary>
    public int Level => PetCatalog.LevelForXp(Xp);
}
