using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>Saved cleanup state for level decals, confirmed in the Dam fixtures.</summary>
public sealed class DecalMapFeature : WorldMapFeatureBase
{
    private const string RemovedName = "Removed_30_128506D0489955F65729EEA611C542AC";
    public override string Id => "decals";
    public override string MapName => "DecalMap";
    public override string DisplayName => "Decals";
    public override string Description => "Change whether a saved stain has been cleaned away. Clear Cleaned to restore it when the region loads.";
    public override bool SupportsRemoval => false;

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props) =>
    [
        WorldMapField.Bool("removed", "Cleaned", props.TryGetBool("Removed_") ?? false,
            hint: "Checked means this decal has been removed from the world."),
    ];

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (fieldId != "removed") return WorldEditResult.Failure("Only the cleaned state can be changed.");
        if (!WorldMapAccessor.TryParseBool(value, out var removed))
            return WorldEditResult.Failure("Choose true or false for the cleaned state.");
        if ((props.TryGetBool("Removed_") ?? false) == removed) return WorldEditResult.NoChange;
        return WorldMapAccessor.SetBool(props, "Removed_", removed, RemovedName)
            ? WorldEditResult.Success : WorldEditResult.Failure("The cleaned state could not be changed.");
    }
}
