namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One entry of a world save's <c>NarrativeNPCMap</c> (traders and story NPCs) or
/// <c>PetNPC</c> map (tamed companions) - both use the same
/// <c>SaveData_NPCState_Struct</c>.
/// </summary>
/// <param name="Id">Map key - the actor path (narrative) or pet GUID.</param>
/// <param name="IsDead">Whether the NPC is dead.</param>
/// <param name="State">
/// The <c>NarrativeState_</c> enum value as stored, e.g.
/// <c>E_NarrativeNPCStates::NewEnumerator3</c> (the game's enum names are compiler
/// artifacts; there are no friendly names in the assets).
/// </param>
/// <param name="IsPet">True when the entry came from the <c>PetNPC</c> map.</param>
/// <param name="CustomName">Player-given name (pets: e.g. "Rex"); empty when unnamed.</param>
/// <param name="NpcClass">The actor class soft path (<c>NPCClass_</c>), pets only.</param>
public sealed record WorldNpc(
    string Id,
    bool IsDead,
    string? State,
    double X = 0,
    double Y = 0,
    double Z = 0,
    bool IsPet = false,
    string? CustomName = null,
    string? NpcClass = null)
{
    /// <summary>
    /// Short display name: the player-given name when one exists, then the class tail
    /// (pets), then the actor-path tail, e.g. <c>NarrativeNPC_Human_ParentBP_C_1</c>.
    /// </summary>
    public string ActorName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomName)) return CustomName!;
            if (IsPet && !string.IsNullOrEmpty(NpcClass))
            {
                var tail = NpcClass![(NpcClass.LastIndexOf('.') + 1)..];
                return tail.EndsWith("_C", StringComparison.Ordinal) ? tail[..^2] : tail;
            }
            var idx = Id.LastIndexOf('.');
            return idx >= 0 ? Id[(idx + 1)..] : Id;
        }
    }
}
