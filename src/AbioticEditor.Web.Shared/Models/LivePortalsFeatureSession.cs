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
        Portals = directory.Portals;
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

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Portals = directory.Portals;
        IsHost = directory.IsHost;
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

        await _channel.SetActiveAsync(entryKey, wanted).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("world teleporters cannot be removed."));
}
