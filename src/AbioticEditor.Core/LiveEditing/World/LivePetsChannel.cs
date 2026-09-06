namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live pet editing: Pest- and Skink-family pets only, matched to a stable id (see
/// <c>pets.list</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/pets.lua</c> for the
/// full research finding). Peccary and Lamogi family pets still have no evidenced stable id and
/// are never reported here. There is no live species change or removal - see
/// <see cref="LivePetDirectory.SupportsSpeciesChange"/>/<see cref="LivePetDirectory.SupportsRemoval"/>,
/// always false.
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
                p.LimbHealth ?? new Dictionary<string, double>(), p.Xp))
            .ToList();
        return new LivePetDirectory(pets, wire.IsHost, wire.Available, wire.Reason,
            wire.SupportsSpeciesChange, wire.SupportsRemoval);
    }

    /// <summary>Stages/applies a Pest- or Skink-family pet's fields immediately. Host only.</summary>
    public Task SetAsync(string id, bool isDead, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("pets.set",
            new SetWire(id, isDead, customName, xp, limbHealth), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<PetWire>? Pets, bool IsHost, bool Available, string? Reason,
        bool SupportsSpeciesChange, bool SupportsRemoval);
    private sealed record PetWire(string Id, string? NpcClass, bool IsDead, string? CustomName,
        double X, double Y, double Z, Dictionary<string, double>? LimbHealth, int Xp);
    private sealed record SetWire(string Id, bool IsDead, string? CustomName, int Xp,
        IReadOnlyDictionary<string, double> LimbHealth);
}

/// <summary>One live-matched pet (Pest/Skink family). <paramref name="Id"/> is the pet's own
/// <c>Guid</c> string field - the same stable id the save's <c>PetNPC</c> map uses as its key, so
/// a live row and a file row for the same pet share the same id.</summary>
public sealed record LivePet(string Id, string? NpcClass, bool IsDead, string? CustomName,
    double X, double Y, double Z, IReadOnlyDictionary<string, double> LimbHealth, int Xp);

/// <summary>Every Pest/Skink-family pet currently matched, whether this process has host
/// authority, and whether pet editing is available at all (always true now, but partial - see
/// <paramref name="Reason"/>). Species change and removal have no evidenced live path.</summary>
public sealed record LivePetDirectory(IReadOnlyList<LivePet> Pets, bool IsHost, bool Available, string? Reason,
    bool SupportsSpeciesChange, bool SupportsRemoval);
