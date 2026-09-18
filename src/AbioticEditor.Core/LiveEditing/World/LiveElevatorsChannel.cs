namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live elevator-state editing: the live twin of <c>Core/WorldSaves/Features/ElevatorMapFeature.cs</c>
/// (the save's <c>ElevatorMap</c>, whose only persisted leaf is <c>TopOpen_&lt;hash&gt;</c> - whether
/// a fixed elevator platform is parked at its top stop). Lists every loaded elevator and lets a
/// host flip <c>topOpen</c> - see <c>elevators.list</c>/<c>elevators.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/elevators.lua</c>.
///
/// <para><b>Discovery is subclass-generic, not a hardcoded class list</b> (round 97): the Lua
/// side sweeps the parent class <c>Elevator_ParentBP_C</c> alone (UE4SS returns subclass
/// instances too, the same idiom already relied on for <c>NPC_Base_ParentBP_C</c>), so a new
/// elevator variant a future update adds is picked up with no code change here. An elevator type
/// this module cannot read state from still lists (with its real class name as its label) but is
/// reported <see cref="LiveElevator.Controllable"/> = false instead of erroring or being
/// dropped.</para>
///
/// <para><b>Corrected against a real class+bytecode probe</b>
/// (<c>tests/AbioticEditor.Probes/ElevatorButtonProbe.cs</c>). The original guess (a live
/// <c>TopOpen</c> bool property with an <c>OnRep_TopOpen</c>) was wrong: <c>Elevator_ParentBP_C</c>
/// has no such property. The real live state is a replicated byte enum,
/// <c>ElevatorCurrentMode</c> (<c>E_ElevatorMovementTypes</c>: 0 StoppedAtBottom, 1 StoppedAtTop,
/// 2 MovingToTop, 3 MovingToBottom). Confirmed from the blueprint's own bytecode:
/// <c>OnLoadedFromSave(Top: bool)</c> sets <c>ElevatorCurrentMode</c> to 1 when <c>Top</c> is true
/// and 0 otherwise, so the saved <c>TopOpen_</c> leaf means exactly "mode == StoppedAtTop" (the
/// save-time direction runs through <c>SaveElevatorStateToWorldSave</c> -&gt; the game mode's
/// <c>UpdateActorToWorldSave</c>, which was not itself traced; this is inferred by symmetry with
/// the confirmed load-time mapping, not independently confirmed). Movement is triggered with the
/// game's own <c>TryPressTopButton</c>/<c>TryPressBottomButton</c> functions (also confirmed from
/// bytecode - see the Lua module's header comment for the exact per-mode transitions, including a
/// toggle-away quirk when pressing the button for the side the elevator already occupies), gated
/// here by the confirmed <c>IsPowered</c>/<c>IsElevatorMoving</c> functions. Because moving the
/// platform takes real travel time, a write that starts or continues the correct direction is
/// accepted as success (not just an already-arrived read-back) - see
/// <see cref="LiveElevator.Moving"/>.</para>
/// </summary>
public sealed class LiveElevatorsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveElevatorDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("elevators.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var elevators = (wire.Elevators ?? [])
            .Select(e => new LiveElevator(e.Id, e.Label, e.Controllable, e.TopOpen, e.Moving, e.Powered, e.X, e.Y, e.Z))
            .ToList();
        return new LiveElevatorDirectory(elevators, wire.IsHost);
    }

    /// <summary>Calls the elevator with <paramref name="id"/> toward its top (true) or bottom
    /// (false) stop, through the game's own button functions.</summary>
    public Task SetTopOpenAsync(string id, bool topOpen, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("elevators.set",
            new SetWire([new EditWire(id, topOpen)]), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<ElevatorWire>? Elevators, bool IsHost);
    private sealed record ElevatorWire(string Id, string Label, bool Controllable, bool TopOpen, bool Moving, bool? Powered, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Elevators);
    private sealed record EditWire(string Id, bool? TopOpen);
}

/// <summary>One loaded elevator actor of any class (see the discovery note above). <paramref
/// name="Id"/> is the game's full object name for this exact actor; <paramref name="Label"/> is
/// its real class name (these fixed actors carry no friendly name, matching every other
/// fixed-actor feature in this mod). <paramref name="Controllable"/> is false when this instance
/// exposes no readable <c>ElevatorCurrentMode</c> (an elevator type this module does not
/// recognize) - <paramref name="TopOpen"/>/<paramref name="Moving"/> are both false in that case
/// and mean nothing. <paramref name="Moving"/> is true while the platform is travelling between
/// stops (<c>ElevatorCurrentMode</c> is MovingToTop or MovingToBottom) - <paramref
/// name="TopOpen"/> is false in that state too, since it is only true once actually parked at the
/// top. <paramref name="Powered"/> (round 125) is a bonus read-only field off the confirmed
/// <c>IsPowered()</c> function the game's own <c>elevators.set</c> already gates a move on - null
/// only means this particular actor's power state could not be read right now, not "unpowered".
/// Shown on the row so the player can see why a move might be refused before clicking, instead of
/// only finding out from the refusal afterward.</summary>
public sealed record LiveElevator(string Id, string Label, bool Controllable, bool TopOpen, bool Moving, bool? Powered, double X, double Y, double Z);

/// <summary>Every loaded elevator plus whether this process has host authority to change them.</summary>
public sealed record LiveElevatorDirectory(IReadOnlyList<LiveElevator> Elevators, bool IsHost);
