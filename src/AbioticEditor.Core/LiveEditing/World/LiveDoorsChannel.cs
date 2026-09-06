namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live door editing: lists every hinged (<c>SimpleDoor_ParentBP</c>) and security door
/// currently loaded, with its state and world position, and lets a host open, close, lock or
/// unlock them - see <c>doors.list</c>/<c>doors.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>. Door state numbers are the
/// game's <c>E_DoorStates</c> enumerators, the same ones the file editor's
/// <c>DoorStateNames</c> maps (0 closed, 1 open, 2 locked, ...).
/// </summary>
public sealed class LiveDoorsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveDoorDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("doors.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var doors = (wire.Doors ?? [])
            .Select(d => new LiveDoor(d.Id, d.Label,
                string.Equals(d.Kind, "security", StringComparison.OrdinalIgnoreCase) ? LiveDoorKind.Security : LiveDoorKind.Simple,
                d.State, d.IsOpen, d.OneWayUnlocked, d.Disabled, d.X, d.Y, d.Z))
            .ToList();
        return new LiveDoorDirectory(doors, wire.IsHost);
    }

    /// <summary>Applies edits to one or more doors (matched by <see cref="LiveDoor.Id"/>) immediately.</summary>
    public Task SetAsync(IReadOnlyList<LiveDoorEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("doors.set",
            new SetWire(edits.Select(e => new EditWire(e.Id,
                e.Kind == LiveDoorKind.Security ? "security" : "simple",
                e.State, e.IsOpen, e.OneWayUnlocked, e.Disabled)).ToList()),
            cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<DoorWire>? Doors, bool IsHost);
    private sealed record DoorWire(string Id, string Label, string Kind, int State, bool IsOpen,
        bool OneWayUnlocked, bool Disabled, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Doors);
    private sealed record EditWire(string Id, string Kind, int? State, bool? IsOpen, bool? OneWayUnlocked, bool? Disabled);
}

public enum LiveDoorKind
{
    /// <summary>A hinged door with the full <c>E_DoorStates</c> state machine.</summary>
    Simple,
    /// <summary>A sliding security door that is simply open or closed.</summary>
    Security,
}

/// <summary>One loaded door. <paramref name="Id"/> is the game's full object name for this
/// exact actor; <paramref name="Label"/> is its class name (e.g. <c>SimpleDoor_ParentBP_C</c>).</summary>
public sealed record LiveDoor(string Id, string Label, LiveDoorKind Kind, int State, bool IsOpen,
    bool OneWayUnlocked, bool Disabled, double X, double Y, double Z);

/// <summary>Every loaded door plus whether this process has host authority to change them.</summary>
public sealed record LiveDoorDirectory(IReadOnlyList<LiveDoor> Doors, bool IsHost);

/// <summary>One door edit; a null field is left untouched. <paramref name="State"/> applies to
/// hinged doors, <paramref name="IsOpen"/> to security doors.</summary>
public sealed record LiveDoorEdit(string Id, LiveDoorKind Kind, int? State = null, bool? IsOpen = null,
    bool? OneWayUnlocked = null, bool? Disabled = null);
