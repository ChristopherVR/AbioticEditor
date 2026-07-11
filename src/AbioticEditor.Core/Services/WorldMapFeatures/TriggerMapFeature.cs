using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>TriggerMap</c> (region saves): each entry represents a named scripted world
/// trigger and records how many times it has fired during the current playthrough. Setting
/// <c>TimesTriggered</c> to zero effectively "resets" a trigger so that its scripted event
/// can fire again on the next qualifying occurrence.
///
/// <para>The only editable field is <c>timesTriggered</c> (int). <c>triggerId</c> is surfaced
/// as a read-only display string because it is identical to the map entry key and editing it
/// would create an inconsistency that Abiotic Factor would likely ignore or reject.</para>
/// </summary>
public sealed class TriggerMapFeature : WorldMapFeatureBase
{
    /// <summary>Stable prefix for the trigger's unique ID string leaf.</summary>
    private const string UniqueTriggerIdPrefix = "UniqueTriggerID_";

    /// <summary>Stable prefix for the fire-count integer leaf.</summary>
    private const string TimesTriggeredPrefix = "TimesTriggered_";

    /// <inheritdoc/>
    public override string Id => "triggers";

    /// <inheritdoc/>
    public override string MapName => "TriggerMap";

    /// <inheritdoc/>
    public override string DisplayName => "Triggers";

    /// <inheritdoc/>
    public override string Description =>
        "how many times each scripted world trigger has fired; reset by setting to 0";

    /// <summary>
    /// Exposes two fields per entry:
    /// <list type="bullet">
    ///   <item><c>triggerId</c>: read-only; the unique trigger name (equals the map key).</item>
    ///   <item><c>timesTriggered</c>: editable integer; fire count for this trigger.</item>
    /// </list>
    /// </summary>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var triggerId = props.GetString(UniqueTriggerIdPrefix);
        var timesTriggered = props.GetLong(TimesTriggeredPrefix);

        return new[]
        {
            WorldMapField.ReadOnly("triggerId", "Trigger ID", triggerId,
                hint: "unique name of this scripted world trigger; read-only (equals the map key)"),
            WorldMapField.Integer("timesTriggered", "Times triggered", timesTriggered,
                hint: "how many times this trigger has fired; set to 0 to reset it"),
        };
    }

    /// <summary>
    /// Handles <c>timesTriggered</c>: parses the string as a non-negative integer,
    /// compares against the current value, and patches the leaf in place.
    /// Returns <see cref="WorldEditResult.NoChange"/> when the value is already correct,
    /// <see cref="WorldEditResult.Failure(string)"/> for unknown fields, non-numeric input,
    /// or negative values.
    /// </summary>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "timesTriggered", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure(
                $"unknown field '{fieldId}' for TriggerMap (expected: timesTriggered).");
        }

        if (!WorldMapAccessor.TryParseInt(value, out var wanted))
        {
            return WorldEditResult.Failure(
                $"'{value}' is not a valid integer.");
        }

        if (wanted < 0)
        {
            return WorldEditResult.Failure(
                $"timesTriggered must be >= 0 (got {wanted}); set to 0 to reset the trigger.");
        }

        var current = (int)props.GetLong(TimesTriggeredPrefix);
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }

        return WorldMapAccessor.SetInt(props, TimesTriggeredPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the TimesTriggered field is missing from this trigger entry.");
    }
}
