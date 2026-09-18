using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="PowerSocketsFeatureId"/> ("Power Sockets", <see cref="PowerSocketMapFeature"/>'s live
/// twin) - implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, the same shape <see cref="LiveButtonsFeatureSession"/>/
/// <see cref="LiveResourceNodesFeatureSession"/> use.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and
/// <c>PowerSocket_ParentBP_C</c>'s own blueprint bytecode - see <see cref="LivePowerSocketsChannel"/>
/// and the Lua module's own header comment
/// (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/powersockets.lua</c>) for the full
/// mapping and citations. Unlike every other live world area with a save-file counterpart, this
/// one has NO settable field at all: the offline feature's only editable leaf, <c>hasTimer</c>,
/// has no live-settable path - the game's own persistence call for this actor unconditionally
/// resets it (and <c>timerMode</c>) to false/0 every time it runs, so every field here renders
/// read-only regardless of whether its value is known.</para>
/// </summary>
public sealed class LivePowerSocketsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>PowerSocketMapFeature.Id</c> (Core/WorldSaves/Services/WorldMapFeatures/PowerSocketMapFeature.cs).</summary>
    public const string PowerSocketsFeatureId = "power-sockets";

    private readonly LivePowerSocketsChannel _channel;

    private LivePowerSocketsFeatureSession(LivePowerSocketsChannel channel, LivePowerSocketDirectory directory)
    {
        _channel = channel;
        Sockets = directory.Sockets;
        IsHost = directory.IsHost;
    }

    public static async Task<LivePowerSocketsFeatureSession> ConnectAsync(
        LivePowerSocketsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LivePowerSocketsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LivePowerSocket> Sockets { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: this session never stages an edit (nothing here is settable) - see
    /// <see cref="LiveButtonsFeatureSession.IsDirty"/> for the same reasoning on a settable area.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world.</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) - see LiveElevatorsFeatureSession's identical
    // remark. Power sockets are typically few per loaded area (unlike resource nodes, which are
    // deliberately excluded from this loop - see LiveResourceNodesFeatureSession), so periodic
    // refresh is safe here.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Sockets = directory.Sockets;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField ReadOnlyOrUnavailable(string id, string label, string? value, string hint)
        => WorldMapField.ReadOnly(id, label, value ?? "not available live",
            hint: value is not null
                ? hint
                : hint + " Could not read this off this socket right now (it may have just unloaded, "
                    + "or be an unfamiliar socket type).");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, PowerSocketsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Sockets.Select(s => new WorldMapEntry(
            s.Id,
            s.Label,
            new[]
            {
                ReadOnlyOrUnavailable("socketId", "Socket ID", s.SocketId,
                    "The socket's own persistent id (its GetPowerSocketID() value, the same id "
                        + "space the save file stores)."),
                WorldMapField.ReadOnly("pluggedInDevice", "Plugged-in device", s.PluggedInDevice ?? "nothing plugged in",
                    hint: "The real class of whatever is currently plugged into this socket, read "
                        + "from the live actor reference itself."),
                ReadOnlyOrUnavailable("hasTimer", "Timer armed", BoolText(s.HasTimer),
                    "Read-only live: the game's only save path for this socket unconditionally "
                        + "resets the timer state to off every time it runs, so nothing here can be "
                        + "armed and have it stick. Edit the save file directly instead."),
                ReadOnlyOrUnavailable("timerMode", "Timer mode", s.TimerMode?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Read-only live for the same reason as above. The raw stored value - the game "
                        + "has never given its timer modes meaningful names."),
                ReadOnlyOrUnavailable("powered", "Powered", BoolText(s.Powered),
                    "Whether this socket currently has power, read from the socket's own IsPowered() function."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            PowerSocketsFeatureId, "Power Sockets",
            "Power sockets currently loaded near you. Everything here is read-only live: the "
                + "game's own save logic always clears a socket's timer state when it persists, so "
                + "there is no live path to arm one that would survive - edit the save file directly "
                + "for that.",
            MapName: "PowerSocketMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    private static string? BoolText(bool? value) => value is { } known ? (known ? "true" : "false") : null;

    public Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, PowerSocketsFeatureId, StringComparison.Ordinal))
        {
            return Task.FromResult(WorldEditResult.Failure("this feature has no live equivalent."));
        }
        var current = Sockets.FirstOrDefault(s => string.Equals(s.Id, entryKey, StringComparison.Ordinal));
        if (current is null)
        {
            return Task.FromResult(WorldEditResult.Failure("power socket not found (it may have been unloaded or destroyed)."));
        }
        // Every field is read-only live - see the class remarks. Refused locally, never round-
        // tripped to the game, since the answer is always the same regardless of the actor.
        return Task.FromResult(WorldEditResult.Failure(
            $"'{fieldId}' cannot be changed live - power sockets have no settable field live (the "
                + "game's own save path always resets the timer state when it runs). Edit the save "
                + "file directly instead."));
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("power sockets cannot be removed live."));
}
