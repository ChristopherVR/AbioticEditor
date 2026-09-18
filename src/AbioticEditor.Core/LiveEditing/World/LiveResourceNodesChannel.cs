namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live resource-node editing: the "Resource Nodes" feature's live twin (the same actors
/// <c>Core/WorldSaves/Features/ResourceNodeMapFeature.cs</c> edits in the save's
/// <c>ResourceNodeMap</c>). Lists every currently-loaded harvestable actor found by a hierarchy
/// sweep of <c>ResourceNode_ParentBP_C</c> (every current and future subclass, including the
/// <c>Resource_MicroNode_ParentBP_C</c> family, which itself derives from
/// <c>ResourceNode_ParentBP_C</c> - no class name hardcoded on this side or the Lua side) and lets
/// a host mark a node harvested/un-harvested and edit which in-game day it was picked up - see
/// <c>resourcenodes.list</c>/<c>resourcenodes.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/resourcenodes.lua</c> for the full
/// citations.
///
/// <para><b>Confirmed against the coordinator's own CUE4Parse class dump and
/// <c>ResourceNode_ParentBP_C</c>'s own blueprint bytecode</b> (<c>SaveNodeToWorldSave</c>'s own
/// code, and the shared ubergraph the <c>RespawnResourceNode</c>/<c>Force_DepleteNode</c> thin
/// wrappers jump into): <see cref="LiveResourceNode.Harvested"/> maps to the live <c>IsDepleted</c>
/// bool (replicated, <c>OnRep_IsDepleted</c>) - <c>SaveNodeToWorldSave</c>'s own bytecode reads
/// exactly this property before persisting a node, grounding the mapping directly rather than
/// guessing from the save leaf's name. <see cref="LiveResourceNode.DayPickedUp"/> maps to the live
/// <c>DayWasDepleted</c> int (plain, NOT replicated - confirmed from its own <c>PropertyFlags</c>
/// carrying no <c>Net</c> flag), which <c>SaveNodeToWorldSave</c>'s bytecode sets to
/// <c>DayNightManager.CurrentDay</c> at the moment it persists a depleted node.</para>
///
/// <para>Both harvested-state properties stay nullable because a specific actor can still fail to
/// read right now (an unfamiliar subclass, or one that unloaded mid-request) - null means "could
/// not read this off this actor just now", not "false"/"0". <see cref="SetHarvestedAsync"/> never
/// writes <c>IsDepleted</c> directly: it presses the node's own <c>RespawnResourceNode()</c>/
/// <c>Force_DepleteNode()</c> function (the game's own "make this visibly reappear/deplete" path,
/// preferred here per the owner's own instruction), and the Lua side re-reads <c>IsDepleted</c>
/// afterward to confirm it actually landed - a node whose class exposes no state, or whose call had
/// no confirmed effect (including the documented case where a valid <c>ContinualRespawnFlag</c>
/// world flag is set on that node, which reroutes <c>RespawnResourceNode</c> into depleting instead
/// of respawning it - see the Lua module's own header comment), fails with a named reason rather
/// than a false success.</para>
/// </summary>
public sealed class LiveResourceNodesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>
    /// Lists every currently-loaded resource node. <paramref name="classFilter"/> is an optional
    /// case-insensitive substring match against the actor's own class name (e.g. "GlassPane"),
    /// applied on the Lua side while sweeping - a performance affordance for a caller that only
    /// wants one harvestable type, since a single loaded area can carry well over a thousand nodes
    /// in the save (though only the ones actually streamed in near the player are ever found).
    /// Omitted (the default) lists everything, matching the file editor's own unfiltered view.
    /// </summary>
    public async Task<LiveResourceNodeDirectory> GetAsync(
        string? classFilter = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>(
            "resourcenodes.list",
            classFilter is { Length: > 0 } ? new RequestWire(classFilter) : null,
            cancellationToken).ConfigureAwait(false);
        var nodes = (wire.Nodes ?? [])
            .Select(n => new LiveResourceNode(n.Id, n.Label, n.Harvested, n.DayPickedUp, n.X, n.Y, n.Z))
            .ToList();
        return new LiveResourceNodeDirectory(nodes, wire.IsHost);
    }

    /// <summary>Presses the node's own reset function toward <paramref name="harvested"/> (see the
    /// class remarks) - never a bare property write.</summary>
    public Task SetHarvestedAsync(string id, bool harvested, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("resourcenodes.set",
            new SetWire([new EditWire(id, harvested, null)]), cancellationToken);

    /// <summary>Writes the in-game day this node was picked up directly (the live field is not
    /// replicated - see the class remarks).</summary>
    public Task SetDayPickedUpAsync(string id, int dayPickedUp, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("resourcenodes.set",
            new SetWire([new EditWire(id, null, dayPickedUp)]), cancellationToken);

    private sealed record RequestWire(string? ClassFilter);
    private sealed record DirectoryWire(IReadOnlyList<NodeWire>? Nodes, bool IsHost);
    private sealed record NodeWire(string Id, string Label, bool? Harvested, int? DayPickedUp, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Nodes);
    private sealed record EditWire(string Id, bool? Harvested, int? DayPickedUp);
}

/// <summary>One loaded resource-node actor of any class (see the discovery note above).
/// <paramref name="Id"/> is the game's full object name for this exact actor; <paramref
/// name="Label"/> is its real class name (fixed actors carry no friendly name live - the host
/// derives a readable type name from it, the same way the file editor's
/// <c>ResourceNodeNaming.FriendlyType</c> already does for the offline key). <paramref
/// name="Harvested"/>/<paramref name="DayPickedUp"/> are null when this instance's live state could
/// not be read just now, not "not harvested"/"day 0".</summary>
public sealed record LiveResourceNode(
    string Id, string Label, bool? Harvested, int? DayPickedUp, double X, double Y, double Z);

/// <summary>Every currently-loaded resource node plus whether this process has host authority to
/// change them.</summary>
public sealed record LiveResourceNodeDirectory(IReadOnlyList<LiveResourceNode> Nodes, bool IsHost);
