namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live breakable-object editing: the "Breakable Objects" feature's live twin (the same actors
/// <c>Core/WorldSaves/Features/DestructibleMapFeature.cs</c> edits in the save's
/// <c>DestructibleMap</c>). Lists every loaded destructible actor found by a hierarchy sweep of
/// <c>Abiotic_GenericDestructible_BP_C</c> (every current and future subclass, no class name
/// hardcoded on this side or the Lua side - see <c>destructibles.list</c>/<c>destructibles.set</c>
/// in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/destructibles.lua</c> for how that
/// discovery works) and lets a host break one.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and
/// Abiotic_GenericDestructible_BP_C's own blueprint bytecode: <c>broken</c> maps to the real live
/// property <c>actor.Broken</c> (direct, replicated, RepNotify <c>OnRep_Broken</c> - see the Lua
/// module's own header comment for the full mapping and citations). <see cref="LiveDestructible.Broken"/>
/// stays nullable because a specific actor can still fail to read right now (unloaded mid-request,
/// a future game patch, etc.) - null means "could not read this property off this actor just now",
/// not "false".</para>
///
/// <para><b>Repair has no live path, confirmed not assumed.</b> <c>OnRep_Broken</c>'s own bytecode
/// starts with an unconditional "if not Broken then return" - there is no branch at all for the
/// false case, and nothing else in the class re-enables the intact mesh's collision/visibility or
/// disables the destroyed mesh's once <c>SetStateBroken</c> has run. Setting <see
/// cref="LiveDestructibleEdit.Broken"/> to <c>false</c> is refused by the Lua side by name rather
/// than performed as a silent no-op or a lying success (see <see cref="SetAsync"/>'s remarks) -
/// <c>LiveDestructiblesFeatureSession</c> (a different project) already rejects that case
/// locally before ever reaching here, but this channel does not assume every caller does.</para>
/// </summary>
public sealed class LiveDestructiblesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveDestructibleDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("destructibles.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var destructibles = (wire.Destructibles ?? [])
            .Select(d => new LiveDestructible(d.Id, d.Label, d.Broken, d.X, d.Y, d.Z))
            .ToList();
        return new LiveDestructibleDirectory(destructibles, wire.IsHost);
    }

    /// <summary>Applies edits to one or more destructibles (matched by
    /// <see cref="LiveDestructible.Id"/>) immediately. A null field in <paramref name="edits"/> is
    /// left untouched. A request that sets <see cref="LiveDestructibleEdit.Broken"/> to
    /// <c>false</c> always fails by name (see the class remarks for why); the feature session
    /// already rejects that case locally before ever reaching here, but this channel does not
    /// assume every caller does.</summary>
    public Task SetAsync(IReadOnlyList<LiveDestructibleEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("destructibles.set",
            new SetWire(edits.Select(e => new EditWire(e.Id, e.Broken)).ToList()), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<DestructibleWire>? Destructibles, bool IsHost);
    private sealed record DestructibleWire(string Id, string Label, bool? Broken, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Destructibles);
    private sealed record EditWire(string Id, bool? Broken);
}

/// <summary>One loaded destructible actor. <paramref name="Id"/> is the game's full object name
/// for this exact actor; <paramref name="Broken"/> is null when no live <c>Broken</c> property
/// could be located for it right now (see the class remarks) rather than a guessed false.</summary>
public sealed record LiveDestructible(string Id, string Label, bool? Broken, double X, double Y, double Z);

/// <summary>Every loaded destructible plus whether this process has host authority to change them.</summary>
public sealed record LiveDestructibleDirectory(IReadOnlyList<LiveDestructible> Destructibles, bool IsHost);

/// <summary>One destructible edit; a null field is left untouched. Setting <see cref="Broken"/> to
/// <c>false</c> is always refused live (see <see cref="LiveDestructiblesChannel"/>'s remarks).</summary>
public sealed record LiveDestructibleEdit(string Id, bool? Broken = null);
