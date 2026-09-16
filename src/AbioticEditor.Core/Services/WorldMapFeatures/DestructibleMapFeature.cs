using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>DestructibleMap</c> (region saves): each entry is a breakable world object
/// (ice walls, spore webbing, ceiling tiles, security doors, x-ray fields, and other
/// <c>Destructible_*</c>/<c>IceWall_*</c>/<c>Webbing_*</c> actors) whose persisted state is a
/// <c>StructProperty -&gt; PropertiesStruct</c> with exactly two leaves, confirmed across every
/// region in the server fixture (Facility, Containment, Dam, DarkFusion, DF_Labs,
/// DF_Overgrowth, Fracture, Labs, MFFoundry, MFWest, Office1, Office3, Plant, Residence,
/// Security, and the V_Inq/V_TheWall vignettes):
/// <list type="bullet">
///   <item><c>ActorPath_</c> (<c>SoftObjectPath</c>) - the actor's own path; read-only and
///   already encoded in the entry key, so it is not surfaced as a field.</item>
///   <item><c>Broken_</c> (<c>BoolProperty</c>) - whether the object has been broken/destroyed.
///   This is the one editable value, exposed here as <c>broken</c>.</item>
/// </list>
///
/// <para>The game only persists an entry once it deviates from its default (unbroken) state:
/// every entry observed across the fixture and a live server backup carries <c>Broken=true</c>
/// (no false entries exist anywhere), so the save simply never records an object that's still
/// intact. That also means removing an entry has the same practical effect as toggling it back
/// to unbroken, so - like <see cref="ElevatorMapFeature"/> - only the field edit is exposed and
/// per-entry removal is disabled to avoid two paths to the same outcome.</para>
/// </summary>
public sealed class DestructibleMapFeature : WorldMapFeatureBase
{
    /// <summary>Save leaf prefix (the blueprint hash suffix is matched by <c>FindByPrefix</c>).</summary>
    private const string BrokenPrefix = "Broken_";

    public override string Id => "destructibles";

    public override string MapName => "DestructibleMap";

    public override string DisplayName => "Breakable Objects";

    public override string Description =>
        "Ice walls, spore webbing, ceiling tiles and other breakable world objects: toggle whether "
        + "each one is broken. Setting one back to false repairs it.";

    /// <summary>
    /// An entry only exists once its object has broken away from the default state; deleting the
    /// entry would have the same effect as the <c>broken</c> field edit, so removal is disabled
    /// to avoid offering two controls for one outcome (mirrors <see cref="ElevatorMapFeature"/>).
    /// </summary>
    public override bool SupportsRemoval => false;

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var broken = props.TryGetBool(BrokenPrefix) ?? false;
        return new[]
        {
            WorldMapField.Bool("broken", "Broken", broken,
                hint: "true = destroyed/broken, false = intact; set to false to repair it"),
        };
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "broken", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: broken).");
        }
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = props.TryGetBool(BrokenPrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetBool(props, BrokenPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the Broken field is missing from this entry.");
    }
}
