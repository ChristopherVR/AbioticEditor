using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, but only for <see cref="PortalsFeatureId"/> ("World Teleporters" pads,
/// <c>BP_Teleporter_ParentBP_C</c>): that is the one feature with an evidenced live UObject path
/// (see <see cref="LivePortalsChannel"/>). Every other feature id (power sockets, resource nodes,
/// buttons, elevators, trams, triggers, entitlements, ...) has no live equivalent and
/// <see cref="MapFeature"/> returns null for it, exactly like a file session with that map absent.
/// </summary>
public sealed class LivePortalsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>PortalMapFeature.Id</c> (Core/WorldSaves/Features/PortalMapFeature.cs).</summary>
    public const string PortalsFeatureId = "portals";

    private readonly LivePortalsChannel _channel;

    private LivePortalsFeatureSession(LivePortalsChannel channel, LivePortalDirectory directory)
    {
        _channel = channel;
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though portals.lua already keys every row by the actor's own unique full name.
        Portals = LiveFeatureRows.DistinctById(directory.Portals, p => p.Id);
        IsHost = directory.IsHost;
    }

    public static async Task<LivePortalsFeatureSession> ConnectAsync(
        LivePortalsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LivePortalsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LivePortal> Portals { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: a toggle already reached the running game by the time it
    /// returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a portal changed in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though portals.lua already keys every row by the actor's own unique full name.
        Portals = LiveFeatureRows.DistinctById(directory.Portals, p => p.Id);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, PortalsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Portals.Select(p => new WorldMapEntry(
            p.Id,
            p.Label,
            new[]
            {
                WorldMapField.Bool("active", "Active", p.Active,
                    hint: "true = teleporter pad activated/usable, false = inactive. Applies live immediately."),
                WorldMapField.ReadOnly("teleporterId", "Teleporter Id", p.TeleporterId),
                WorldMapField.ReadOnly("destinationId", "Destination Id", p.DestinationId),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            PortalsFeatureId, "World Teleporters",
            "Fixed in-level teleporters: toggle whether each is active (unlocked/usable).",
            MapName: "PortalMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, PortalsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (!string.Equals(fieldId, "active", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        }
        if (!bool.TryParse(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = Portals.FirstOrDefault(p => string.Equals(p.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("teleporter not found (it may have been unloaded).");
        if (current.Active == wanted) return WorldEditResult.NoChange;

        try
        {
            await _channel.SetActiveAsync(entryKey, wanted).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // Round 125: same gap the elevators area had (see LiveElevatorsFeatureSession's
            // identical remark) - an uncaught exception here skips WorldFeaturesTab.SetFieldAsync's
            // error handling and revert entirely, letting a refused toggle keep re-sending on the
            // next periodic refresh instead of failing once and snapping back.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("world teleporters cannot be removed."));
}
