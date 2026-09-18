namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live pet editing. Pest- and Skink-family pets are matched to a stable id (see
/// <c>pets.list</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/pets.lua</c> for the
/// full research finding). Round 105 added a second, generic sweep for every other tamed
/// creature (Peccary/Lamogi families and any future one): matched not by a hardcoded class list
/// but by calling the game's own <c>AbioticFunctionLibrary::IsTamedPet(Actor)</c> on every
/// <c>NPC_Base_ParentBP_C</c> that has no <c>Guid</c> of its own - see <see cref="LivePet.Matched"/>.
/// Those rows carry no stable id (their <see cref="LivePet.Id"/> is the live actor's own full path,
/// valid only for that actor's current lifetime) and can only have <c>IsDead</c>/limb health
/// changed (both are real, universal <c>AbioticCharacter</c> fields), never a name or XP (the class
/// exposes neither). There is no live species change - see
/// <see cref="LivePetDirectory.SupportsSpeciesChange"/>, always false: the game's own
/// <c>Abiotic_Survival_GameMode_C.SpawnPet(Class, SpawnTransform, Guid, Name, Owner,
/// DynamicProperties, Tamed)</c> is a real function (found in this round's class dump) but its
/// <c>SpawnTransform</c> parameter is an <c>FTransform</c>, a struct this project has no working
/// construction precedent for over UE4SS Lua reflection anywhere (unlike the flat
/// <c>FVector</c>/<c>FRotator</c> tables round 76 proved) - guessing that shape live is exactly
/// what caused the BASES tab's fatal crash (round 79), so this stays refused. Removal (round 78)
/// IS supported for every row, matched or not - see <see cref="RemoveAsync"/>.
/// </summary>
public sealed class LivePetsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LivePetDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("pets.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var pets = (wire.Pets ?? [])
            .Select(p => new LivePet(p.Id, p.NpcClass, p.IsDead, p.CustomName, p.X, p.Y, p.Z,
                p.LimbHealth ?? new Dictionary<string, double>(), p.Xp, p.Matched))
            .ToList();
        return new LivePetDirectory(pets, wire.IsHost, wire.Available, wire.Reason,
            wire.SupportsSpeciesChange, wire.SupportsRemoval);
    }

    /// <summary>Stages/applies a Pest- or Skink-family pet's fields immediately. Host only. Some
    /// fields can genuinely fail without the whole call failing (round 78 - e.g. raising the level
    /// of a pet that has never earned real XP, which cannot be fabricated live): those come back
    /// as <see cref="LivePetSetResult.Warnings"/> rather than an exception, so the fields that DID
    /// apply are never thrown away along with the one that didn't.</summary>
    public async Task<LivePetSetResult> SetAsync(string id, bool isDead, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<SetResultWire>("pets.set",
            new SetWire(id, isDead, customName, xp, limbHealth), cancellationToken).ConfigureAwait(false);
        return new LivePetSetResult(wire?.Warnings ?? []);
    }

    /// <summary>Removes a pet by destroying its live actor outright (round 78: no blueprint
    /// function cleanly "releases" a tamed world pet, so this uses the same standard
    /// <c>K2_DestroyActor</c> call the reference CheatConsoleCommands mod's own "deleteobject"
    /// command already uses on an arbitrary world actor - see <c>pets.lua</c>'s own remarks). Host
    /// only. There is no undo once this returns.</summary>
    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("pets.remove", new IdWire(id), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<PetWire>? Pets, bool IsHost, bool Available, string? Reason,
        bool SupportsSpeciesChange, bool SupportsRemoval);
    private sealed record PetWire(string Id, string? NpcClass, bool IsDead, string? CustomName,
        double X, double Y, double Z, Dictionary<string, double>? LimbHealth, int Xp, bool Matched = true);
    private sealed record SetWire(string Id, bool IsDead, string? CustomName, int Xp,
        IReadOnlyDictionary<string, double> LimbHealth);
    private sealed record SetResultWire(IReadOnlyList<string>? Warnings);
    private sealed record IdWire(string Id);
}

/// <summary>The outcome of <see cref="LivePetsChannel.SetAsync"/>: the call itself succeeded, but
/// <paramref name="Warnings"/> lists any individual field that could not be applied (player-safe
/// text, ready to show as-is).</summary>
public sealed record LivePetSetResult(IReadOnlyList<string> Warnings);

/// <summary>One live pet row. When <paramref name="Matched"/> is true (Pest/Skink family),
/// <paramref name="Id"/> is the pet's own <c>Guid</c> string field - the same stable id the save's
/// <c>PetNPC</c> map uses as its key, so a live row and a file row for the same pet share the same
/// id. When <paramref name="Matched"/> is false (round 105: any tamed creature the
/// <c>AbioticFunctionLibrary::IsTamedPet</c> sweep found with no <c>Guid</c> of its own - Peccary
/// and Lamogi families today), <paramref name="Id"/> is the live actor's own full path instead
/// (stable only for that actor's current lifetime, never a save key) and <paramref name="CustomName"/>
/// /<paramref name="Xp"/> are always null/0 - the class exposes neither field to read or write.</summary>
public sealed record LivePet(string Id, string? NpcClass, bool IsDead, string? CustomName,
    double X, double Y, double Z, IReadOnlyDictionary<string, double> LimbHealth, int Xp,
    bool Matched = true);

/// <summary>Every live pet row found (matched and unmatched - see <see cref="LivePet.Matched"/>),
/// whether this process has host authority, and whether pet editing is available at all (always
/// true now, but partial - see <paramref name="Reason"/>). Species change has no evidenced safe
/// live path (see <see cref="LivePetsChannel"/>'s remarks); removal works for every row.</summary>
public sealed record LivePetDirectory(IReadOnlyList<LivePet> Pets, bool IsHost, bool Available, string? Reason,
    bool SupportsSpeciesChange, bool SupportsRemoval);
