using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s containment slice: implements
/// the same <see cref="IWorldContainmentSession"/> boundary <c>WorldContainmentTab</c> already
/// binds to, so that widget needs no changes to work against a running game instead of a loaded
/// save. Like <see cref="LiveNpcSession"/>/<see cref="LiveContainersSession"/>, edits apply one
/// unit at a time, immediately, then the roster is re-read - a containment unit's occupant is
/// server-authoritative state, not something to stage locally and hope still matches by the time
/// SAVE is clicked.
/// </summary>
public sealed class LiveContainmentSession : IWorldContainmentSession
{
    private readonly LiveContainmentChannel _channel;

    private LiveContainmentSession(LiveContainmentChannel channel, LiveContainmentDirectory directory)
    {
        _channel = channel;
        Units = directory.Units;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveContainmentSession> ConnectAsync(
        LiveContainmentChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveContainmentSession(channel, directory);
    }

    public IReadOnlyList<LiveContainmentUnit> Units { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    bool IWorldContainmentSession.ContainmentUnitsLoaded => true;

    public IReadOnlyList<WorldContainmentUnit> ContainmentUnits => Units
        .Select(u => new WorldContainmentUnit(u.Id, RegionSaveFileName: string.Empty, u.X, u.Y, u.Z,
            u.Stability, StoredCreatureIndex: null, u.Creature))
        .ToArray();

    /// <summary>Nothing to fail to read live: the running game answers or the request throws.</summary>
    public IReadOnlyList<string> ContainmentScanFailures => [];

    public IReadOnlyList<KeyValuePair<string, string>> Containments => Units
        .Where(u => u.Creature is not null)
        .Select(u => new KeyValuePair<string, string>(u.Creature!, u.Id))
        .ToArray();

    /// <summary>A live unit is always the actual unit it claims to be; nothing can orphan here.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> OrphanedContainments => [];

    bool IWorldContainmentSession.AppliesImmediately => true;

    public Task LoadContainmentUnitsAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    public string? CreatureInUnit(string unitId)
        => Units.FirstOrDefault(u => string.Equals(u.Id, unitId, StringComparison.Ordinal))?.Creature;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Units = directory.Units;
        IsHost = directory.IsHost;
    }

    public async Task SetContainmentUnitOccupantAsync(string unitId, string? creature, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(creature))
        {
            // Emptying a unit through the "holds" picker means releasing whatever it holds now.
            if (CreatureInUnit(unitId) is { } occupant) await ReleaseContainmentAsync(occupant, cancellationToken).ConfigureAwait(false);
            return;
        }
        await _channel.AssignAsync(unitId, creature, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SwapContainmentUnitsAsync(string unitIdA, string unitIdB, CancellationToken cancellationToken = default)
    {
        await _channel.SwapAsync(unitIdA, unitIdB, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseContainmentAsync(string creature, CancellationToken cancellationToken = default)
    {
        await _channel.ReleaseAsync(creature, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
