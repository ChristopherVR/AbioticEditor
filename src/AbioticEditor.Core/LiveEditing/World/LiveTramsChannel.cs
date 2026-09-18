namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live tram reading and recall: the "Trams" feature's live twin (the same actors
/// <c>Core/WorldSaves/Services/WorldMapFeatures/TramMapFeature.cs</c> edits in the save's
/// <c>TramMap</c>, Facility only). Lists every loaded tram actor found by a hierarchy sweep of
/// <c>Tram_ParentBP_C</c> - see <c>trams.list</c>/<c>trams.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/trams.lua</c> for the full discovery and
/// field-mapping citations.
///
/// <para><b>Reading is fully confirmed.</b> <see cref="LiveTram.PreviousStation"/> is confirmed, by
/// tracing <c>Tram_ParentBP_C</c>'s own arrival bytecode, to be exactly the offline save's
/// <c>LastStation_</c> leaf (on reaching a stop the graph sets <c>PreviousStation = TargetStation</c>
/// then immediately persists the actor).</para>
///
/// <para><b>Writing (round-103 follow-up): a real recall path was found, not guessed, but is not
/// fully bytecode-confirmed end to end.</b> A <c>TramSystem_RecallStation_C</c> actor (a leaf class,
/// props <c>LinkedTram</c>/<c>LinkedStation</c>, function <c>TramRecallPressed(Activated: bool)</c>)
/// exists per recall platform. <c>SetNextStopPoint(Positive, CurrentPoint)</c> on the tram itself is
/// fully traced this round and confirmed to set <c>TargetStation</c> from the rail's own
/// <c>GetNextStopPoint</c> and call <c>SetMoving(true)</c> - a genuine, working "start heading to
/// the next stop" call, though only one hop. <c>TramRecallPressed</c>'s own bytecode was NOT part of
/// this round's dump (only its property/function signature list), so exactly what it calls on
/// <c>LinkedTram</c> is inferred (almost certainly <c>SetNextStopPoint</c>, repeated as the tram
/// reaches each intermediate station - every station stop is confirmed to be a real, unconditional
/// full stop, so a distant recall is an asynchronous, multi-step journey, not a single atomic
/// teleport), not independently read off <c>TramRecallPressed</c> itself. <see
/// cref="SetTargetStationAsync"/> calls the game's own <c>TramRecallPressed(true)</c> on the
/// matching recall station and the Lua side gates on the *observed* outcome (tram now moving, or
/// already at the requested station) rather than asserting anything about the function's internals -
/// see the Lua module's own header comment for the full reasoning and what is still open for a
/// future round (dumping <c>TramSystem_RecallStation.json</c>/<c>TramSystem_Rail.json</c>).</para>
/// </summary>
public sealed class LiveTramsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveTramDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("trams.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var trams = (wire.Trams ?? [])
            .Select(t => new LiveTram(t.Id, t.Label, t.PreviousStation, t.TargetStation, t.Moving,
                t.PositiveDirection, t.IsAtStation, t.HasPassengers, t.Containers,
                t.RecallStations ?? [], t.X, t.Y, t.Z))
            .ToList();
        return new LiveTramDirectory(trams, wire.IsHost);
    }

    /// <summary>Recalls the tram with <paramref name="id"/> to <paramref name="targetStation"/>
    /// (one of its own <see cref="LiveTram.RecallStations"/> options) through the game's own linked
    /// <c>TramSystem_RecallStation_C</c>'s <c>TramRecallPressed(true)</c>. Throws
    /// <see cref="LiveAgentException"/> (via <see cref="ILiveGameChannel.RequestAsync{TResponse}"/>)
    /// when the tram is already moving, no recall station links this exact tram/station pair, or the
    /// press had no observable effect - see the class remarks.</summary>
    public Task SetTargetStationAsync(string id, string targetStation, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("trams.set",
            new SetWire([new EditWire(id, targetStation)]), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<TramWire>? Trams, bool IsHost);
    private sealed record TramWire(string Id, string Label, string? PreviousStation, string? TargetStation,
        bool? Moving, bool? PositiveDirection, bool? IsAtStation, bool? HasPassengers, int Containers,
        IReadOnlyList<string>? RecallStations, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Trams);
    private sealed record EditWire(string Id, string? TargetStation);
}

/// <summary>One loaded tram actor. <paramref name="Id"/> is the game's full object name for this
/// exact actor. <paramref name="PreviousStation"/>/<paramref name="TargetStation"/> are the
/// friendly station labels (matching <c>TramMapFeature</c>'s own <c>FriendlyStation</c> shape,
/// "PersistentLevel." stripped) - <paramref name="PreviousStation"/> is read-only (see the class
/// remarks); recalling the tram is done through <paramref name="RecallStations"/> instead of
/// writing it directly. <paramref name="RecallStations"/> is the set of friendly station labels a
/// real <c>TramSystem_RecallStation_C</c> actor links to this specific tram (empty when none do -
/// an honest, narrower set than the offline feature's "any station the save has ever referenced").
/// <paramref name="Containers"/> is the on-board storage count off the confirmed
/// <c>GetTramContainers()</c> function.</summary>
public sealed record LiveTram(string Id, string Label, string? PreviousStation, string? TargetStation,
    bool? Moving, bool? PositiveDirection, bool? IsAtStation, bool? HasPassengers, int Containers,
    IReadOnlyList<string> RecallStations, double X, double Y, double Z);

/// <summary>Every loaded tram plus whether this process has host authority to recall one.</summary>
public sealed record LiveTramDirectory(IReadOnlyList<LiveTram> Trams, bool IsHost);
