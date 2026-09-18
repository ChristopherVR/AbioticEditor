using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="TramsFeatureId"/> ("Trams", <see cref="TramMapFeature"/>'s live twin, Facility only) -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c> already
/// binds to.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and <c>Tram_ParentBP_C</c>'s own
/// blueprint bytecode - see <see cref="LiveTramsChannel"/> and the Lua module's own header comment
/// (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/trams.lua</c>) for the full mapping,
/// citations, and exactly what is/isn't independently bytecode-confirmed. Round-103 follow-up: the
/// offline feature's <c>lastStation</c> leaf now HAS a real live write path where a
/// <c>TramSystem_RecallStation_C</c> actor links this specific tram to a specific station -
/// rendered as an editable <see cref="WorldMapField.Choice"/> over <see
/// cref="LiveTram.RecallStations"/> when that list is non-empty, and read-only (no known recall
/// path for this particular tram) otherwise - the same per-instance degradation idiom
/// <see cref="LiveResourceNodesFeatureSession"/> already uses for a field that isn't always
/// resolvable.</para>
/// </summary>
public sealed class LiveTramsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>TramMapFeature.Id</c> (Core/WorldSaves/Services/WorldMapFeatures/TramMapFeature.cs).</summary>
    public const string TramsFeatureId = "trams";

    private readonly LiveTramsChannel _channel;

    private LiveTramsFeatureSession(LiveTramsChannel channel, LiveTramDirectory directory)
    {
        _channel = channel;
        Trams = directory.Trams;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveTramsFeatureSession> ConnectAsync(
        LiveTramsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveTramsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveTram> Trams { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: a recall already reached the running game by the time it returns, so
    /// there is never a client-side staged copy - see <see cref="LiveButtonsFeatureSession.IsDirty"/>
    /// for the same reasoning on another settable area.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) - see LiveElevatorsFeatureSession's identical
    // remark. Trams are few per facility, so periodic refresh is safe here.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Trams = directory.Trams;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static string TextOrUnavailable(string? value)
        => value ?? "not available live";

    private static string BoolTextOrUnavailable(bool? value)
        => value is { } known ? (known ? "true" : "false") : "not available live";

    /// <summary>"Last station" is editable only when this specific tram has at least one linked
    /// recall station (<see cref="LiveTram.RecallStations"/>) - an honest, per-tram degradation:
    /// the offline feature can pick any station the save has ever referenced, live can only recall
    /// to a station a real placed <c>TramSystem_RecallStation_C</c> actually links to this tram.</summary>
    private static WorldMapField LastStationField(LiveTram t)
        => t.RecallStations.Count > 0
            ? WorldMapField.Choice("lastStation", "Recall to station", t.PreviousStation, t.RecallStations,
                hint: "Sends this tram toward the chosen station through its own linked recall "
                    + "station (the game's own TramRecallPressed function) - only stations a real "
                    + "recall station links to THIS tram are offered. Refused while the tram is "
                    + "already moving. A distant station can take real travel time and multiple "
                    + "stops to reach; a change that starts the tram moving the right way is "
                    + "accepted, not only an already-arrived state.")
            : WorldMapField.ReadOnly("lastStation", "Last station", TextOrUnavailable(t.PreviousStation),
                hint: "The station this tram last parked at. Read-only for this specific tram: no "
                    + "recall station in the loaded area links to it right now - edit the save file "
                    + "directly to re-park it at an arbitrary station.");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, TramsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Trams.Select(t => new WorldMapEntry(
            t.Id,
            t.Label,
            new[]
            {
                LastStationField(t),
                WorldMapField.ReadOnly("targetStation", "Heading to", TextOrUnavailable(t.TargetStation),
                    hint: "The station this tram is currently travelling toward, or sitting at if not moving."),
                WorldMapField.ReadOnly("moving", "Moving", BoolTextOrUnavailable(t.Moving),
                    hint: "true while the tram is travelling between stations."),
                WorldMapField.ReadOnly("isAtStation", "At station", BoolTextOrUnavailable(t.IsAtStation),
                    hint: "true while the tram is stopped at a station."),
                WorldMapField.ReadOnly("hasPassengers", "Has passengers", BoolTextOrUnavailable(t.HasPassengers),
                    hint: "true while a player is aboard this tram."),
                WorldMapField.ReadOnly("inventories", "Container inventories",
                    t.Containers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hint: "Number of on-board storage containers attached to this tram, read from GetTramContainers()."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            TramsFeatureId, "Trams",
            "Trams currently loaded near you. Recall a tram to a station through its own linked "
                + "recall station when one is linked; everything else is read-only.",
            MapName: "TramMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, TramsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (!string.Equals(fieldId, "lastStation", StringComparison.Ordinal))
        {
            return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        }

        var current = Trams.FirstOrDefault(t => string.Equals(t.Id, entryKey, StringComparison.Ordinal));
        if (current is null)
        {
            return WorldEditResult.Failure("tram not found (it may have been unloaded or destroyed).");
        }
        if (current.RecallStations.Count == 0)
        {
            return WorldEditResult.Failure("no recall station links this tram to any station live.");
        }
        if (string.IsNullOrWhiteSpace(value) || !current.RecallStations.Contains(value, StringComparer.Ordinal))
        {
            return WorldEditResult.Failure($"'{value}' is not a station this tram can be recalled to " +
                $"(available: {string.Join(", ", current.RecallStations)}).");
        }
        if (string.Equals(current.PreviousStation, value, StringComparison.Ordinal) && current.Moving != true)
        {
            return WorldEditResult.NoChange;
        }

        try
        {
            await _channel.SetTargetStationAsync(entryKey, value).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when the tram is already moving, no
            // recall station links this pair, or the press had no observable effect (see
            // trams.lua's own header comment) - surface it as-is rather than a generic failure.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("trams cannot be removed live."));
}
