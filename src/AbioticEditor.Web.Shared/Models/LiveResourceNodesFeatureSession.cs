using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="ResourceNodesFeatureId"/> ("Resource Nodes", <see cref="ResourceNodeMapFeature"/>'s
/// live twin) - implements the same <see cref="IWorldFeaturesSession"/> boundary
/// <c>WorldFeaturesTab</c> already binds to, the same shape <see cref="LiveElevatorsFeatureSession"/>/
/// <see cref="LiveButtonsFeatureSession"/> use.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and
/// <c>ResourceNode_ParentBP_C</c>'s own blueprint bytecode - see <see
/// cref="LiveResourceNodesChannel"/> and the Lua module's own header comment
/// (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/resourcenodes.lua</c>) for the full
/// mapping and citations. <c>harvested</c>/<c>dayPickedUp</c> are real, independently settable live
/// state (<c>IsDepleted</c> via the node's own <c>RespawnResourceNode()</c>/
/// <c>Force_DepleteNode()</c>, <c>DayWasDepleted</c> via a direct write since it is not
/// replicated); a null value only means this particular actor's live state could not be read right
/// now (renders as a read-only "not available live" row rather than an editable field).</para>
///
/// <para><b>Removal is refused live</b> (unlike the offline feature, which supports it): offline
/// "remove" drops the whole <c>ResourceNodeMap</c> entry so the game recreates the actor at its
/// blueprint default on next load - a different, broader action than anything the live game
/// exposes a confirmed function for (<c>RespawnResourceNode</c> only clears the harvested flag and
/// re-places the existing actor on the ground; it does not reset position or any other persisted
/// state), so mapping "remove" onto it here would overstate what actually happens.</para>
/// </summary>
public sealed class LiveResourceNodesFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>ResourceNodeMapFeature.Id</c> (Core/WorldSaves/Features/ResourceNodeMapFeature.cs).</summary>
    public const string ResourceNodesFeatureId = "resource-nodes";

    private readonly LiveResourceNodesChannel _channel;

    private LiveResourceNodesFeatureSession(LiveResourceNodesChannel channel, LiveResourceNodeDirectory directory)
    {
        _channel = channel;
        Nodes = directory.Nodes;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveResourceNodesFeatureSession> ConnectAsync(
        LiveResourceNodesChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new LiveResourceNodesFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveResourceNode> Nodes { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: an edit already reached the running game by the time it returns, so
    /// there is never a client-side staged copy - see <see cref="LiveElevatorsFeatureSession.IsDirty"/>.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // Deliberately NOT a zero-arg overload discovered by LiveConnect's periodic refresh loop (see
    // LivePortalsFeatureSession/LiveElevatorsFeatureSession for that convention) - this feature is
    // excluded from that loop on purpose (see LiveConnect.razor's ActiveLiveSessions switch): a
    // single loaded area can carry far more resource nodes than any other live world area, so only
    // an explicit tab visit or REFRESH re-runs the sweep, matching containers/npcs/bases.
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        Nodes = directory.Nodes;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField BoolOrUnavailable(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.Bool(id, label, known, hint: hint + " Applies live immediately.")
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this resource node right now (it may have just "
                    + "unloaded, or be an unfamiliar node type). Try again after the next refresh, "
                    + "or edit it in the save file instead.");

    private static WorldMapField IntOrUnavailable(string id, string label, int? value, string hint)
        => value is { } known
            ? WorldMapField.Integer(id, label, known, hint: hint + " Applies live immediately.")
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this resource node right now (it may have just "
                    + "unloaded, or be an unfamiliar node type). Try again after the next refresh, "
                    + "or edit it in the save file instead.");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, ResourceNodesFeatureId, StringComparison.Ordinal)) return null;
        var entries = Nodes.Select(n => new WorldMapEntry(
            n.Id,
            ResourceNodeNaming.FriendlyType(n.Id),
            new[]
            {
                WorldMapField.ReadOnly("position", "Position",
                    FormattableString.Invariant($"{n.X:0.###}, {n.Y:0.###}, {n.Z:0.###}"),
                    hint: "Where this node sits in the world right now (x, y, z). Read-only."),
                BoolOrUnavailable("harvested", "Harvested", n.Harvested,
                    "true = depleted (waiting to regrow); false = available to harvest right now. "
                        + "Setting this presses the node's own respawn/deplete function, so a node "
                        + "with an in-progress special effect (a portal-style vanish, on the rare "
                        + "node configured to work that way) may not respond exactly as asked - a "
                        + "change that could not be confirmed is refused with a named reason rather "
                        + "than reported as done."),
                IntOrUnavailable("dayPickedUp", "Day picked up", n.DayPickedUp,
                    "The in-game day this node was last harvested; the game measures regrowth from "
                        + "this day. 0 means it has not been picked up yet."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            ResourceNodesFeatureId, "Resource Nodes",
            "Harvestable spots currently loaded near you (ore veins, mineable rocks, gatherable "
                + "plants, and similar). Set a node back to un-harvested to make it available now, "
                + "or adjust the day it was picked up.",
            MapName: "ResourceNodeMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, ResourceNodesFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (string.Equals(fieldId, "position", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure("'position' cannot be changed live.");
        }

        var current = Nodes.FirstOrDefault(n => string.Equals(n.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("resource node not found (it may have been unloaded or destroyed).");

        try
        {
            if (string.Equals(fieldId, "harvested", StringComparison.OrdinalIgnoreCase))
            {
                if (!WorldMapAccessor.TryParseBool(value, out var wanted))
                {
                    return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
                }
                // A null current value means "could not read this right now" (see the class
                // remarks), not "false" - it never matches wanted, so the edit is still attempted
                // rather than silently reported as NoChange.
                if (current.Harvested == wanted) return WorldEditResult.NoChange;

                await _channel.SetHarvestedAsync(entryKey, wanted).ConfigureAwait(false);
            }
            else if (string.Equals(fieldId, "dayPickedUp", StringComparison.OrdinalIgnoreCase))
            {
                if (!WorldMapAccessor.TryParseInt(value, out var wanted))
                {
                    return WorldEditResult.Failure($"'{value}' is not a valid integer.");
                }
                if (current.DayPickedUp == wanted) return WorldEditResult.NoChange;

                await _channel.SetDayPickedUpAsync(entryKey, wanted).ConfigureAwait(false);
            }
            else
            {
                return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
            }
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when it could not confirm the
            // change landed (see resourcenodes.lua's own header comment) - surface it as-is rather
            // than a generic failure.
            return WorldEditResult.Failure(ex.Message);
        }

        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("resource nodes cannot be removed live (edit the save file instead)."));
}
