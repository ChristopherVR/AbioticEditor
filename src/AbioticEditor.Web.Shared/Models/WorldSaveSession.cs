using AbioticEditor.Core.Saves;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>Razor-hosted staged edit session for a world save.</summary>
public sealed class WorldSaveSession
{
    private WorldSaveData _data;
    private readonly string _path;
    private readonly AbioticEditor.Web.Services.ISaveFileSystem? _files;
    private HashSet<string> _originalFlags;
    private HashSet<string> _originalGlobalRecipes;
    private Dictionary<string, WorldDoor> _originalDoors;
    private Dictionary<string, WorldDoor> _doors;
    private Dictionary<string, WorldContainer> _originalContainers;
    private Dictionary<string, WorldContainer> _containers;
    private Dictionary<string, WorldNpc> _originalNpcs;
    private Dictionary<string, WorldNpc> _npcs;
    private Dictionary<string, WorldPet> _originalPets;
    private Dictionary<string, WorldPet> _pets;
    private HashSet<string> _removedPetIds = new(StringComparer.Ordinal);
    private readonly List<PendingWorldPetPlacement> _pendingPetPlacements = [];
    private Dictionary<string, WorldDroppedItem> _originalDroppedItems;
    private Dictionary<string, WorldDroppedItem> _droppedItems;
    private HashSet<string> _removedDroppedItemIds = new(StringComparer.Ordinal);
    private readonly List<WorldDroppedItem> _pendingDroppedItems = [];
    private Dictionary<string, WorldVehicle> _originalVehicles;
    private Dictionary<string, WorldVehicle> _vehicles;
    private Dictionary<string, WorldDeployable> _originalDeployables;
    private Dictionary<string, WorldDeployable> _deployables;
    private string? _storyRow;
    private string? _originalStoryRow;
    private int? _minutesPassed;
    private int? _originalMinutesPassed;
    private double? _worldTimeSeconds;
    private double? _originalWorldTimeSeconds;
    private int? _worldDay;
    private int? _originalWorldDay;
    private int? _dayDiscovered;
    private int? _originalDayDiscovered;
    private Dictionary<string, string> _containments;
    private Dictionary<string, string> _originalContainments;
    // World-wide discovery (GlobalUnlocks struct, metadata save only): items seen, emails
    // read, journal pages found, and compendium entries, shared by every player in the world.
    // Staged additively per array prefix and only ever grows until Save/Revert.
    private readonly Dictionary<string, IReadOnlyList<string>> _stagedWorldUnlocks = new(StringComparer.Ordinal);
    // Feature implementations edit a SaveGame tree directly.  Keep that tree separate from
    // _data so a Razor session can still promise that Revert never changes the loaded save.
    // Building that second tree means re-reading the whole save, which on the big Facility
    // region costs about half a second and doubles the memory the open save holds, so it is
    // deferred until a feature or workbench editor actually asks for it. Most sessions never
    // open one, and the ones that do pay for it when the tab opens instead of at load.
    private WorldSaveData? _featureData;
    private readonly List<WorldMapFeatureOperation> _featureOperations = [];
    private readonly List<BenchUpgradeOperation> _benchUpgradeOperations = [];
    private readonly Dictionary<string, string> _rawEdits = new(StringComparer.Ordinal);

    /// <param name="files">
    /// Where the save is written back to. Null means write straight to the local file system
    /// (what the tests and any caller holding a real path expect); the hosts pass their own.
    /// </param>
    public WorldSaveSession(WorldSaveData data, string path, AbioticEditor.Web.Services.ISaveFileSystem? files = null)
    {
        _data = data;
        _path = path;
        _files = files;
        Flags = new HashSet<string>(data.Flags, StringComparer.Ordinal);
        _originalFlags = new HashSet<string>(Flags, StringComparer.Ordinal);
        GlobalRecipes = new HashSet<string>(data.GlobalRecipes, StringComparer.Ordinal);
        _originalGlobalRecipes = new HashSet<string>(GlobalRecipes, StringComparer.Ordinal);
        _doors = data.Doors.ToDictionary(door => door.Id, StringComparer.Ordinal);
        _originalDoors = new Dictionary<string, WorldDoor>(_doors, StringComparer.Ordinal);
        _containers = data.Containers.ToDictionary(ContainerKey, StringComparer.Ordinal);
        _originalContainers = new Dictionary<string, WorldContainer>(_containers, StringComparer.Ordinal);
        _npcs = data.Npcs.ToDictionary(npc => npc.Id, StringComparer.Ordinal);
        _originalNpcs = new Dictionary<string, WorldNpc>(_npcs, StringComparer.Ordinal);
        _pets = data.Pets.ToDictionary(pet => pet.Id, StringComparer.Ordinal);
        _originalPets = new Dictionary<string, WorldPet>(_pets, StringComparer.Ordinal);
        _droppedItems = data.DroppedItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        _originalDroppedItems = new Dictionary<string, WorldDroppedItem>(_droppedItems, StringComparer.Ordinal);
        _vehicles = data.Vehicles.ToDictionary(vehicle => vehicle.Id, StringComparer.Ordinal);
        _originalVehicles = new Dictionary<string, WorldVehicle>(_vehicles, StringComparer.Ordinal);
        _deployables = data.Deployables.ToDictionary(deployable => deployable.Id, StringComparer.Ordinal);
        _originalDeployables = new Dictionary<string, WorldDeployable>(_deployables, StringComparer.Ordinal);
        _storyRow = data.StoryProgressionRow;
        _originalStoryRow = _storyRow;
        _minutesPassed = data.MinutesPassed;
        _originalMinutesPassed = _minutesPassed;
        var clock = WorldSaveReader.ReadWorldClock(data.Raw);
        _worldTimeSeconds = _originalWorldTimeSeconds = clock?.Seconds;
        _worldDay = _originalWorldDay = clock?.Day;
        _dayDiscovered = _originalDayDiscovered = WorldSaveReader.ReadDayDiscovered(data.Raw);
        _containments = WorldSaveReader.ReadLeyakContainments(data.Raw)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        _originalContainments = new Dictionary<string, string>(_containments, StringComparer.OrdinalIgnoreCase);
        LastPlayedText = WorldSaveReader.ReadLastPlayedText(data.Raw);
    }

    /// <summary>The isolated tree the feature/bench editors patch, built on first use.</summary>
    private WorldSaveData FeatureData => _featureData ??= CloneForFeatures(_data);

    /// <summary>
    /// The tree to <b>read</b> feature state from: the staged one if any edit has been made,
    /// otherwise the loaded save itself.
    /// </summary>
    /// <remarks>
    /// Reading used to go through <see cref="FeatureData"/> too, which meant that merely opening
    /// a tab like BUTTONS or POWER SOCKETS built the isolated copy - and building it re-serializes
    /// and re-parses the entire save. On the ~16 MB facility world that was measured at 8.6
    /// seconds of frozen page, paid by whichever of those tabs the player happened to open first,
    /// for a screen they had not edited anything on.
    ///
    /// Nothing here mutates, so the loaded tree is a perfectly good source until an edit exists;
    /// once one does, the staged tree is the only one showing it, and this switches to it.
    /// </remarks>
    private UeSaveGame.SaveGame ReadableFeatureRaw => (_featureData ?? _data).Raw;

    public HashSet<string> Flags { get; private set; }
    /// <summary>World-wide unlocked/researched recipe rows (<c>GlobalUnlocks</c>, metadata save only).</summary>
    public HashSet<string> GlobalRecipes { get; private set; }
    public string Path => _path;
    public string JsonPath => _path + ".json";
    public bool JsonFileExists => File.Exists(JsonPath);
    public IReadOnlyList<WorldDoor> Doors => _doors.Values.OrderBy(door => door.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool CanEditFlags => _data.Raw.Properties.FindByPrefix("WorldFlags") is not null;
    public bool CanEditGlobalRecipes => _data.Raw.Properties.FindByPrefix("GlobalUnlocks") is not null;
    public bool CanEditDoors => _doors.Count > 0;
    public IReadOnlyList<WorldContainer> Containers => _containers.Values.OrderBy(container => container.Source).ThenBy(container => container.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<WorldNpc> Npcs => _npcs.Values.OrderBy(npc => npc.IsPet).ThenBy(npc => npc.ActorName, StringComparer.OrdinalIgnoreCase).ToArray();
    /// <summary>Tamed companions from the PetNPC map, with edits staged until save.</summary>
    public IReadOnlyList<WorldPet> Pets => _pets.Values.Concat(_pendingPetPlacements.Select(placement => placement.Pet))
        .OrderBy(pet => pet.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool CanEditContainers => _containers.Count > 0;
    public bool CanEditNpcs => _npcs.Values.Any(npc => !npc.IsPet);
    public IReadOnlyList<WorldDroppedItem> DroppedItems => _droppedItems.Values.Concat(_pendingDroppedItems).OrderBy(item => item.Slot.ItemId, StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<WorldVehicle> Vehicles => _vehicles.Values.OrderBy(vehicle => vehicle.Region).ThenBy(vehicle => vehicle.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<WorldDeployable> Deployables => _deployables.Values.OrderBy(deployable => deployable.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool CanEditDroppedItems => _data.Raw.Properties.FindByPrefix("DroppedItemMap") is not null;
    public bool CanEditVehicles => _vehicles.Count > 0;
    public bool IsMetadataSave => _data.StoryProgressionRow is not null;
    /// <summary>Formatted <c>LastPlayed</c> timestamp (metadata save; null elsewhere).</summary>
    public string? LastPlayedText { get; }
    public string? StoryProgressionRow => _storyRow;
    public int? MinutesPassed => _minutesPassed;
    public double? WorldTimeSeconds => _worldTimeSeconds;
    public int? WorldDay => _worldDay;
    public int? DayDiscovered => _dayDiscovered;
    public int ContainerCount => _containers.Count;
    public int NonEmptyContainerCount => _containers.Values.Count(container => container.Inventories.Any(inventory => inventory.Slots.Any(slot => !slot.IsEmpty)));
    public IReadOnlyList<KeyValuePair<string, string>> Containments => _containments.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    public bool CanEditContainments => _containments.Count > 0 || _data.Raw.Properties.FindByPrefix("LeyakContainmentIDs") is not null;
    /// <summary>The bulk world-unlock section only makes sense on the metadata save.</summary>
    public bool HasWorldUnlocks => IsMetadataSave;
    public int WorldItemsSeenCount => WorldUnlockCount(GlobalUnlockPrefix.Items);
    public int WorldEmailsReadCount => WorldUnlockCount(GlobalUnlockPrefix.Emails);
    public int WorldJournalsFoundCount => WorldUnlockCount(GlobalUnlockPrefix.Journals);
    public int WorldCompendiumUnlockedCount => WorldUnlockCount(GlobalUnlockPrefix.CompEmail)
        + WorldUnlockCount(GlobalUnlockPrefix.CompNarrative) + WorldUnlockCount(GlobalUnlockPrefix.CompExploration);
    public IReadOnlyList<RawSaveProperty> RawProperties => RawSavePropertyEditor.List(_data.Raw)
        .Select(property => _rawEdits.TryGetValue(property.Name, out var staged)
            ? property with { Value = staged } : property).ToArray();
    /// <summary>
    /// Which map-backed features this save carries, as name-only headers. Naming the world
    /// tabs needs nothing more, and decoding every feature's entries just to label a tab meant
    /// reading a thousand-plus entries on a region save before the editor could even be shown.
    /// </summary>
    public IReadOnlyList<WorldMapFeatureTab> MapFeatureTabs => WorldMapFeatures.ApplicableTo(_data.Raw)
        .Select(feature => new WorldMapFeatureTab(feature.Id, feature.DisplayName))
        .OrderBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Decoded state of one map-backed feature, read from this session's isolated staged save
    /// tree, or null when this save has no such map. Only the feature actually on screen is
    /// decoded: the tab holding four hundred resource nodes should not also pay for the five
    /// hundred power sockets next to it.
    /// </summary>
    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (WorldMapFeatures.Find(featureId) is not { } feature || !feature.AppliesTo(_data.Raw)) return null;
        return new WorldMapFeatureSnapshot(
            feature.Id,
            feature.DisplayName,
            feature.Description,
            feature.MapName,
            feature.SupportsRemoval,
            feature.RemoveActionLabel,
            feature.Read(ReadableFeatureRaw));
    }
    public bool IsDirty => !_originalFlags.SetEquals(Flags) || GlobalRecipesAreDirty() || DoorsAreDirty() || ContainersAreDirty() || NpcsAreDirty() || PetsAreDirty() || _pendingPetPlacements.Count > 0 || DroppedItemsAreDirty() || VehiclesAreDirty() || DeployablesAreDirty() || StoryIsDirty() || WorldTimeIsDirty() || ContainmentsAreDirty() || _featureOperations.Count > 0 || _benchUpgradeOperations.Count > 0 || _rawEdits.Count > 0 || _stagedWorldUnlocks.Count > 0;
    public string? Status { get; private set; }

    public void SetFlag(string flag, bool enabled)
    {
        if (enabled) Flags.Add(flag); else Flags.Remove(flag);
        UpdateStatus();
    }

    /// <summary>Stages a world-wide recipe row as unlocked/researched, or removes it.</summary>
    public void SetGlobalRecipe(string id, bool unlocked)
    {
        if (unlocked) GlobalRecipes.Add(id); else GlobalRecipes.Remove(id);
        UpdateStatus();
    }

    /// <summary>Stages every supplied recipe id as world-wide unlocked; used by the world recipes browser's unlock-all action.</summary>
    public int EnableGlobalRecipes(IEnumerable<string> ids)
    {
        var before = GlobalRecipes.Count;
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id))) GlobalRecipes.Add(id);
        UpdateStatus();
        return GlobalRecipes.Count - before;
    }

    public void SetWorldClock(double seconds, int day)
    {
        if (_worldTimeSeconds is null || _worldDay is null) return;
        _worldTimeSeconds = Math.Clamp(seconds, 0, 86400);
        _worldDay = Math.Max(0, day);
        UpdateStatus();
    }

    public void SetDayDiscovered(int day)
    {
        if (_dayDiscovered is null) return;
        _dayDiscovered = Math.Max(0, day);
        UpdateStatus();
    }

    public bool AddFlag(string? flag)
    {
        var trimmed = flag?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        SetFlag(trimmed, true);
        return true;
    }

    /// <summary>Stages a flag together with every curated story/quest prerequisite.</summary>
    public int EnableFlagWithPrerequisites(string flag)
    {
        var before = Flags.Count;
        foreach (var prerequisite in FlagGate.PrerequisitesFor(flag)) Flags.Add(prerequisite);
        Flags.Add(flag);
        UpdateStatus();
        return Flags.Count - before;
    }

    /// <summary>Stages every supplied flag; used by story/trader unlock actions.</summary>
    public int EnableFlags(IEnumerable<string> flags)
    {
        var before = Flags.Count;
        foreach (var flag in flags.Where(flag => !string.IsNullOrWhiteSpace(flag))) Flags.Add(flag);
        UpdateStatus();
        return Flags.Count - before;
    }

    /// <summary>Stages a stable simple-door state: Closed, Open, or Locked.</summary>
    public void SetSimpleDoorState(string id, string rawState)
    {
        if (_doors.TryGetValue(id, out var door) && door.Kind == WorldDoorKind.Simple)
        {
            _doors[id] = door with { DoorState = rawState };
            UpdateStatus();
        }
    }

    public void SetSecurityDoorOpen(string id, bool open)
    {
        if (_doors.TryGetValue(id, out var door) && door.Kind == WorldDoorKind.Security)
        {
            _doors[id] = door with { IsDoorOpen = open };
            UpdateStatus();
        }
    }

    public void SetOneWayUnlocked(string id, bool unlocked)
    {
        if (_doors.TryGetValue(id, out var door) && door.Kind == WorldDoorKind.Simple)
        {
            _doors[id] = door with { OneWayUnlocked = unlocked };
            UpdateStatus();
        }
    }

    /// <summary>Stages the "keep state (no auto-reset)" marker on a door of either kind.</summary>
    public void SetDoorNoReset(string id, bool noReset)
    {
        if (_doors.TryGetValue(id, out var door))
        {
            _doors[id] = door with { NoReset = noReset };
            UpdateStatus();
        }
    }

    public void SetAllSimpleDoors(string state) { foreach (var door in _doors.Values.Where(door => door.Kind == WorldDoorKind.Simple).ToArray()) _doors[door.Id] = door with { DoorState = state }; UpdateStatus(); }
    public void SetAllSecurityDoors(bool open) { foreach (var door in _doors.Values.Where(door => door.Kind == WorldDoorKind.Security).ToArray()) _doors[door.Id] = door with { IsDoorOpen = open }; UpdateStatus(); }
    public void SetAllDroppedNoDespawn(bool noDespawn) { foreach (var item in _droppedItems.Values.ToArray()) _droppedItems[item.Id] = item with { NoDespawn = noDespawn }; UpdateStatus(); }
    public void SetAllNarrativeNpcsDead(bool dead) { foreach (var npc in _npcs.Values.Where(npc => !npc.IsPet).ToArray()) _npcs[npc.Id] = npc with { IsDead = dead }; UpdateStatus(); }
    public void SetAllVehicles(bool driveable, bool destroyed) { foreach (var vehicle in _vehicles.Values.ToArray()) _vehicles[vehicle.Id] = vehicle with { Driveable = driveable, Destroyed = destroyed }; UpdateStatus(); }

    /// <summary>Stages a stack-count change while preserving every other slot field.</summary>
    public void SetContainerSlotCount(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, int count)
    {
        var key = $"{source}:{id}";
        if (!_containers.TryGetValue(key, out var container) || inventoryIndex < 0 || inventoryIndex >= container.Inventories.Count) return;
        var inventory = container.Inventories[inventoryIndex];
        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count) return;
        var slots = inventory.Slots.ToArray();
        slots[slotIndex] = slots[slotIndex] with { Count = Math.Max(0, count) };
        var inventories = container.Inventories.ToArray();
        inventories[inventoryIndex] = inventory with { Slots = slots };
        _containers[key] = container with { Inventories = inventories };
        UpdateStatus();
    }

    public bool TryGetContainerSlot(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, out InventoryItemSlot slot)
    {
        slot = default!;
        if (!TryGetContainerInventory(source, id, inventoryIndex, out var container, out var inventory)) return false;
        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count) return false;
        slot = inventory.Slots[slotIndex];
        return true;
    }

    public bool TrySetContainerSlot(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, InventoryItemSlot slot)
    {
        if (!TryGetContainerInventory(source, id, inventoryIndex, out var container, out var inventory)) return false;
        if (slotIndex < 0 || slotIndex >= inventory.Slots.Count) return false;
        var slots = inventory.Slots.ToArray();
        slots[slotIndex] = slot with { Index = slots[slotIndex].Index };
        ReplaceContainerInventory(source, id, container, inventoryIndex, inventory with { Slots = slots });
        UpdateStatus();
        return true;
    }

    public bool TrySwapContainerSlots(WorldContainerSource source, string id, int inventoryIndex, int firstIndex, int secondIndex)
    {
        if (!TryGetContainerSlot(source, id, inventoryIndex, firstIndex, out var first)
            || !TryGetContainerSlot(source, id, inventoryIndex, secondIndex, out var second)
            || firstIndex == secondIndex) return false;
        return TrySetContainerSlot(source, id, inventoryIndex, firstIndex, second)
            && TrySetContainerSlot(source, id, inventoryIndex, secondIndex, first);
    }

    public bool SortContainerSlots(WorldContainerSource source, string id, int inventoryIndex)
    {
        if (!TryGetContainerInventory(source, id, inventoryIndex, out var container, out var inventory)) return false;
        var original = inventory.Slots.ToArray();
        var ordered = original.OrderBy(slot => slot.IsEmpty).ThenBy(slot => slot.ItemId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(slot => slot.PlayerMadeString, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < ordered.Length; index++) ordered[index] = ordered[index] with { Index = original[index].Index };
        ReplaceContainerInventory(source, id, container, inventoryIndex, inventory with { Slots = ordered });
        UpdateStatus();
        return true;
    }

    /// <summary>Stages safe narrative-NPC fields supported by the world writer. Pets are deliberately excluded.</summary>
    public void SetNpc(string id, bool isDead, string? state, string? customName)
    {
        if (_npcs.TryGetValue(id, out var npc) && !npc.IsPet)
        {
            _npcs[id] = npc with { IsDead = isDead, State = state, CustomName = customName?.Trim() };
            UpdateStatus();
        }
    }

    /// <summary>Stages a pet's persisted fields. Limb keys are retained exactly as read.</summary>
    public void SetPet(string id, bool isDead, string? npcClass, string? customName, int xp, IReadOnlyDictionary<string, double> limbHealth)
    {
        if (_pets.TryGetValue(id, out var pet))
        {
            _pets[id] = pet with
            {
                IsDead = isDead,
                NpcClass = string.IsNullOrWhiteSpace(npcClass) ? pet.NpcClass : npcClass.Trim(),
                CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim(),
                Xp = Math.Max(0, xp),
                LimbHealth = limbHealth.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value), StringComparer.Ordinal),
            };
            UpdateStatus();
        }
    }

    /// <summary>Stages removal of a pet. The map entry is only removed when the session is saved.</summary>
    public void RemovePet(string id)
    {
        if (_pets.Remove(id)) { _removedPetIds.Add(id); UpdateStatus(); }
    }

    /// <summary>Un-stages a pending pet removal (the UNDO DELETE action of the PETS tab).</summary>
    public bool RestorePet(WorldPet pet)
    {
        if (!_removedPetIds.Remove(pet.Id)) return false;
        _pets[pet.Id] = pet;
        UpdateStatus();
        return true;
    }

    /// <summary>Stages placing a carried pet into this world; the Core writer creates its map entry on Save.</summary>
    public bool TryPlaceCarriedPet(CarriedPet pet, double x, double y, double z, out string message)
    {
        var npcClass = PetItemCatalog.NpcClassFor(pet.ItemRow);
        if (npcClass is null) { message = $"No world creature class is known for '{pet.ItemRow}'."; return false; }
        if (_pets.Count == 0 && _npcs.Count == 0) { message = "This world needs an existing pet or NPC so the save writer can clone a valid creature entry."; return false; }
        var staged = new WorldPet($"pending-{Guid.NewGuid():N}", false, npcClass, x, y, z, pet.Name,
            new Dictionary<string, double>(), pet.Xp, null);
        _pendingPetPlacements.Add(new PendingWorldPetPlacement(staged, pet.Health > 0 ? pet.Health : null));
        UpdateStatus();
        message = $"Staged {pet.DisplayName} at {x:0}, {y:0}, {z:0}. Save this world before removing it from the player save.";
        return true;
    }

    public void SetDroppedItem(string id, int count, bool noDespawn)
    {
        if (_droppedItems.TryGetValue(id, out var item))
        {
            _droppedItems[id] = item with { Slot = item.Slot with { Count = Math.Max(0, count) }, NoDespawn = noDespawn };
            UpdateStatus();
        }
    }

    public void RemoveDroppedItem(string id)
    {
        var pending = _pendingDroppedItems.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (pending >= 0) { _pendingDroppedItems.RemoveAt(pending); UpdateStatus(); return; }
        if (_droppedItems.Remove(id)) { _removedDroppedItemIds.Add(id); UpdateStatus(); }
    }

    /// <summary>Stages a new ground item. Save uses Core's clone-an-existing-entry writer.</summary>
    public bool TryAddDroppedItem(InventoryItemSlot slot, double x, double y, double z, out string pendingId)
    {
        pendingId = string.Empty;
        if (!CanEditDroppedItems || _data.DroppedItems.Count == 0 || slot.IsEmpty) return false;
        pendingId = $"pending-{Guid.NewGuid():N}";
        _pendingDroppedItems.Add(new WorldDroppedItem(pendingId, slot, true, x, y, z));
        UpdateStatus();
        return true;
    }

    /// <summary>Restores an existing staged removal when a coordinated transfer rolls back.</summary>
    public bool RestoreDroppedItem(WorldDroppedItem item)
    {
        if (!_removedDroppedItemIds.Remove(item.Id)) return false;
        _droppedItems[item.Id] = item;
        UpdateStatus();
        return true;
    }

    public void SetVehicle(string id, bool driveable, bool destroyed, double x, double y, double z)
    {
        if (_vehicles.TryGetValue(id, out var vehicle))
        {
            _vehicles[id] = vehicle with { Driveable = driveable, Destroyed = destroyed, X = x, Y = y, Z = z };
            UpdateStatus();
        }
    }

    public void SetDeployableCustomName(string id, string? customName)
    {
        if (_deployables.TryGetValue(id, out var deployable))
        {
            _deployables[id] = deployable with { CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim() };
            UpdateStatus();
        }
    }
    public void ClearDeployableCustomNames(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            if (_deployables.TryGetValue(id, out var deployable)) _deployables[id] = deployable with { CustomName = null };
        UpdateStatus();
    }

    public void SetStoryProgression(string? row)
    {
        if (!IsMetadataSave) return;
        _storyRow = row?.Trim();
        UpdateStatus();
    }

    public void SetMinutesPassed(int minutes)
    {
        if (!IsMetadataSave) return;
        _minutesPassed = Math.Max(0, minutes);
        UpdateStatus();
    }

    public void ReleaseContainment(string creature)
    {
        if (_containments.Remove(creature)) UpdateStatus();
    }

    // ---------- containment units (the deployables the creatures sit in) ----------

    /// <summary>
    /// Every containment unit deployed anywhere in this world, surveyed once off-thread. The
    /// units live in the region saves, not in this one, so the survey has to read the sibling
    /// files; the result is cached for the life of the session because a unit can only be built
    /// or picked up in-game, never from here.
    /// </summary>
    public IReadOnlyList<WorldContainmentUnit> ContainmentUnits => _containmentSurvey?.Units ?? [];

    /// <summary>Creature -> unit id entries pointing at a unit no region save contains.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> OrphanedContainments
        => _containmentSurvey?.OrphanedAssignments ?? [];

    /// <summary>Region saves the unit survey could not read, so the list may be incomplete.</summary>
    public IReadOnlyList<string> ContainmentScanFailures => _containmentSurvey?.UnreadableSaves ?? [];

    /// <summary>True once <see cref="LoadContainmentUnitsAsync"/> has finished a survey.</summary>
    public bool ContainmentUnitsLoaded => _containmentSurvey is not null;

    private ContainmentSurvey? _containmentSurvey;
    private Task? _containmentSurveyTask;

    /// <summary>
    /// Surveys the world folder for containment units. Safe to call from every render: the scan
    /// runs once and later calls await the same task.
    /// </summary>
    public Task LoadContainmentUnitsAsync()
        => _containmentSurveyTask ??= Task.Run(() =>
        {
            _containmentSurvey = ContainmentDirectory.Survey(_path);
        });

    /// <summary>The creature staged into a given unit, or null when the unit is empty.</summary>
    public string? CreatureInUnit(string unitId)
    {
        foreach (var pair in _containments)
        {
            if (string.Equals(pair.Value, unitId, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        }
        return null;
    }

    /// <summary>
    /// Stages which creature a unit holds. Passing null empties it. Because the save's map is
    /// keyed by creature, moving a creature into a unit automatically takes it out of whatever
    /// unit it was in before; anything already sitting in the target unit is turned loose.
    /// </summary>
    public void SetContainmentUnitOccupant(string unitId, string? creature)
    {
        if (string.IsNullOrWhiteSpace(unitId)) return;

        foreach (var evicted in _containments.Where(pair => string.Equals(pair.Value, unitId, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key).ToList())
        {
            _containments.Remove(evicted);
        }
        if (!string.IsNullOrWhiteSpace(creature)) _containments[creature] = unitId;
        UpdateStatus();
    }

    /// <summary>
    /// Exchanges the occupants of two units in one step (the Leyak takes the Krasue's cell and
    /// vice versa). Works when only one of the two is occupied - the creature simply moves.
    /// </summary>
    public void SwapContainmentUnits(string unitIdA, string unitIdB)
    {
        if (string.IsNullOrWhiteSpace(unitIdA) || string.IsNullOrWhiteSpace(unitIdB)
            || string.Equals(unitIdA, unitIdB, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var inA = CreatureInUnit(unitIdA);
        var inB = CreatureInUnit(unitIdB);
        if (inA is null && inB is null) return;

        if (inA is not null) _containments[inA] = unitIdB;
        if (inB is not null) _containments[inB] = unitIdA;
        UpdateStatus();
    }

    /// <summary>World-wide (<c>GlobalUnlocks</c>) array prefixes, metadata save only.</summary>
    private static class GlobalUnlockPrefix
    {
        public const string Items = "GlobalItemsPickedUp_";
        public const string Emails = "GlobalEmailsRead_";
        public const string Journals = "GlobalJournalEntries_";
        public const string CompEmail = "GlobalCompendiumEmail_";
        public const string CompNarrative = "GlobalCompendiumNarrative_";
        public const string CompExploration = "GlobalCompendiumExploration_";
    }

    private int WorldUnlockCount(string prefix) => _stagedWorldUnlocks.TryGetValue(prefix, out var staged)
        ? staged.Count
        : WorldSaveReader.ReadGlobalUnlockArray(_data.Raw, prefix).Count;

    /// <summary>Stages every supplied catalog item id as world-wide picked-up; returns the number newly added.</summary>
    public int EnableWorldItemsSeen(IEnumerable<string> ids) => StageWorldUnlock(GlobalUnlockPrefix.Items, ids);

    /// <summary>Stages every supplied email id as world-wide read; returns the number newly added.</summary>
    public int EnableWorldEmailsRead(IEnumerable<string> ids) => StageWorldUnlock(GlobalUnlockPrefix.Emails, ids);

    /// <summary>Stages every supplied journal id as world-wide found; returns the number newly added.</summary>
    public int EnableWorldJournalsFound(IEnumerable<string> ids) => StageWorldUnlock(GlobalUnlockPrefix.Journals, ids);

    /// <summary>
    /// Stages every supplied compendium entry as world-wide unlocked, split into the save's
    /// three arrays by unlock type (email / narrative / exploration). Returns the number newly added.
    /// </summary>
    public int EnableWorldCompendium(IEnumerable<string> emailIds, IEnumerable<string> narrativeIds, IEnumerable<string> explorationIds)
    {
        var added = StageWorldUnlock(GlobalUnlockPrefix.CompEmail, emailIds);
        added += StageWorldUnlock(GlobalUnlockPrefix.CompNarrative, narrativeIds);
        added += StageWorldUnlock(GlobalUnlockPrefix.CompExploration, explorationIds);
        return added;
    }

    private int StageWorldUnlock(string prefix, IEnumerable<string> ids)
    {
        if (!IsMetadataSave) return 0;
        var current = _stagedWorldUnlocks.TryGetValue(prefix, out var staged) ? staged : WorldSaveReader.ReadGlobalUnlockArray(_data.Raw, prefix);
        var merged = current.ToList();
        var seen = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var before = merged.Count;
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            if (seen.Add(id)) merged.Add(id);
        if (merged.Count == before) return 0;
        _stagedWorldUnlocks[prefix] = merged;
        UpdateStatus();
        return merged.Count - before;
    }

    /// <summary>Stages an existing primitive top-level property after validation on an isolated clone.</summary>
    public bool TryStageRawEdit(string name, string? value, out string? error)
    {
        var candidate = CloneForFeatures(_data);
        if (!RawSavePropertyEditor.TryApply(candidate.Raw, name, value, out error)) return false;
        _rawEdits[name] = value ?? string.Empty;
        UpdateStatus();
        return true;
    }

    public void DiscardRawEdits() { _rawEdits.Clear(); UpdateStatus(); }

    /// <summary>Exports the complete save JSON beside the save for editing with an external tool.</summary>
    public Task ExportJsonToFileAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => SaveJsonBridge.ExportJsonToFile(_data.Raw, JsonPath), cancellationToken);

    /// <summary>
    /// The same JSON as bytes, for hosts that have nowhere to write a file beside the save.
    /// A browser has no folder to put it in, so it hands these bytes to the player as a download.
    /// </summary>
    public Task<byte[]> ExportJsonBytesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => System.Text.Encoding.UTF8.GetBytes(SaveJsonBridge.ToJson(_data.Raw)), cancellationToken);

    /// <summary>Imports the JSON file sitting beside this save, through Core's validated conversion and backup path.</summary>
    public Task ImportJsonFromFileAsync(CancellationToken cancellationToken = default)
        => ImportJsonFromFileAsync(JsonPath, cancellationToken);

    /// <summary>
    /// Imports complete save JSON from any chosen file, replacing this save (a .bak is kept).
    /// The JSON need not be the copy exported beside the save: an edited copy kept anywhere,
    /// or one taken from another machine, works too.
    /// </summary>
    /// <remarks>
    /// The conversion happens off the caller's thread and the result is handed to the host's own
    /// file system, so this works in a browser too. It used to write straight to a disk path,
    /// which a browser tab does not have - the import failed there with a complaint about a
    /// folder that never existed.
    /// </remarks>
    public async Task ImportJsonFromFileAsync(string jsonPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        var bytes = await Task.Run(() => SaveJsonBridge.ReadJsonAsSaveBytes(jsonPath), cancellationToken).ConfigureAwait(false);
        await AbioticEditor.Web.Services.SaveFilePersistence
            .WriteBytesAsync(_files, _path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when this deployable can carry bench upgrade modules (its gameplay-tag container exists).</summary>
    public bool BenchSupportsUpgrades(string deployableId)
        => WorldMapAccessor.FindEntry(ReadableFeatureRaw, "DeployedObjectMap", deployableId) is { } props
            && BenchUpgradeCatalog.SupportsUpgrades(props);

    /// <summary>The upgrade rows currently installed on a bench, staged edits included.</summary>
    public IReadOnlyList<string> BenchInstalledUpgrades(string deployableId)
        => WorldMapAccessor.FindEntry(ReadableFeatureRaw, "DeployedObjectMap", deployableId) is { } props
            ? BenchUpgradeCatalog.ReadInstalledRows(props)
            : Array.Empty<string>();

    /// <summary>Stages installing/removing one bench upgrade module; applied to the file on save.</summary>
    public bool SetBenchUpgrade(string deployableId, string row, bool installed)
    {
        if (WorldMapAccessor.FindEntry(FeatureData.Raw, "DeployedObjectMap", deployableId) is not { } props
            || !BenchUpgradeCatalog.SetInstalled(props, row, installed)) return false;
        _benchUpgradeOperations.Add(new BenchUpgradeOperation(deployableId, row, installed));
        UpdateStatus();
        return true;
    }

    /// <summary>Stages a generic map-feature field edit without mutating the loaded save.</summary>
    public WorldEditResult SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        var feature = WorldMapFeatures.Find(featureId);
        if (feature is null) return RefuseUnknownFeature(featureId);
        var result = feature.SetField(FeatureData.Raw, entryKey, fieldId, value);
        if (result.Changed)
        {
            _featureOperations.Add(WorldMapFeatureOperation.Set(featureId, entryKey, fieldId, value));
            UpdateStatus();
        }
        return result;
    }

    /// <summary>Stages removal/reset of a generic map-feature entry.</summary>
    public WorldEditResult RemoveMapFeatureEntry(string featureId, string entryKey)
    {
        var feature = WorldMapFeatures.Find(featureId);
        if (feature is null) return RefuseUnknownFeature(featureId);
        var result = feature.Remove(FeatureData.Raw, entryKey);
        if (result.Changed)
        {
            _featureOperations.Add(WorldMapFeatureOperation.Remove(featureId, entryKey));
            UpdateStatus();
        }
        return result;
    }

    /// <summary>
    /// Refuses an edit aimed at a part of the world this build does not know how to change.
    /// The player only ever sees a plain "that change was not accepted" from the screen that
    /// asked; the internal name is written to the log, where it is useful for diagnosis and
    /// where nobody has to read it. The refusal deliberately carries no explanation of its own
    /// so the screen falls back to its own translated wording.
    /// </summary>
    private static WorldEditResult RefuseUnknownFeature(string featureId)
    {
        AbioticEditor.Core.Diagnostics.EditorLog.Warn(
            "WorldFeatures", $"No world-map feature is registered for '{featureId}'; the edit was refused.");
        return WorldEditResult.Failure(string.Empty);
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsDirty) return;
        // Apply all staged state to a disposable clone. A rejected writer or failed disk write
        // therefore cannot leak partial mutations into this session's baseline.
        var workingData = CloneForFeatures(_data);
        if (!_originalFlags.SetEquals(Flags))
        {
            if (!CanEditFlags) throw new InvalidOperationException("This save does not contain an editable WorldFlags array.");
            WorldSaveWriter.ApplyFlags(workingData, Flags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray());
        }
        if (GlobalRecipesAreDirty())
        {
            if (!CanEditGlobalRecipes) throw new InvalidOperationException("This save does not contain an editable GlobalUnlocks recipe list.");
            WorldSaveWriter.ApplyGlobalRecipes(workingData, GlobalRecipes.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }
        if (DoorsAreDirty()) WorldSaveWriter.ApplyDoors(workingData, _doors.Values);
        if (ContainersAreDirty()) WorldSaveWriter.ApplyContainers(workingData, _containers.Values);
        if (NpcsAreDirty()) WorldSaveWriter.ApplyNpcs(workingData, _npcs.Values.Where(npc => !npc.IsPet));
        if (PetsAreDirty())
        {
            WorldSaveWriter.ApplyPets(workingData, _pets.Values);
            foreach (var id in _removedPetIds)
                if (!WorldSaveWriter.RemovePet(workingData, id))
                    throw new InvalidOperationException($"Could not remove pet '{id}'.");
        }
        foreach (var placement in _pendingPetPlacements)
            if (WorldSaveWriter.AddPet(workingData, placement.Pet with { Id = string.Empty }, placement.TotalHealth) is null)
                throw new InvalidOperationException($"Could not place pet '{placement.Pet.DisplayName}'.");
        if (DroppedItemsAreDirty())
        {
            WorldSaveWriter.ApplyDroppedItems(workingData, _droppedItems.Values);
            WorldSaveWriter.RemoveDroppedItems(workingData, _removedDroppedItemIds);
        }
        var addedDroppedItems = new List<WorldDroppedItem>();
        foreach (var pending in _pendingDroppedItems)
        {
            var id = WorldSaveWriter.AddDroppedItem(workingData, pending.Slot, pending.X, pending.Y, pending.Z, pending.NoDespawn)
                ?? throw new InvalidOperationException($"Could not place dropped item '{pending.Slot.ItemId}' because this world has no ground-item template.");
            addedDroppedItems.Add(pending with { Id = id });
        }
        if (VehiclesAreDirty()) WorldSaveWriter.ApplyVehicles(workingData, _vehicles.Values);
        if (DeployablesAreDirty())
        {
            foreach (var deployable in _deployables.Values)
            {
                if (_originalDeployables.TryGetValue(deployable.Id, out var original) && original.CustomName != deployable.CustomName
                    && !WorldSaveWriter.ApplyDeployableCustomText(workingData, deployable.Id, deployable.CustomName ?? string.Empty))
                    throw new InvalidOperationException($"Deployable '{deployable.DisplayName}' does not support custom text edits.");
            }
        }
        if (StoryIsDirty())
        {
            WorldSaveWriter.ApplyStoryProgression(workingData, _storyRow ?? string.Empty);
            if (_minutesPassed.HasValue) WorldSaveWriter.ApplyMinutesPassed(workingData, _minutesPassed.Value);
        }
        if (WorldTimeIsDirty())
        {
            if (_worldTimeSeconds.HasValue && _worldDay.HasValue)
                WorldSaveWriter.ApplyWorldClock(workingData, _worldTimeSeconds.Value, _worldDay.Value);
            if (_dayDiscovered.HasValue)
                WorldSaveWriter.ApplyDayDiscovered(workingData, _dayDiscovered.Value);
        }
        var containmentsChanged = ContainmentsAreDirty();
        if (containmentsChanged)
        {
            foreach (var creature in _originalContainments.Keys.Where(creature => !_containments.ContainsKey(creature)))
            {
                if (!WorldSaveWriter.RemoveLeyakContainment(workingData, creature))
                    throw new InvalidOperationException($"Could not release containment '{creature}'.");
            }
            foreach (var (creature, unitId) in _containments)
            {
                if (_originalContainments.TryGetValue(creature, out var was)
                    && string.Equals(was, unitId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!WorldSaveWriter.SetLeyakContainment(workingData, creature, unitId))
                    throw new InvalidOperationException($"Could not move '{creature}' into containment unit '{unitId}'.");
            }
        }
        foreach (var (prefix, values) in _stagedWorldUnlocks)
        {
            if (!WorldSaveWriter.ApplyGlobalUnlockArray(workingData, prefix, values))
                throw new InvalidOperationException($"Could not apply world unlock '{prefix}'.");
        }
        // Replay only accepted edits onto the real tree immediately before writing. This keeps
        // all feature changes transactional, and lets the specialised writers run first.
        foreach (var operation in _featureOperations)
        {
            var feature = WorldMapFeatures.Find(operation.FeatureId)
                ?? throw new InvalidOperationException($"Map feature '{operation.FeatureId}' is no longer available.");
            var result = operation.IsRemoval
                ? feature.Remove(workingData.Raw, operation.EntryKey)
                : feature.SetField(workingData.Raw, operation.EntryKey, operation.FieldId!, operation.Value);
            if (result.IsError)
                throw new InvalidOperationException($"Could not apply {feature.DisplayName} edit: {result.Error}");
        }
        foreach (var operation in _benchUpgradeOperations)
        {
            if (WorldMapAccessor.FindEntry(workingData.Raw, "DeployedObjectMap", operation.DeployableId) is not { } props)
                throw new InvalidOperationException($"Bench '{operation.DeployableId}' is no longer present in this save.");
            BenchUpgradeCatalog.SetInstalled(props, operation.Row, operation.Installed);
        }
        foreach (var edit in _rawEdits)
            if (!RawSavePropertyEditor.TryApply(workingData.Raw, edit.Key, edit.Value, out var error))
                throw new InvalidOperationException($"Raw edit '{edit.Key}' is no longer valid: {error}");
        await AbioticEditor.Web.Services.SaveFilePersistence
            .WriteAsync(_files, _path, workingData.Raw, cancellationToken).ConfigureAwait(false);
        // A unit also keeps its own note of which creature it holds, and that note lives in the
        // region save the unit stands in - a different file from this one. Bring those back in
        // line after the map itself is safely written, so a failure here can never leave the
        // metadata save half-updated. Units that were already correct are not rewritten.
        //
        // This reaches sibling saves by local path, so it only runs where such paths exist. A
        // browser-hosted workspace skips it rather than silently writing nothing (the
        // containment tab is gated on the same capability).
        var canReachSiblingSaves = _files is null || _files.HasLocalPaths;
        var unitSync = containmentsChanged && canReachSiblingSaves
            ? ContainmentDirectory.SyncUnitRecords(_path, _containments)
            : new ContainmentDirectory.SyncResult(0, [], []);
        if (unitSync.FilesWritten.Count > 0 && _containmentSurvey is { } survey)
        {
            // The rewritten region saves now agree with the map just written, and we know
            // exactly what they say, so the survey is corrected in place. Discarding it made
            // the tab re-read every region save immediately after a save and sit on "looking
            // through the world for containment units" again, which reads like nothing saved.
            _containmentSurvey = survey with
            {
                Units = survey.Units
                    .Select(unit =>
                    {
                        var creature = CreatureInUnit(unit.Id);
                        var index = ContainmentCreatureCatalog.IndexOf(creature);
                        return unit with
                        {
                            Creature = creature,
                            StoredCreatureIndex = index >= 0 ? index : unit.StoredCreatureIndex,
                        };
                    })
                    .ToArray(),
            };
        }
        _data = workingData;
        _originalFlags = new HashSet<string>(Flags, StringComparer.Ordinal);
        _originalGlobalRecipes = new HashSet<string>(GlobalRecipes, StringComparer.Ordinal);
        _originalDoors = new Dictionary<string, WorldDoor>(_doors, StringComparer.Ordinal);
        _originalContainers = new Dictionary<string, WorldContainer>(_containers, StringComparer.Ordinal);
        _originalNpcs = new Dictionary<string, WorldNpc>(_npcs, StringComparer.Ordinal);
        _originalPets = new Dictionary<string, WorldPet>(_pets, StringComparer.Ordinal);
        _removedPetIds.Clear();
        _pendingPetPlacements.Clear();
        foreach (var item in addedDroppedItems) _droppedItems[item.Id] = item;
        _originalDroppedItems = new Dictionary<string, WorldDroppedItem>(_droppedItems, StringComparer.Ordinal);
        _removedDroppedItemIds.Clear();
        _pendingDroppedItems.Clear();
        _originalVehicles = new Dictionary<string, WorldVehicle>(_vehicles, StringComparer.Ordinal);
        _originalDeployables = new Dictionary<string, WorldDeployable>(_deployables, StringComparer.Ordinal);
        _originalStoryRow = _storyRow;
        _originalMinutesPassed = _minutesPassed;
        _originalWorldTimeSeconds = _worldTimeSeconds;
        _originalWorldDay = _worldDay;
        _originalDayDiscovered = _dayDiscovered;
        _originalContainments = new Dictionary<string, string>(_containments, StringComparer.OrdinalIgnoreCase);
        _stagedWorldUnlocks.Clear();
        _featureOperations.Clear();
        _benchUpgradeOperations.Clear();
        _rawEdits.Clear();
        // Drop the feature tree rather than rebuild it: the next feature tab that needs one
        // rebuilds it from the just-saved data, and a session that never opens one never pays.
        _featureData = null;
        Status = unitSync.FilesWritten.Count > 0
            ? $"Saved (a .bak backup was created). Also updated {string.Join(", ", unitSync.FilesWritten)} so the containment units match."
            : "Saved (a .bak backup was created).";
    }

    public void Revert()
    {
        Flags = new HashSet<string>(_originalFlags, StringComparer.Ordinal);
        GlobalRecipes = new HashSet<string>(_originalGlobalRecipes, StringComparer.Ordinal);
        _doors = new Dictionary<string, WorldDoor>(_originalDoors, StringComparer.Ordinal);
        _containers = new Dictionary<string, WorldContainer>(_originalContainers, StringComparer.Ordinal);
        _npcs = new Dictionary<string, WorldNpc>(_originalNpcs, StringComparer.Ordinal);
        _pets = new Dictionary<string, WorldPet>(_originalPets, StringComparer.Ordinal);
        _removedPetIds.Clear();
        _pendingPetPlacements.Clear();
        _droppedItems = new Dictionary<string, WorldDroppedItem>(_originalDroppedItems, StringComparer.Ordinal);
        _removedDroppedItemIds.Clear();
        _pendingDroppedItems.Clear();
        _vehicles = new Dictionary<string, WorldVehicle>(_originalVehicles, StringComparer.Ordinal);
        _deployables = new Dictionary<string, WorldDeployable>(_originalDeployables, StringComparer.Ordinal);
        _storyRow = _originalStoryRow;
        _minutesPassed = _originalMinutesPassed;
        _worldTimeSeconds = _originalWorldTimeSeconds;
        _worldDay = _originalWorldDay;
        _dayDiscovered = _originalDayDiscovered;
        _containments = new Dictionary<string, string>(_originalContainments, StringComparer.OrdinalIgnoreCase);
        _stagedWorldUnlocks.Clear();
        _featureOperations.Clear();
        _benchUpgradeOperations.Clear();
        _rawEdits.Clear();
        _featureData = null;
        Status = "Changes reverted.";
    }

    private bool GlobalRecipesAreDirty() => !_originalGlobalRecipes.SetEquals(GlobalRecipes);
    private bool DoorsAreDirty() => _doors.Count != _originalDoors.Count ||
        _doors.Any(pair => !_originalDoors.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool ContainersAreDirty() => _containers.Count != _originalContainers.Count ||
        _containers.Any(pair => !_originalContainers.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool NpcsAreDirty() => _npcs.Count != _originalNpcs.Count ||
        _npcs.Any(pair => !_originalNpcs.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool PetsAreDirty() => _removedPetIds.Count > 0 || _pets.Count != _originalPets.Count ||
        _pets.Any(pair => !_originalPets.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool DroppedItemsAreDirty() => _pendingDroppedItems.Count > 0 || _removedDroppedItemIds.Count > 0 || _droppedItems.Count != _originalDroppedItems.Count ||
        _droppedItems.Any(pair => !_originalDroppedItems.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool VehiclesAreDirty() => _vehicles.Count != _originalVehicles.Count ||
        _vehicles.Any(pair => !_originalVehicles.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool DeployablesAreDirty() => _deployables.Count != _originalDeployables.Count ||
        _deployables.Any(pair => !_originalDeployables.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private bool StoryIsDirty() => !string.Equals(_storyRow, _originalStoryRow, StringComparison.Ordinal) || _minutesPassed != _originalMinutesPassed;
    private bool WorldTimeIsDirty() => _worldTimeSeconds != _originalWorldTimeSeconds || _worldDay != _originalWorldDay || _dayDiscovered != _originalDayDiscovered;
    private bool ContainmentsAreDirty() => _containments.Count != _originalContainments.Count ||
        _containments.Any(pair => !_originalContainments.TryGetValue(pair.Key, out var original) || original != pair.Value);
    private static string ContainerKey(WorldContainer container) => $"{container.Source}:{container.Id}";

    private bool TryGetContainerInventory(WorldContainerSource source, string id, int inventoryIndex, out WorldContainer container, out WorldInventory inventory)
    {
        inventory = default!;
        if (!_containers.TryGetValue($"{source}:{id}", out container!) || inventoryIndex < 0 || inventoryIndex >= container.Inventories.Count) return false;
        inventory = container.Inventories[inventoryIndex];
        return true;
    }

    private void ReplaceContainerInventory(WorldContainerSource source, string id, WorldContainer container, int inventoryIndex, WorldInventory inventory)
    {
        var inventories = container.Inventories.ToArray();
        inventories[inventoryIndex] = inventory;
        _containers[$"{source}:{id}"] = container with { Inventories = inventories };
    }

    private static WorldSaveData CloneForFeatures(WorldSaveData source)
    {
        using var stream = new MemoryStream();
        source.Raw.WriteTo(stream);
        stream.Position = 0;
        return WorldSaveReader.ReadFromStream(stream);
    }

    private void UpdateStatus() => Status = IsDirty ? "Unsaved changes" : null;
}

internal sealed record WorldMapFeatureOperation(string FeatureId, string EntryKey, string? FieldId, string? Value, bool IsRemoval)
{
    public static WorldMapFeatureOperation Set(string featureId, string entryKey, string fieldId, string? value)
        => new(featureId, entryKey, fieldId, value, false);

    public static WorldMapFeatureOperation Remove(string featureId, string entryKey)
        => new(featureId, entryKey, null, null, true);
}

internal sealed record PendingWorldPetPlacement(WorldPet Pet, double? TotalHealth);

/// <summary>One staged install/remove of a bench upgrade module, replayed onto the tree on save.</summary>
internal sealed record BenchUpgradeOperation(string DeployableId, string Row, bool Installed);

/// <summary>A map feature this save carries, named but not yet decoded (used to build the tab strip).</summary>
public sealed record WorldMapFeatureTab(string Id, string DisplayName);

/// <summary>Read-only, host-neutral snapshot of a typed Core world-map feature.</summary>
public sealed record WorldMapFeatureSnapshot(
    string Id,
    string DisplayName,
    string Description,
    string MapName,
    bool SupportsRemoval,
    string RemoveActionLabel,
    IReadOnlyList<WorldMapEntry> Entries);
