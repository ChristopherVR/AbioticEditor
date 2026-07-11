using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Resolves a power socket's <c>PluggedInDeviceAssetID_</c> (a 32-hex GUID, or the sentinel
/// <c>"-1"</c> when nothing is plugged in) to the actual device behind it. That id lives in the
/// same identifier space as <c>DeployedObjectMap</c> keys, so a device index built from a save's
/// deployables maps the id straight to a deployable's class - confirmed across the fixture saves,
/// where every resolved socket pointed at a DeployedObjectMap entry. The device may be power
/// infrastructure (cable reroute, battery, plug strip, laser, ...) or a container (crafting bench,
/// fridge, storage crate, ...); only containers can be opened in the editor's CONTAINERS tab.
/// </summary>
public static class PowerSocketDeviceResolver
{
    /// <summary>What a plugged-in device id resolves to.</summary>
    /// <param name="Id">The device GUID (the socket's stored asset id).</param>
    /// <param name="ClassName">The deployable class (e.g. <c>Deployed_Plugboard_C</c>), or null.</param>
    /// <param name="FriendlyName">A human label (e.g. "Plug Board", "Crafting Bench").</param>
    /// <param name="IsContainer">True when the device has an inventory (can be opened in CONTAINERS).</param>
    /// <param name="SourceFile">The world-save file the device lives in (folder index only); null for a same-save index.</param>
    public sealed record DeviceInfo(
        string Id, string? ClassName, string FriendlyName, bool IsContainer, string? SourceFile = null);

    /// <summary>True when an asset id means "nothing is plugged in" (empty, "-1" or "None").</summary>
    public static bool IsNothingPlugged(string? assetId)
        => string.IsNullOrWhiteSpace(assetId) || assetId is "-1" or "None";

    /// <summary>
    /// Builds a GUID -> device index from one save's <c>DeployedObjectMap</c>. Custom-inventory and
    /// vehicle maps are never referenced by sockets, so only deployables are indexed.
    /// </summary>
    public static Dictionary<string, DeviceInfo> BuildIndex(SaveGame save)
    {
        var index = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        AddDeployables(index, save, sourceFile: null);
        return index;
    }

    /// <summary>
    /// Builds a folder-wide GUID -> device index by merging the deployables of every supplied
    /// world save, tagging each device with the file it lives in. A socket in one region save can
    /// power a device in another (commonly the hub <c>WorldSave_Facility.sav</c>); this index
    /// resolves those cross-save references for navigation. The first save to define a GUID wins
    /// (GUIDs are unique across the world).
    /// </summary>
    public static Dictionary<string, DeviceInfo> BuildFolderIndex(
        IEnumerable<(string FileName, SaveGame Save)> saves)
    {
        var index = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, save) in saves)
        {
            AddDeployables(index, save, fileName);
        }
        return index;
    }

    /// <summary>
    /// Merges one save's deployables into an existing index, tagged with <paramref name="fileName"/>.
    /// Lets a host build the folder index by loading one save at a time (so a large hub save isn't
    /// held in memory alongside every sibling). First writer of a GUID wins.
    /// </summary>
    public static void MergeSave(Dictionary<string, DeviceInfo> index, string? fileName, SaveGame save)
        => AddDeployables(index, save, fileName);

    private static void AddDeployables(Dictionary<string, DeviceInfo> index, SaveGame save, string? sourceFile)
    {
        foreach (var entry in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
        {
            if (index.ContainsKey(entry.Key))
            {
                continue;
            }
            var className = ExtractClassName(entry.Props);
            var isContainer = entry.Props.FindByPrefix("ContainerInventories_")?.Property is ArrayProperty arr
                && arr.Value is { Length: > 0 };
            index[entry.Key] = new DeviceInfo(entry.Key, className, FriendlyName(className), isContainer, sourceFile);
        }
    }

    /// <summary>A short readable label for a deployable class, with curated names for the common ones.</summary>
    public static string FriendlyName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return "Unknown device";
        }
        foreach (var (needle, label) in Curated)
        {
            if (className.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }
        return Humanize(className);
    }

    // Matched by substring (first match wins), so tiered variants (Battery_T1/T2/T3) and named
    // benches all collapse to one readable label.
    private static readonly (string Needle, string Label)[] Curated =
    {
        ("CraftingBench", "Crafting Bench"),
        ("CookingStation", "Cooking Station"),
        ("AmmoStation", "Ammo Bench"),
        ("ChemistryBench", "Chemistry Bench"),
        ("DistillationBench", "Distillation Bench"),
        ("Plugboard", "Plug Board"),
        ("PlugStrip", "Plug Strip"),
        ("CableReroute", "Cable Reroute"),
        ("Battery", "Battery"),
        ("LaserEmitter", "Laser Emitter"),
        ("TeslaCoil", "Tesla Coil"),
        ("Megalight", "Megalight"),
        ("TeleporterPad", "Teleporter Pad"),
        ("DistributionPad", "Distribution Pad"),
        ("ChargingStation", "Charging Station"),
        ("StorageCrate", "Storage Crate"),
        ("Refrigerator", "Refrigerator"),
        ("MiniFridge", "Mini Fridge"),
        ("Freezer", "Freezer"),
        ("Aquarium", "Aquarium"),
        ("AutoSalvager", "Auto-Salvager"),
        ("Stove", "Stove"),
        ("Oven", "Oven"),
    };

    private static string Humanize(string className)
    {
        var name = className;
        if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
        name = name.Replace("Deployed_", string.Empty).Replace("Deployable_", string.Empty)
            .Replace("Container_", string.Empty).Replace('_', ' ').Trim();
        return name.Length == 0 ? className : name;
    }

    private static string? ExtractClassName(IList<FPropertyTag> deployableProps)
    {
        var classTag = deployableProps.FindByPrefix("Class_");
        if (classTag?.Property?.Value is SoftObjectPath softPath)
        {
            return softPath.AssetName?.Value;
        }
        return classTag?.Property?.Value?.ToString();
    }
}
