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
/// changed (both are real, universal <c>AbioticCharacter</c> fields), never a name, XP, or species
/// (the class exposes neither the identity fields nor a way to verify a species-change result).
/// Round 109 stopped refusing live species change outright for MATCHED (Pest/Skink-family) rows -
/// see <see cref="LivePetDirectory.SupportsSpeciesChange"/>, now reported by the live agent itself
/// (older agent builds that omit the field still report <c>false</c>, so the app's control stays
/// hidden against them). The game's own <c>Abiotic_Survival_GameMode_C.SpawnPet(Class,
/// SpawnTransform, Guid, Name, Owner, DynamicProperties, Tamed)</c> needs an <c>FTransform</c> for
/// <c>SpawnTransform</c>, which round 76/105 refused because this project had no construction
/// precedent for a HAND-BUILT FTransform table (guessing that shape live is exactly what caused
/// the BASES tab's fatal crash, round 79). <c>pets.lua</c>'s <c>trySpeciesChange</c> does not build
/// one: it reads the OLD pet actor's own current transform fresh via the standard, zero-argument
/// <c>K2_GetActorTransform</c> (the same category of call as the already-proven
/// <c>K2_GetActorLocation</c>/<c>K2_GetActorRotation</c> round trip <c>spawn.lua</c>/
/// <c>vehicles.lua</c> use) and passes it straight back UNCHANGED - never a fabricated struct. See
/// that file's own header comment for the full reasoning, the safety ordering (the new actor's
/// Guid is verified to match before the old one is destroyed - any failure leaves the old pet
/// untouched), and the one honest caveat: a wrong-shaped native-call argument is the one class of
/// failure in this project <c>pcall</c> cannot be trusted to catch, and this exact call has never
/// run against the real game yet - it is proven only against the Lua stub harness so far. Removal
/// (round 78) IS supported for every row, matched or not - see <see cref="RemoveAsync"/>.
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
    /// of a pet that has never earned real XP, which cannot be fabricated live; round 109 - a
    /// species change that can't be resolved or verified): those come back as
    /// <see cref="LivePetSetResult.Warnings"/> rather than an exception, so the fields that DID
    /// apply are never thrown away along with the one that didn't. <paramref name="npcClass"/> is
    /// only ever treated as a real species-change REQUEST when it differs from the pet's own
    /// current class (see <c>pets.lua</c>'s own header comment) - resending the pet's current class
    /// unchanged is always a safe no-op.</summary>
    public async Task<LivePetSetResult> SetAsync(string id, bool isDead, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, string? npcClass = null,
        CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<SetResultWire>("pets.set",
            new SetWire(id, isDead, customName, xp, limbHealth, npcClass), cancellationToken).ConfigureAwait(false);
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
        IReadOnlyDictionary<string, double> LimbHealth, string? NpcClass = null);
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
/// true now, but partial - see <paramref name="Reason"/>). <paramref name="SupportsSpeciesChange"/>
/// (round 109) reflects what the connected live agent itself reports - an older agent build that
/// never sends the field deserializes to <c>false</c> here (the wire's default), so the app's
/// creature-type control stays hidden against it automatically; a newer one that reports
/// <c>true</c> can still only apply it to a MATCHED row (see <see cref="LivePetsChannel"/>'s
/// remarks for the mechanism and its one remaining unverified assumption). Removal works for every
/// row regardless.</summary>
public sealed record LivePetDirectory(IReadOnlyList<LivePet> Pets, bool IsHost, bool Available, string? Reason,
    bool SupportsSpeciesChange, bool SupportsRemoval);
