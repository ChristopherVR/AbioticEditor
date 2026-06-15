using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>ResourceNodeMap</c> (region saves): each resource node actor stores
/// whether it has been harvested and on which in-game day it was picked up. Resetting
/// <c>HasBeenPickedUp_</c> to false sets a node back to un-harvested so it can be
/// gathered again without waiting for the in-game respawn timer.
///
/// <para>Fields exposed per entry:</para>
/// <list type="bullet">
///   <item><description><c>position</c>: world position (read-only display string, may be absent).</description></item>
///   <item><description><c>harvested</c>: whether the node has already been picked up (editable bool).</description></item>
///   <item><description><c>dayPickedUp</c>: in-game day the node was harvested (editable integer).</description></item>
/// </list>
/// </summary>
public sealed class ResourceNodeMapFeature : WorldMapFeatureBase
{
    private const string HasBeenPickedUpPrefix = "HasBeenPickedUp_";
    private const string DayPickedUpPrefix = "DayPickedUp_";
    private const string CurrentPositionPrefix = "CurrentPosition_";

    /// <inheritdoc/>
    public override string Id => "resource-nodes";

    /// <inheritdoc/>
    public override string MapName => "ResourceNodeMap";

    /// <inheritdoc/>
    public override string DisplayName => "Resource Nodes";

    /// <inheritdoc/>
    public override string Description =>
        "Resource nodes are the harvestable spots placed around the world (ore veins, mineable "
        + "rocks, gatherable plants, and similar). The game tracks whether each has been "
        + "harvested and on which in-game day, so it can regrow and be gathered again after a "
        + "delay. Here you can set a node back to un-harvested (so it's available now) or adjust "
        + "the harvest day.";

    /// <inheritdoc/>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var fields = new List<WorldMapField>(3);

        // Read-only world position (may be absent on some entries).
        var vec = WorldMapAccessor.GetVector(props, CurrentPositionPrefix);
        if (vec is not null)
        {
            var (x, y, z) = vec.Value;
            fields.Add(WorldMapField.ReadOnly(
                "position",
                "Position",
                FormattableString.Invariant($"{x:0.###}, {y:0.###}, {z:0.###}"),
                hint: "Where this node sits in the world (x, y, z). Read-only - moving a node "
                    + "would not relocate the harvestable in-game."));
        }

        // Editable: has the node been harvested?
        var harvested = props.TryGetBool(HasBeenPickedUpPrefix) ?? false;
        fields.Add(WorldMapField.Bool(
            "harvested",
            "Harvested",
            harvested,
            hint: "Whether this node has already been gathered. true = depleted (waiting to "
                + "regrow); false = available to harvest right now. Set to false to make it "
                + "gatherable again immediately."));

        // Editable: which in-game day it was picked up.
        var dayPickedUp = (int)props.GetLong(DayPickedUpPrefix, defaultValue: 0);
        fields.Add(WorldMapField.Integer(
            "dayPickedUp",
            "Day Picked Up",
            dayPickedUp,
            hint: "The in-game day this node was last harvested; the game measures regrowth from "
                + "this day. 0 means it has not been picked up yet."));

        return fields;
    }

    /// <inheritdoc/>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (string.Equals(fieldId, "harvested", StringComparison.OrdinalIgnoreCase))
        {
            if (!WorldMapAccessor.TryParseBool(value, out var wanted))
            {
                return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
            }

            var current = props.TryGetBool(HasBeenPickedUpPrefix) ?? false;
            if (current == wanted)
            {
                return WorldEditResult.NoChange;
            }

            return WorldMapAccessor.SetBool(props, HasBeenPickedUpPrefix, wanted)
                ? WorldEditResult.Success
                : WorldEditResult.Failure("the HasBeenPickedUp field is missing from this resource node entry.");
        }

        if (string.Equals(fieldId, "dayPickedUp", StringComparison.OrdinalIgnoreCase))
        {
            if (!WorldMapAccessor.TryParseInt(value, out var wanted))
            {
                return WorldEditResult.Failure($"'{value}' is not a valid integer.");
            }

            var current = (int)props.GetLong(DayPickedUpPrefix, defaultValue: 0);
            if (current == wanted)
            {
                return WorldEditResult.NoChange;
            }

            return WorldMapAccessor.SetInt(props, DayPickedUpPrefix, wanted)
                ? WorldEditResult.Success
                : WorldEditResult.Failure("the DayPickedUp field is missing from this resource node entry.");
        }

        return WorldEditResult.Failure(
            $"unknown field '{fieldId}' (expected: harvested, dayPickedUp).");
    }
}
