namespace AbioticEditor.Core.WorldSaves;

/// <summary>One selectable paint colour (a value of the game's <c>EPaintColor</c> enum).</summary>
public sealed record DeployablePaintColor(int Value, string DisplayName);

/// <summary>
/// Placed-object (deployable) paint colours: which classes can be painted, and what colour
/// values mean. Grounded in the game's own data (see
/// <c>docs/reference/research/research-deployable-paint.md</c> and
/// <c>tests/AbioticEditor.Probes/DeployablePaintProbeTests.cs</c>), not guessed:
/// <list type="bullet">
///   <item>The colour values and names come from the game's <c>EPaintColor</c> enum (13 colours
///     plus <c>None</c> at value 12, which means "unpainted" and is deliberately excluded from
///     <see cref="Colors"/> - it is expressed as "no entry", not as a selectable colour).</item>
///   <item>The class -&gt; paintable-row map below is every <c>Deployed_*</c> blueprint whose
///     compiled class default (CDO) sets a non-<c>None</c> <c>PaintedDeployableRow</c>, dumped
///     directly from the pak. A deployable whose class isn't in this map has no confirmed paint
///     profile and is left alone (the same "only expose what's confirmed" rule the item
///     texture-variant catalog follows).</item>
/// </list>
/// The save itself carries no separate paint field: painting a deployable adds a
/// <c>{Key: EDynamicProperty::PaintColor, Value: &lt;colour&gt;}</c> entry to its
/// <c>ChangableData_.DynamicProperties_</c> array, the same array/struct pair item slots already
/// use for weapon coatings (see <see cref="PetDynamicProperties"/>) - confirmed against real
/// <c>WorldSave_Facility.sav</c> data carrying exactly that entry on painted crates, cubicles,
/// barricades and a crafting bench.
/// </summary>
public static class DeployablePaintCatalog
{
    /// <summary>The <c>EDynamicProperty</c> key name the game stores the colour under.</summary>
    public const string DynamicPropertyKey = "PaintColor";

    /// <summary>The <c>EPaintColor::None</c> value - "unpainted", omitted from <see cref="Colors"/>.</summary>
    public const int NoneValue = 12;

    /// <summary>
    /// Every selectable colour, in the game enum's own order (its numeric value, which is what
    /// gets written to the save - not display order).
    /// </summary>
    public static IReadOnlyList<DeployablePaintColor> Colors { get; } =
    [
        new DeployablePaintColor(0, "White"),
        new DeployablePaintColor(1, "Blue"),
        new DeployablePaintColor(2, "Red"),
        new DeployablePaintColor(3, "Green"),
        new DeployablePaintColor(4, "Orange"),
        new DeployablePaintColor(5, "Purple"),
        new DeployablePaintColor(6, "Yellow"),
        new DeployablePaintColor(7, "Black"),
        new DeployablePaintColor(8, "Cyan"),
        new DeployablePaintColor(9, "Lime"),
        new DeployablePaintColor(10, "Pink"),
        new DeployablePaintColor(11, "Brown"),
        new DeployablePaintColor(13, "Glitch"),
    ];

    /// <summary>The display name for a colour value, or a fallback for an unrecognized one
    /// (a newer game build could add a 14th colour before this catalog is updated).</summary>
    public static string DisplayName(int value)
    {
        foreach (var c in Colors)
        {
            if (c.Value == value) return c.DisplayName;
        }
        return value == NoneValue ? "Unpainted" : $"Colour {value}";
    }

    /// <summary>
    /// Confirmed <c>Deployed_*_C</c> class name -&gt; <c>DT_PaintedDeployables</c> row, dumped
    /// from each blueprint's own compiled default (<c>Default__&lt;Class&gt;.PaintedDeployableRow</c>).
    /// Not every row in the 38-row table has a class mapped here: only classes whose own CDO sets
    /// the row are included, so an unmapped subclass that merely inherits a parent's row without
    /// overriding it (several cubicle/teleporter/etc. reskins) is intentionally left out rather
    /// than guessed at.
    /// </summary>
    private static readonly Dictionary<string, string> PaintableRowByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Deployed_Barricade_Makeshift_C"] = "barricade_office",
        ["Deployed_Barricade_Plank_Full_C"] = "PlankBarricade",
        ["Deployed_Barricade_Plank_Half_C"] = "PlankBarricade",
        ["Deployed_Barricade_Plank_Window_C"] = "PlankBarricade",
        ["Deployed_Bridge_Tier1_C"] = "BridgeT1",
        ["Deployed_Bridge_Tier2_C"] = "bridgeT2",
        ["Deployed_CementBagWall_Block_C"] = "bagwall",
        ["Deployed_CementBagWall_Corner_C"] = "bagwall",
        ["Deployed_CementBagWall_Curve_C"] = "bagwall",
        ["Deployed_CementBagWall_HalfCurve_C"] = "bagwall",
        ["Deployed_CementBagWall_Straight_C"] = "bagwall",
        ["Deployed_Ramp_Large_C"] = "ramp",
        ["Deployed_Ramp_Small_C"] = "Barricade_Office",
        ["Deployed_CraftingBench_Default_C"] = "craftingbench",
        ["Deployed_CubicleBase_C"] = "cubicle_full",
        ["Deployed_Cubicle_FullHalf_C"] = "cubicle_full_half",
        ["Deployed_Cubicle_Gate_C"] = "cubicle_half",
        ["Deployed_Cubicle_Gate_Half_C"] = "cubicle_half_half",
        ["Deployed_Cubicle_Half_C"] = "cubicle_half",
        ["Deployed_Cubicle_HalfHalf_C"] = "cubicle_half_half",
        ["Deployed_Cubicle_HalfNoWindow_C"] = "cubicle_half_nowindow",
        ["Deployed_Cubicle_HalfWindow_C"] = "cubicle_half_window",
        ["Deployed_Cubicle_NoWindow_C"] = "cubicle_full_nowindow",
        ["Deployed_Cubicle_Window_C"] = "cubicle_full_window",
        ["Deployed_Furniture_CraftedBed_C"] = "bed",
        ["Deployed_Furniture_CraftedBed_T2_C"] = "bedT2",
        ["Deployed_ItemStand_ParentBP_C"] = "wallshelfing",
        ["Deployed_LiquidContainer_Barrel_C"] = "oildrum",
        ["Deployed_LiquidContainer_Barrel_Wood_C"] = "barrelcrafted",
        ["Deployed_LiquidContainer_Cauldron_Tech_C"] = "cauldron",
        ["Deployed_PetBed_Luxury_C"] = "petbed_luxury",
        ["Deployed_PetBed_Medium_C"] = "petbed",
        ["Deployed_PetBed_Small_C"] = "petbed_small",
        ["Deployed_Shelf_Small_C"] = "wallshelfing",
        ["Deployed_StorageCrate_Makeshift_C"] = "makeshiftcrate",
        ["Deployed_StorageCrate_Makeshift_T2_C"] = "reinforcedcrate",
        ["Deployed_StorageCrate_Makeshift_T3_C"] = "carboncrate",
        ["Deployed_StorageCrate_Makeshift_T4_C"] = "crateT4",
        ["Deployed_TeleporterPad_C"] = "teleporterpad",
        ["Deployed_JackOLantern_C"] = "pumpkin_carved_classic",
        ["Deployed_Lamp_Standing_Crafted_C"] = "Light",
        ["Deployed_Lamp_Wall_Crafted_C"] = "Light",
        ["Deployed_Rug_Mat_C"] = "Rug_Mat",
        ["Deployed_Rug_Oval_C"] = "Rug_Oval",
        ["Deployed_Rug_Rectangular_C"] = "Rug_Rectangle",
        ["Deployed_Rug_Square_C"] = "Rug_Square",
    };

    /// <summary>True when this deployable class has a confirmed paint profile.</summary>
    public static bool IsPaintable(string? className)
        => className is not null && PaintableRowByClass.ContainsKey(className);

    /// <summary>The <c>DT_PaintedDeployables</c> row for a class, or null when unconfirmed/not paintable.</summary>
    public static string? RowFor(string? className)
        => className is not null && PaintableRowByClass.TryGetValue(className, out var row) ? row : null;
}
