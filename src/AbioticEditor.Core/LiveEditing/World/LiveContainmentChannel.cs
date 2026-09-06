namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live Leyak Containment Unit editing: lists every deployed unit (<c>Deployed_LeyakContainment_C</c>)
/// currently loaded, with its occupant (Leyak/Krasue/none) and stability, and lets a host assign,
/// release or swap creatures between units - see <c>containment.list</c>/<c>containment.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/containment.lua</c>. The write path is
/// the reference mod's own trap/free commands (<c>AFUtils.TrapLeyak</c>/<c>FreeLeyak</c>/
/// <c>TrapKrasue</c>/<c>FreeKrasue</c>), so this mirrors what those console commands already do
/// live, one unit at a time.
/// </summary>
public sealed class LiveContainmentChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveContainmentDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("containment.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var units = (wire.Units ?? [])
            .Select(u => new LiveContainmentUnit(u.Id, u.X, u.Y, u.Z, u.Stability, u.Creature))
            .ToList();
        return new LiveContainmentDirectory(units, wire.IsHost);
    }

    /// <summary>Assigns <paramref name="creature"/> ("Leyak" or "Krasue") into <paramref name="unitId"/>,
    /// evicting whoever occupies that unit and freeing the creature from any other unit first.</summary>
    public Task AssignAsync(string unitId, string creature, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containment.set",
            new SetWire("assign", unitId, creature, null, null), cancellationToken);

    /// <summary>Frees <paramref name="creature"/> from whichever unit currently holds it.</summary>
    public Task ReleaseAsync(string creature, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containment.set",
            new SetWire("release", null, creature, null, null), cancellationToken);

    /// <summary>Exchanges the occupants of two units in one step.</summary>
    public Task SwapAsync(string unitIdA, string unitIdB, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containment.set",
            new SetWire("swap", null, null, unitIdA, unitIdB), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<UnitWire>? Units, bool IsHost);
    private sealed record UnitWire(string Id, double X, double Y, double Z, int? Stability, string? Creature);
    private sealed record SetWire(string Action, string? UnitId, string? Creature, string? UnitIdA, string? UnitIdB);
}

/// <summary>One loaded containment unit. <paramref name="Id"/> is the game's full object name for
/// this exact actor; <paramref name="Creature"/> is the row it currently holds ("Leyak"/"Krasue"),
/// or null when empty.</summary>
public sealed record LiveContainmentUnit(string Id, double X, double Y, double Z, int? Stability, string? Creature);

/// <summary>Every loaded containment unit plus whether this process has host authority to change them.</summary>
public sealed record LiveContainmentDirectory(IReadOnlyList<LiveContainmentUnit> Units, bool IsHost);
