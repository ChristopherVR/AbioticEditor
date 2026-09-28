namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Coarse grouping of a placed object for viewers and filters. Derived only from the blueprint
/// class name (and whether the object carries an inventory), so it is a display hint: an
/// unrecognised class is <see cref="Other"/>, and a missing class is <see cref="Unknown"/>.
/// </summary>
public enum PlacedObjectCategory
{
    /// <summary>No class name at all.</summary>
    Unknown = 0,

    /// <summary>Crafting benches and workstations.</summary>
    Bench,

    /// <summary>Anything that holds items (crates, cabinets, fridges, trash cans).</summary>
    Container,

    /// <summary>Power devices: batteries, plug strips, cables, chargers, emitters, teleporters.</summary>
    Power,

    /// <summary>Lamps and other light sources.</summary>
    Light,

    /// <summary>Building pieces: barricades, bridges, ramps, walls, ropes.</summary>
    Structure,

    /// <summary>Furniture, decorations and everything not recognised above.</summary>
    Other,
}

/// <summary>Classifies placed-object classes into <see cref="PlacedObjectCategory"/>.</summary>
public static class PlacedObjectCategoryCatalog
{
    /// <summary>The categories in display order (Unknown last).</summary>
    public static IReadOnlyList<PlacedObjectCategory> All { get; } =
    [
        PlacedObjectCategory.Bench, PlacedObjectCategory.Container, PlacedObjectCategory.Power,
        PlacedObjectCategory.Light, PlacedObjectCategory.Structure, PlacedObjectCategory.Other,
        PlacedObjectCategory.Unknown,
    ];

    /// <summary>
    /// Category for a class name such as <c>Deployed_CraftingBench_Default_C</c>. Order matters:
    /// benches first (a bench that also holds items is still a bench), then lights, power,
    /// structures, and containers last so that an item-holding lamp stays a lamp.
    /// </summary>
    public static PlacedObjectCategory Classify(string? className, bool hasInventory = false)
    {
        if (string.IsNullOrWhiteSpace(className)) return PlacedObjectCategory.Unknown;
        bool Has(params string[] hints) => hints.Any(h => className.Contains(h, StringComparison.OrdinalIgnoreCase));

        if (Has("CraftingBench", "Bench_Crafting", "Bench_CookingStation", "Bench_AmmoStation", "ChemistryBench",
                "DistillationBench", "AutoSalvager", "TransmogDresser"))
            return PlacedObjectCategory.Bench;
        if (Has("Lamp", "Light", "GlowTulip", "Antelight", "Megalight", "Sconce"))
            return PlacedObjectCategory.Light;
        if (Has("Battery", "PlugStrip", "Plugboard", "CableReroute", "Charging", "LaserEmitter",
                "LaserPowerConverter", "LaserCollector", "NeutrinoEmitter", "Teleporter", "TeslaCoil", "Generator",
                "Solar", "DistributionPad", "PowerChair"))
            return PlacedObjectCategory.Power;
        if (Has("Barricade", "Bridge_", "Ramp", "CementBagWall", "ClothRope", "PlantRope", "Fence", "Stairs"))
            return PlacedObjectCategory.Structure;
        if (hasInventory || Has("StorageCrate", "Container_", "Freezer", "Refrigerator", "Tacklebox", "FilingCabinet", "Crate_"))
            return PlacedObjectCategory.Container;
        return PlacedObjectCategory.Other;
    }
}
