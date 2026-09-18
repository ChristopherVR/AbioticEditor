using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="TriggersFeatureId"/> ("Triggers", <see cref="TriggerMapFeature"/>'s live twin) -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, the same shape <see cref="LiveButtonsFeatureSession"/> uses.
///
/// <para>See <see cref="LiveTriggersChannel"/> for the full field mapping and citations.
/// <see cref="LiveTrigger.Id"/> is the trigger's own <c>UniqueTriggerID</c> string (matching the
/// offline save's <c>TriggerMap</c> key exactly), NOT an actor path - every other live world area
/// keys its rows by actor path, so this is a deliberate, documented exception. <c>timesTriggered</c>
/// is a real editable count; <c>reset</c> is a momentary "do it now" toggle (the game's own
/// <c>ResetTriggerState()</c>, which clears both the count and <c>hasBeenTriggeredOnce</c>) that
/// always reads back <c>false</c> so its checkbox resets after a refresh.</para>
/// </summary>
public sealed class LiveTriggersFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>TriggerMapFeature.Id</c> (Core/WorldSaves/Features/TriggerMapFeature.cs).</summary>
    public const string TriggersFeatureId = "triggers";

    private readonly LiveTriggersChannel _channel;

    private LiveTriggersFeatureSession(LiveTriggersChannel channel, LiveTriggerDirectory directory)
    {
        _channel = channel;
        // Defensive dedupe (round 122): triggers.lua's own triggerRows() already merges every
        // volume sharing a UniqueTriggerID into one row (see its header comment for why - the
        // save's TriggerMap only ever has one entry per id), so this should never actually trim
        // anything in practice. It stays as a second, independent backstop the same way every
        // other live area gets one via LiveFeatureRows - see that type's own remarks.
        Triggers = LiveFeatureRows.DistinctById(directory.Triggers, t => t.Id);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveTriggersFeatureSession> ConnectAsync(
        LiveTriggersChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveTriggersFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveTrigger> Triggers { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: an edit already reached the running game by the time it returns, so
    /// there is never a client-side staged copy - see <see cref="LivePortalsFeatureSession.IsDirty"/>.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) - see LivePortalsFeatureSession's identical remark.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        // Defensive dedupe (round 122): triggers.lua's own triggerRows() already merges every
        // volume sharing a UniqueTriggerID into one row (see its header comment for why - the
        // save's TriggerMap only ever has one entry per id), so this should never actually trim
        // anything in practice. It stays as a second, independent backstop the same way every
        // other live area gets one via LiveFeatureRows - see that type's own remarks.
        Triggers = LiveFeatureRows.DistinctById(directory.Triggers, t => t.Id);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField IntInfo(string id, string label, int? value, string hint)
        => value is { } known
            ? WorldMapField.ReadOnly(id, label, known.ToString(System.Globalization.CultureInfo.InvariantCulture), hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this trigger actor right now (it may have just unloaded).");

    private static WorldMapField BoolInfo(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.ReadOnly(id, label, known ? "true" : "false", hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this trigger actor right now (it may have just unloaded).");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, TriggersFeatureId, StringComparison.Ordinal)) return null;
        var entries = Triggers.Select(t => new WorldMapEntry(
            t.Id,
            t.Label,
            new[]
            {
                t.TimesTriggered is { } times
                    ? WorldMapField.Integer("timesTriggered", "Times triggered", times,
                        hint: "How many times this trigger has fired. Applies live immediately via the game's own "
                            + "SaveTriggerData(); does not by itself clear \"has been triggered once\" below.")
                    : WorldMapField.ReadOnly("timesTriggered", "Times triggered", "not available live",
                        hint: "Could not read this off this trigger actor right now (it may have just unloaded)."),
                BoolInfo("hasBeenTriggeredOnce", "Has been triggered once", t.HasBeenTriggeredOnce,
                    "Whether this trigger has ever fired. Read-only here - use Reset below to clear it."),
                IntInfo("triggerLimit", "Trigger limit", t.TriggerLimit,
                    "The trigger's own configured fire-limit design value (a negative number typically means "
                        + "no limit). Informational only."),
                WorldMapField.Bool("reset", "Reset now", false,
                    hint: "Tick to fully reset this trigger (the game's own ResetTriggerState(): clears both the "
                        + "fire count and \"has been triggered once\", and re-arms the volume). Applies live "
                        + "immediately; this box always shows unticked afterward."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            TriggersFeatureId, "Triggers",
            "Scripted world triggers currently loaded in this region: how many times each has fired, and whether "
                + "to reset one.",
            MapName: "TriggerMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, TriggersFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }

        var current = Triggers.FirstOrDefault(t => string.Equals(t.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("trigger not found (it may have been unloaded).");

        try
        {
            if (string.Equals(fieldId, "reset", StringComparison.Ordinal))
            {
                if (!WorldMapAccessor.TryParseBool(value, out var wantedReset))
                {
                    return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
                }
                // Unticking is a no-op: there is nothing to undo, it was never persistent state.
                if (!wantedReset) return WorldEditResult.NoChange;
                await _channel.ResetAsync(entryKey).ConfigureAwait(false);
            }
            else if (string.Equals(fieldId, "timesTriggered", StringComparison.Ordinal))
            {
                if (!WorldMapAccessor.TryParseInt(value, out var wanted))
                {
                    return WorldEditResult.Failure($"'{value}' is not a valid integer.");
                }
                if (wanted < 0)
                {
                    return WorldEditResult.Failure($"timesTriggered must be >= 0 (got {wanted}); set to 0 to reset the trigger.");
                }
                if (current.TimesTriggered == wanted) return WorldEditResult.NoChange;
                await _channel.SetTimesTriggeredAsync(entryKey, wanted).ConfigureAwait(false);
            }
            else
            {
                return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
            }
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when it could not find a live
            // control for this trigger (see triggers.lua's own header comment) - surface it as-is.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("triggers cannot be removed."));
}
