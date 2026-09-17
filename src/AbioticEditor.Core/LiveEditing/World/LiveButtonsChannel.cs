namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-button editing: the "Buttons" feature's live twin (the same actors
/// <c>Core/WorldSaves/Features/ButtonMapFeature.cs</c> edits in the save's <c>ButtonMap</c>).
/// Lists every loaded button actor found by a hierarchy sweep of <c>Button_Generic_C</c> (every
/// current and future subclass, no class name hardcoded on this side or the Lua side - see
/// <c>buttons.list</c>/<c>buttons.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua</c> for how that discovery
/// works and for the two look-alike classes confirmed NOT to belong here) and lets a host toggle
/// <c>enabled</c>/<c>activated</c>/<c>noReset</c>.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and Button_Generic_C's own
/// blueprint bytecode: <c>enabled</c>/<c>activated</c>/<c>noReset</c> map to real live properties
/// (<c>NOT ButtonDisabled</c>, <c>Activated</c>, <c>NoVignetteReset</c> respectively - see the Lua
/// module's own header comment for the full mapping and citations). <see cref="LiveButton"/>'s
/// four state properties stay nullable because a specific actor can still fail to read right now
/// (unloaded mid-request, a future game patch, etc.) - null means "could not read this property
/// off this button just now", not "false". <c>PressedOnce</c> is genuinely different: it is
/// readable (the save struct's own <c>ButtonHasBeenPressedOnce_</c> leaf) but not independently
/// settable at all - the game's own persistence call forces it <c>true</c> as an unconditional
/// side effect of saving any other field, with no live path found that clears it back to
/// <c>false</c>; <see cref="LiveButtonEdit.PressedOnce"/> exists for wire-shape symmetry only and
/// the Lua side always refuses a request that sets it (see <see cref="SetAsync"/>'s remarks).</para>
/// </summary>
public sealed class LiveButtonsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveButtonDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("buttons.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var buttons = (wire.Buttons ?? [])
            .Select(b => new LiveButton(b.Id, b.Label, b.Enabled, b.Activated, b.PressedOnce, b.NoReset, b.X, b.Y, b.Z))
            .ToList();
        return new LiveButtonDirectory(buttons, wire.IsHost);
    }

    /// <summary>Applies edits to one or more buttons (matched by <see cref="LiveButton.Id"/>)
    /// immediately. A null field in <paramref name="edits"/> is left untouched. A request that
    /// sets <see cref="LiveButtonEdit.PressedOnce"/> always fails by name (see the Lua module's
    /// own header comment for why); <c>LiveButtonsFeatureSession</c> (a different project) already
    /// rejects that case locally before ever reaching here, but this channel does not assume every
    /// caller does.</summary>
    public Task SetAsync(IReadOnlyList<LiveButtonEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("buttons.set",
            new SetWire(edits.Select(e => new EditWire(e.Id, e.Enabled, e.Activated, e.PressedOnce, e.NoReset)).ToList()),
            cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<ButtonWire>? Buttons, bool IsHost);
    private sealed record ButtonWire(string Id, string Label, bool? Enabled, bool? Activated, bool? PressedOnce,
        bool? NoReset, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Buttons);
    private sealed record EditWire(string Id, bool? Enabled, bool? Activated, bool? PressedOnce, bool? NoReset);
}

/// <summary>One loaded button actor. <paramref name="Id"/> is the game's full object name for
/// this exact actor; the four state fields are null when no live property could be located for
/// them (see the class remarks) rather than a guessed false.</summary>
public sealed record LiveButton(string Id, string Label, bool? Enabled, bool? Activated, bool? PressedOnce,
    bool? NoReset, double X, double Y, double Z);

/// <summary>Every loaded button plus whether this process has host authority to change them.</summary>
public sealed record LiveButtonDirectory(IReadOnlyList<LiveButton> Buttons, bool IsHost);

/// <summary>One button edit; a null field is left untouched.</summary>
public sealed record LiveButtonEdit(string Id, bool? Enabled = null, bool? Activated = null,
    bool? PressedOnce = null, bool? NoReset = null);
