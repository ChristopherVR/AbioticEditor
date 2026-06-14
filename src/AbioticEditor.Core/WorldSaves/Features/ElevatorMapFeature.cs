using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>ElevatorMap</c> (region saves): each elevator actor stores whether it is
/// currently parked at its top stop. The only persisted, meaningful state is
/// <c>TopOpen</c> (bool) - true = at the top, false = at the bottom. Editing it lets a player
/// "call" a fixed elevator to the level they want without riding it.
///
/// <para>This is also the reference implementation for the other world-map features: derive
/// from <see cref="WorldMapFeatureBase"/>, expose typed fields in <see cref="ReadFields"/>,
/// and patch one leaf in <see cref="ApplyField"/> via <see cref="WorldMapAccessor"/>.</para>
/// </summary>
public sealed class ElevatorMapFeature : WorldMapFeatureBase
{
    /// <summary>Save leaf prefix (the blueprint hash suffix is matched by <c>FindByPrefix</c>).</summary>
    private const string TopOpenPrefix = "TopOpen_";

    public override string Id => "elevators";

    public override string MapName => "ElevatorMap";

    public override string DisplayName => "Elevators";

    public override string Description =>
        "Set whether each fixed elevator is parked at its top stop (call it up or down).";

    /// <summary>Elevator actor keys carry no friendly name, so number them for the list.</summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
        => $"Elevator {ordinal}";

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var atTop = props.TryGetBool(TopOpenPrefix) ?? false;
        return new[]
        {
            WorldMapField.Bool("topOpen", "At top stop", atTop,
                hint: "true = elevator parked at the top, false = at the bottom"),
        };
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "topOpen", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: topOpen).");
        }
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = props.TryGetBool(TopOpenPrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetBool(props, TopOpenPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the TopOpen field is missing from this elevator entry.");
    }
}
