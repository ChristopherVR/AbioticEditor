using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s doors slice: implements the
/// same <see cref="IWorldDoorsSession"/> boundary the <c>WorldDoorsTab</c> widget already binds
/// to (see <c>IWorldDoorsSession.cs</c>), so that widget needs zero changes to work against a
/// running game instead of a loaded file. Every mutator applies to the running game immediately
/// (<see cref="AppliesImmediately"/> is true) and re-reads the door list afterwards, since the
/// world's own triggers (a story event, another player) can change a door at any moment - unlike
/// the file session there is no local "staged until Save" copy to trust instead.
/// </summary>
public sealed class LiveDoorsSession : IWorldDoorsSession
{
    private readonly LiveDoorsChannel _channel;

    private LiveDoorsSession(LiveDoorsChannel channel, LiveDoorDirectory directory)
    {
        _channel = channel;
        Doors = [];
        Apply(directory);
    }

    public static async Task<LiveDoorsSession> ConnectAsync(
        LiveDoorsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveDoorsSession(channel, directory);
    }

    public IReadOnlyList<WorldDoor> Doors { get; private set; }
    public bool CanEditDoors => Doors.Count > 0;
    /// <summary>Always false: quest/story flags are their own live area
    /// (<c>LiveWorldFlagsSession</c>), not reachable from here, so the door detail card's
    /// "already reached that story point" hint is left out live rather than guessed at.</summary>
    public bool CanEditFlags => false;
    public IReadOnlySet<string> Flags { get; } = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>Empty: there is no file. The tab still resolves each door's own region from its
    /// sub-level, which needs no file path.</summary>
    public string Path => string.Empty;
    public string? Status { get; private set; }
    public bool AppliesImmediately => true;
    public bool IsHost { get; private set; }

    /// <summary>Re-reads every loaded door from the running game, discarding nothing (there is
    /// nothing staged to discard).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Apply(directory);
    }

    public Task SetSimpleDoorState(string id, string rawState)
    {
        var state = DoorStateNames.TryParseIndex(rawState);
        return state is null ? Task.CompletedTask : ApplyEditAsync(id, LiveDoorKind.Simple, state: state);
    }

    public Task SetSecurityDoorOpen(string id, bool open) =>
        ApplyEditAsync(id, LiveDoorKind.Security, isOpen: open);

    public Task SetOneWayUnlocked(string id, bool unlocked) =>
        ApplyEditAsync(id, LiveDoorKind.Simple, oneWayUnlocked: unlocked);

    /// <summary>No-op live: "keep state / no auto-reset" only affects the save file's own
    /// session-restart logic, so there is nothing to send the game. The tab hides this control
    /// while <see cref="AppliesImmediately"/> is true, so this should not normally be reached.</summary>
    public Task SetDoorNoReset(string id, bool noReset) => Task.CompletedTask;

    private async Task ApplyEditAsync(string id, LiveDoorKind kind, int? state = null, bool? isOpen = null, bool? oneWayUnlocked = null)
    {
        await _channel.SetAsync([new LiveDoorEdit(id, kind, state, isOpen, oneWayUnlocked)]).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync().ConfigureAwait(false);
    }

    private void Apply(LiveDoorDirectory directory)
    {
        Doors = directory.Doors.Select(ToWorldDoor).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        IsHost = directory.IsHost;
    }

    /// <summary>
    /// Maps one live door reading onto the shared <see cref="WorldDoor"/> domain record so
    /// <c>WorldDoorsTab</c> can render it with no live-specific branching: the enum-string
    /// <see cref="WorldDoor.DoorState"/> the offline tab already parses (<c>DoorStateNames</c>),
    /// and the world position filled in directly instead of left for the tab's usual
    /// game-file lookup by actor id.
    /// </summary>
    private static WorldDoor ToWorldDoor(LiveDoor door)
    {
        var isSimple = door.Kind == LiveDoorKind.Simple;
        return new WorldDoor(
            door.Id,
            isSimple ? WorldDoorKind.Simple : WorldDoorKind.Security,
            isSimple ? $"E_DoorStates::NewEnumerator{door.State}" : null,
            null,
            isSimple ? door.OneWayUnlocked : null,
            isSimple ? null : door.IsOpen,
            null,
            door.X, door.Y, door.Z);
    }
}
