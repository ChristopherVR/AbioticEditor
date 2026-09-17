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
        Elevators = directory.Elevators;
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
        Elevators = directory.Elevators;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

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

        await _channel.SetTopOpenAsync(entryKey, wanted).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("elevators cannot be removed."));
}
