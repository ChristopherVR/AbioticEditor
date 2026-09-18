using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, but only for <see cref="ElevatorsFeatureId"/> ("Elevators": every loaded
/// elevator actor, whatever its concrete class - see <see cref="LiveElevatorsChannel"/> for the
/// subclass-generic discovery). Mirrors <see cref="LivePortalsFeatureSession"/>'s shape; every
/// other feature id still returns null here, exactly like a file session with that map absent.
/// </summary>
public sealed class LiveElevatorsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>ElevatorMapFeature.Id</c> (Core/WorldSaves/Features/ElevatorMapFeature.cs).</summary>
    public const string ElevatorsFeatureId = "elevators";

    private readonly LiveElevatorsChannel _channel;

    private LiveElevatorsFeatureSession(LiveElevatorsChannel channel, LiveElevatorDirectory directory)
    {
        _channel = channel;
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though elevators.lua already keys every row by the actor's own unique full name.
        Elevators = LiveFeatureRows.DistinctById(directory.Elevators, e => e.Id);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveElevatorsFeatureSession> ConnectAsync(
        LiveElevatorsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveElevatorsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveElevator> Elevators { get; private set; }
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
    // see LivePortalsFeatureSession for the identical requirement.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though elevators.lua already keys every row by the actor's own unique full name.
        Elevators = LiveFeatureRows.DistinctById(directory.Elevators, e => e.Id);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static string PoweredText(bool? powered) => powered is { } known ? (known ? "true" : "false") : "not available live";

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, ElevatorsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Elevators.Select(e => new WorldMapEntry(
            e.Id,
            e.Label,
            e.Controllable
                ? new[]
                {
                    WorldMapField.Bool("topOpen", "At top stop", e.TopOpen,
                        hint: "true = elevator parked at the top, false = at the bottom or still travelling. "
                            + "Setting this presses the elevator's own top/bottom call button (see "
                            + "LiveElevatorsChannel), so moving between stops takes real time - a change that "
                            + "starts or continues the platform moving the right way is accepted, not only an "
                            + "already-arrived state. Refused with a named reason if the elevator is already "
                            + "moving or not powered."),
                    WorldMapField.ReadOnly("moving", "Moving", e.Moving ? "true" : "false",
                        hint: "true while the platform is travelling between stops."),
                    // Round 125: shown so the player can see why a move might be refused (the
                    // game's own elevators.set already gates a press on this, see IsPowered()
                    // below) before clicking, not only from the refusal toast afterward.
                    WorldMapField.ReadOnly("powered", "Powered", PoweredText(e.Powered),
                        hint: "Whether this elevator currently has power, read from the "
                            + "elevator's own IsPowered() function. Moving a platform with no "
                            + "power is refused."),
                }
                : new[]
                {
                    // An elevator class this module does not recognize (see the discovery note
                    // on LiveElevatorsChannel): still listed, with its real class name as its
                    // label above, but nothing is known to read or set - not an error, not
                    // dropped from the list.
                    WorldMapField.ReadOnly("topOpen", "At top stop",
                        value: null, hint: "not controllable live (unrecognized elevator type)."),
                })).ToArray();
        return new WorldMapFeatureSnapshot(
            ElevatorsFeatureId, "Elevators",
            "Fixed elevator platforms: set whether each is parked at its top stop.",
            MapName: "ElevatorMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, ElevatorsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (!string.Equals(fieldId, "topOpen", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        }
        if (!bool.TryParse(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = Elevators.FirstOrDefault(e => string.Equals(e.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("elevator not found (it may have been unloaded).");
        if (!current.Controllable)
        {
            return WorldEditResult.Failure("this elevator type is not controllable live (unrecognized class).");
        }
        if (current.TopOpen == wanted) return WorldEditResult.NoChange;

        try
        {
            await _channel.SetTopOpenAsync(entryKey, wanted).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // Round 125: this used to let the Lua side's own player-safe reason ("elevator is not
            // powered", "elevator is currently moving", ...) escape as an uncaught exception
            // instead of a WorldEditResult.Failure - every sibling Live*FeatureSession (buttons,
            // npcspawns, triggers, destructibles, resourcenodes, trams) already catches this
            // exact exception here; elevators was the one area that did not. The uncaught
            // exception meant WorldFeaturesTab.SetFieldAsync's own error handling and
            // RefreshSnapshot() call were both skipped, so a refused toggle was never reverted
            // and kept re-sending on the next periodic refresh (see docs/PROGRESS.md's Round-125
            // entry for the observed retry storm) - surfacing it as a Failure result here, like
            // every sibling area already does, is the actual fix.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("elevators cannot be removed."));
}
