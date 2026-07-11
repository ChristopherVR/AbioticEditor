using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>ButtonMap</c> (region saves): each interactive button actor stores its
/// press/enable/activation state. Exposes four editable boolean fields plus a read-only
/// display of the button's ID string, so players can pre-trigger or lock buttons without
/// physically interacting with them in-game.
///
/// <para>The entry value is a <c>StructProperty → PropertiesStruct</c>; leaves are located
/// by stable name prefix (e.g. <c>ButtonIsEnabled_</c>) via
/// <see cref="AbioticEditor.Core.Saves.PropertyTagExtensions.FindByPrefix"/>.</para>
/// </summary>
public sealed class ButtonMapFeature : WorldMapFeatureBase
{
    // ── stable leaf prefixes ──────────────────────────────────────────────────────────────────

    private const string ButtonIdPrefix = "ButtonID_";
    private const string PressedOncePrefix = "ButtonHasBeenPressedOnce_";
    private const string EnabledPrefix = "ButtonIsEnabled_";
    private const string ActivatedPrefix = "ButtonActivated_";
    private const string NoResetPrefix = "NoReset_";

    // ── IWorldMapFeature identity ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override string Id => "buttons";

    /// <inheritdoc/>
    public override string MapName => "ButtonMap";

    /// <inheritdoc/>
    public override string DisplayName => "Buttons";

    /// <inheritdoc/>
    public override string Description =>
        "Set whether each interactive button has been pressed, is enabled, is activated, or skips the next reset.";

    /// <summary>Prefer the button's own ID string for the list label; fall back to a number.</summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
    {
        var id = props.GetString(ButtonIdPrefix);
        return string.IsNullOrWhiteSpace(id) || id is "None" ? $"Button {ordinal}" : $"Button {ordinal}: {id}";
    }

    // ── ReadFields ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var buttonId = props.GetString(ButtonIdPrefix);
        var pressedOnce = props.TryGetBool(PressedOncePrefix) ?? false;
        var enabled = props.TryGetBool(EnabledPrefix) ?? false;
        var activated = props.TryGetBool(ActivatedPrefix) ?? false;
        var noReset = props.TryGetBool(NoResetPrefix) ?? false;

        return new[]
        {
            WorldMapField.ReadOnly("buttonId", "Button ID", buttonId,
                hint: "Identifier string from the save (read-only)."),
            WorldMapField.Bool("pressedOnce", "Pressed once", pressedOnce,
                hint: "true = the button has been pressed at least once."),
            WorldMapField.Bool("enabled", "Enabled", enabled,
                hint: "true = the button can currently be interacted with."),
            WorldMapField.Bool("activated", "Activated", activated,
                hint: "true = the button is in an activated/latched state."),
            WorldMapField.Bool("noReset", "No reset", noReset,
                hint: "true = the button keeps its current state and is skipped by the next "
                    + "reset cycle (it won't return to its default)."),
        };
    }

    // ── ApplyField ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        var prefix = fieldId switch
        {
            "pressedOnce" => PressedOncePrefix,
            "enabled" => EnabledPrefix,
            "activated" => ActivatedPrefix,
            "noReset" => NoResetPrefix,
            _ => null,
        };

        if (prefix is null)
        {
            return WorldEditResult.Failure(
                $"unknown field '{fieldId}' (expected: pressedOnce, enabled, activated, noReset).");
        }

        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = props.TryGetBool(prefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }

        return WorldMapAccessor.SetBool(props, prefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the '{prefix}' field is missing from this button entry.");
    }
}
