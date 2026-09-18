namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-trigger editing: the live twin of <c>Core/WorldSaves/Features/TriggerMapFeature.cs</c>
/// (the save's <c>TriggerMap</c>, whose leaves are <c>UniqueTriggerID_</c>/<c>TimesTriggered_</c>).
/// Lists every loaded scripted trigger volume and lets a host set its fire count or reset it - see
/// <c>triggers.list</c>/<c>triggers.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/triggers.lua</c> for the full field
/// mapping and citations (round 102, grounded in a real CUE4Parse class+bytecode dump).
///
/// <para><b>Rows are identified by <see cref="LiveTrigger.Id"/> = the trigger's own
/// <c>UniqueTriggerID</c> string</b> (e.g. <c>WF_NewGameStarted</c>), NOT the actor's
/// <c>GetFullName()</c> the way every other live world area in this project keys its rows - this
/// is deliberate: the save file's own <c>TriggerMap</c> is keyed by that exact id, confirmed
/// directly off <c>Abiotic_TriggerVolume_ParentBP_C</c>'s own declared, unsuffixed
/// <c>UniqueTriggerID</c> property (<c>FNameProperty</c>, set per placed instance in the level).
/// The original task brief guessed the live counts might live in a single map on the game mode or
/// game state - checked and wrong: <c>Abiotic_WorldSave_C</c> does carry its own <c>TriggerMap</c>
/// (confirmed in the dump), but each placed trigger actor keeps and persists its own entry
/// directly (see <c>SaveTriggerData</c>/<c>ResetTriggerState</c> below); there is no separate
/// live map object to read or write through.</para>
///
/// <para><b>Class discovery</b> sweeps one confirmed root, <c>Abiotic_TriggerVolume_ParentBP_C</c>
/// (the dump's own example subclass, <c>Trigger_CompendiumExploration_C</c>, chains to it
/// directly) - FindAllOf is hierarchy-inclusive, so every one of the offline feature's ~17
/// <c>Trigger_*</c> classes is expected to come back from that single sweep with no leaf-class
/// list, though the probe's package set only happened to include the one confirmed example.</para>
///
/// <para><b>Field mapping</b>, read straight off the class's own declared properties and its
/// <c>ResetTriggerState()</c>/<c>SaveTriggerData()</c> bytecode: <see cref="LiveTrigger.TimesTriggered"/>
/// is the direct <c>TimesTriggered</c> property; <see cref="LiveTrigger.HasBeenTriggeredOnce"/> and
/// <see cref="LiveTrigger.TriggerLimit"/> are informational reads of the matching direct
/// properties. <see cref="SetTimesTriggeredAsync"/> writes <c>TimesTriggered</c> directly then
/// calls the trigger's own real, no-argument, actor-level <c>SaveTriggerData()</c> to persist it -
/// the same persistence call <c>ResetTriggerState()</c> itself calls internally.
/// <see cref="ResetAsync"/> instead calls the trigger's own real, no-argument
/// <c>ResetTriggerState()</c>, confirmed from its own bytecode to also clear
/// <see cref="LiveTrigger.HasBeenTriggeredOnce"/> and re-arm the volume's collision/overlap
/// state - a strictly more complete reset than a bare <c>TimesTriggered=0</c> write, so this is
/// preferred whenever a full reset (not an arbitrary count) is wanted.</para>
/// </summary>
public sealed class LiveTriggersChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveTriggerDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("triggers.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var triggers = (wire.Triggers ?? [])
            .Select(t => new LiveTrigger(t.Id, t.Label, t.TimesTriggered, t.HasBeenTriggeredOnce, t.TriggerLimit, t.X, t.Y, t.Z))
            .ToList();
        return new LiveTriggerDirectory(triggers, wire.IsHost);
    }

    /// <summary>Writes an arbitrary fire count directly, then persists it (the game's own
    /// <c>SaveTriggerData()</c>). Does not touch <see cref="LiveTrigger.HasBeenTriggeredOnce"/>.</summary>
    public Task SetTimesTriggeredAsync(string id, int timesTriggered, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("triggers.set",
            new SetWire([new EditWire(id, timesTriggered, Reset: null)]), cancellationToken);

    /// <summary>Fully resets one trigger via the game's own <c>ResetTriggerState()</c> - clears
    /// both <see cref="LiveTrigger.TimesTriggered"/> and <see cref="LiveTrigger.HasBeenTriggeredOnce"/>.</summary>
    public Task ResetAsync(string id, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("triggers.set",
            new SetWire([new EditWire(id, TimesTriggered: null, Reset: true)]), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<TriggerWire>? Triggers, bool IsHost);
    private sealed record TriggerWire(string Id, string Label, int? TimesTriggered, bool? HasBeenTriggeredOnce,
        int? TriggerLimit, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Triggers);
    private sealed record EditWire(string Id, int? TimesTriggered, bool? Reset);
}

/// <summary>One loaded scripted trigger volume. <paramref name="Id"/> is its own
/// <c>UniqueTriggerID</c> (the same key the save's <c>TriggerMap</c> uses - see the class
/// remarks), NOT an actor path. <paramref name="Label"/> is its real class name. Every state field
/// is null when this particular actor could not be read right now (a trigger-shaped class this
/// module has never heard of), rather than a guessed value.</summary>
public sealed record LiveTrigger(string Id, string Label, int? TimesTriggered, bool? HasBeenTriggeredOnce,
    int? TriggerLimit, double X, double Y, double Z);

/// <summary>Every loaded trigger plus whether this process has host authority to change them.</summary>
public sealed record LiveTriggerDirectory(IReadOnlyList<LiveTrigger> Triggers, bool IsHost);
