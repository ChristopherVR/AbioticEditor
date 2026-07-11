using UeSaveGame;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Read-only view of a world save with typed accessors for the editable categories
/// (containers, quest-progression flags, doors), alongside the raw <see cref="SaveGame"/>
/// tree the writer mutates and re-serializes byte-perfect for everything else.
/// </summary>
public sealed class WorldSaveData
{
    public WorldSaveData(
        SaveGame raw,
        IReadOnlyList<WorldContainer> containers,
        IReadOnlyList<string> flags,
        IReadOnlyList<WorldDoor> doors,
        string? storyProgressionRow = null,
        int? minutesPassed = null,
        IReadOnlyList<string>? globalRecipes = null,
        IReadOnlyList<WorldDroppedItem>? droppedItems = null,
        IReadOnlyList<WorldNpc>? npcs = null,
        IReadOnlyList<WorldDeployable>? deployables = null,
        IReadOnlyList<WorldPet>? pets = null,
        IReadOnlyList<WorldVehicle>? vehicles = null)
    {
        DroppedItems = droppedItems ?? Array.Empty<WorldDroppedItem>();
        Npcs = npcs ?? Array.Empty<WorldNpc>();
        Pets = pets ?? Array.Empty<WorldPet>();
        Vehicles = vehicles ?? Array.Empty<WorldVehicle>();
        Deployables = deployables ?? Array.Empty<WorldDeployable>();
        Raw = raw;
        Containers = containers;
        Flags = flags;
        Doors = doors;
        StoryProgressionRow = storyProgressionRow;
        MinutesPassed = minutesPassed;
        GlobalRecipes = globalRecipes ?? Array.Empty<string>();
    }

    public SaveGame Raw { get; }

    /// <summary>
    /// The ABF_SAVE_VERSION from the save's custom header, or null when the save class
    /// wasn't one of our registered <c>[SaveClassPath]</c> classes. Known-good value:
    /// <see cref="Compatibility.SaveCompatibility.KnownGoodWorldVersion"/>.
    /// </summary>
    public int? AbfVersion => (Raw.CustomSaveClass as SaveClasses.AbioticWorldSave)?.Version;

    /// <summary>True when the save class mapped to a registered custom save class.</summary>
    public bool HasKnownSaveClass => Raw.CustomSaveClass is not null;

    /// <summary>The raw save class string from the file header.</summary>
    public string? SaveClassName => Raw.SaveClass?.Value;

    public IReadOnlyList<WorldContainer> Containers { get; }

    /// <summary>
    /// Active quest-progression flag names from the <c>WorldFlags</c> array
    /// (e.g. <c>Office_NewGameStarted</c>). The underlying property is an
    /// <c>ArrayProperty</c> of <c>NameProperty</c>, so order is preserved.
    /// </summary>
    public IReadOnlyList<string> Flags { get; }

    /// <summary>
    /// All doors from both <c>SimpleDoorMap</c> and <c>SecurityDoorMap</c>.
    /// </summary>
    public IReadOnlyList<WorldDoor> Doors { get; }

    /// <summary>
    /// Current main-quest chapter (<c>WorldSave_MetaData.sav</c> only); see
    /// <see cref="StoryProgressionCatalog"/>. Null for per-region world saves.
    /// </summary>
    public string? StoryProgressionRow { get; }

    /// <summary>Total played minutes (<c>WorldSave_MetaData.sav</c> only).</summary>
    public int? MinutesPassed { get; }

    /// <summary>
    /// World-wide unlocked recipe rows from <c>GlobalUnlocks.GlobalRecipesUnlocked_</c>
    /// (<c>WorldSave_MetaData.sav</c> only; empty elsewhere).
    /// </summary>
    public IReadOnlyList<string> GlobalRecipes { get; }

    /// <summary>Items lying on the ground (<c>DroppedItemMap</c>).</summary>
    public IReadOnlyList<WorldDroppedItem> DroppedItems { get; }

    /// <summary>Story NPCs / traders (<c>NarrativeNPCMap</c>).</summary>
    public IReadOnlyList<WorldNpc> Npcs { get; }

    /// <summary>Tamed companions (<c>PetNPC</c>).</summary>
    public IReadOnlyList<WorldPet> Pets { get; }

    /// <summary>Spawned vehicles (<c>VehicleMap</c>, region saves).</summary>
    public IReadOnlyList<WorldVehicle> Vehicles { get; }

    /// <summary>Every player-placed object with its world position (base manager / map).</summary>
    public IReadOnlyList<WorldDeployable> Deployables { get; }
}
