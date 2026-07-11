using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Parses an Abiotic Factor <c>WorldSave_*.sav</c> file into typed models.
///
/// Like player saves, world-save property names carry hash suffixes from the blueprint
/// compiler - e.g. <c>ContainerInventories_110_2B3F...</c>. We match by prefix everywhere
/// so the reader survives suffix changes between game patches.
///
/// First pass models the obvious editable category: <em>containers</em> (deployables in
/// <c>DeployedObjectMap</c> with non-empty <c>ContainerInventories_</c>, plus entries of
/// <c>CustomInventoryMap</c>). The raw save tree is preserved on
/// <see cref="WorldSaveData.Raw"/> so unedited properties round-trip byte-perfect.
/// </summary>
public static partial class WorldSaveReader
{
    static WorldSaveReader()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Loads a world save from <paramref name="path"/> and returns a typed view.
    /// </summary>
    public static WorldSaveData ReadFromFile(string path)
    {
        Diagnostics.EditorLog.Info("WorldSave", $"Parsing {Path.GetFileName(path)}");
        try
        {
            using var fs = File.OpenRead(path);
            return ReadFromStream(fs);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("WorldSave", $"Failed to parse {Path.GetFileName(path)}", ex);
            throw;
        }
    }

    public static WorldSaveData ReadFromStream(Stream stream)
    {
        var save = SaveGame.LoadFrom(stream);

        var containers = new List<WorldContainer>();
        containers.AddRange(ReadDeployedContainers(save));
        containers.AddRange(ReadCustomInventoryContainers(save));
        containers.AddRange(ReadVehicleContainers(save));

        var flags = ReadWorldFlags(save);
        var doors = ReadDoors(save);

        // Metadata-save extras: quest chapter + playtime + global recipes. Absent on
        // per-region saves.
        var story = save.Properties.FindByPrefix("StoryProgressionRow")?.Property?.Value?.ToString();
        int? minutes = save.Properties.FindByPrefix("MinutesPassed")?.Property?.Value is int m ? m : null;
        var globalRecipes = ReadGlobalRecipes(save);
        var droppedItems = ReadDroppedItems(save);
        var npcs = ReadNpcs(save);
        var pets = ReadPets(save);
        var vehicles = ReadVehicles(save);
        var deployables = ReadDeployables(save);

        LogUnmodeledKeys(save);

        return new WorldSaveData(save, containers, flags, doors, story, minutes, globalRecipes, droppedItems, npcs, deployables, pets, vehicles);
    }

    // Top-level prefixes this reader consumes. Anything else in a save is data the
    // editor has NO visibility on - surfaced via the diagnostic log so format changes
    // in game updates are traceable.
    private static readonly string[] ConsumedPrefixes =
    {
        "DeployedObjectMap", "CustomInventoryMap", "WorldFlags",
        // The door maps' real key names; a bare "Door" prefix matches neither.
        "SimpleDoorMap", "SecurityDoorMap",
        "StoryProgressionRow", "MinutesPassed", "GlobalRecipesUnlocked", "GlobalRecipesResearched",
        "DroppedItemMap", "NarrativeNPCMap", "LevelGUID",
        "TimeOfDay", "DayDiscovered", "LeyakContainmentIDs",
        "PetNPC", "GlobalUnlocks", "LastPlayed",
        // Understood bookkeeping, intentionally not editable: the owning server/world
        // id and the raw engine-side save version int.
        "SaveIdentifier", "SaveVersion",
    };

    private static void LogUnmodeledKeys(SaveGame save)
    {
        if (save.Properties is null) return;
        foreach (var tag in save.Properties)
        {
            var name = tag.Name?.Value;
            if (name is null) continue;
            if (ConsumedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            // A registered world-map feature (Features/) now models this map - editable, not unknown.
            if (Features.WorldMapFeatures.IsKnownMap(name)) continue;
            Diagnostics.EditorLog.UnknownData("WorldSave", name,
                $"unmodeled top-level property ({tag.Property?.GetType().Name ?? "?"}) - preserved verbatim, not editable in the UI");
        }
    }

    internal static IList<KeyValuePair<FProperty, FProperty>>? GetMapPairs(
        IList<FPropertyTag>? topLevel,
        string namePrefix)
    {
        if (topLevel is null) return null;
        var tag = topLevel.FindByPrefix(namePrefix);
        if (tag?.Property is MapProperty mp) return mp.Value;
        return null;
    }

    internal static string? ExtractMapKeyString(FProperty key)
    {
        // Map keys here are StrProperty / NameProperty / similar; Value is either an
        // FString or a plain string depending on the property type.
        var v = key.Value;
        return v switch
        {
            FString fs => fs.Value,
            string s => s,
            _ => v?.ToString(),
        };
    }
}
