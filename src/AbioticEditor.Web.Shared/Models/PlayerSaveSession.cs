using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Models;

/// <summary>
/// A Razor-hosted editing session for one player save.  It deliberately depends only on
/// Core save types: neither this class nor callers need a native view model.
/// </summary>
public sealed class PlayerSaveSession : IPlayerVitalsSession
{
    private readonly PlayerSaveData _data;
    private readonly string _path;
    private readonly AbioticEditor.Web.Services.ISaveFileSystem? _files;
    private PlayerVitals _original;
    private HashSet<string> _originalRecipes;
    private List<string> _originalTraits;
    private string? _originalPhd;
    private HashSet<string> _originalItemsPickedUp;
    private HashSet<string> _originalCraftedItems;
    private HashSet<string> _originalMapsUnlocked;
    private PlayerRespawnEdit _originalRespawn = null!;
    private HashSet<string> _originalEmails;
    private HashSet<string> _originalJournals;
    private HashSet<string> _originalCompendium;
    private HashSet<string> _originalFish;
    private Dictionary<string, int> _originalKills;
    private readonly Func<string, object?[], string>? _codexLocalize;
    // Exact-name primitive overrides from the Raw tab. They are validated on a cloned
    // tree, then applied only to the writer's working data at Save time.
    private readonly Dictionary<string, string> _rawEdits = new(StringComparer.Ordinal);

    /// <param name="files">
    /// Where the save is written back to. Null means write straight to the local file system,
    /// which is what the tests and any caller that already holds a real path expect; the hosts
    /// pass their own so the browser can write through the browser's file APIs instead.
    /// </param>
    public PlayerSaveSession(PlayerSaveData data, string path, IEnumerable<string>? recipeVocabulary = null,
        IEnumerable<string>? itemVocabulary = null, IEnumerable<string>? mapVocabulary = null,
        CodexVocabulary? codexVocabulary = null, ItemUpgradeCatalog? itemUpgrades = null,
        Func<string, object?[], string>? codexLocalize = null,
        AbioticEditor.Web.Services.ISaveFileSystem? files = null)
    {
        _codexLocalize = codexLocalize;
        _files = files;
        _data = data;
        _path = path;
        Vitals = ToVitals(data);
        _original = Vitals.Clone();
        Skills = data.Skills.OrderBy(skill => skill.Index)
            .Select(skill => new PlayerSkillEdit(skill, SkillDefinitionFor(skill.Index))).ToList();
        _recipes = BuildRecipes(data.Recipes, recipeVocabulary);
        _originalRecipes = CurrentRecipeSet();
        Traits = data.Traits.ToList();
        _originalTraits = Traits.ToList();
        Background = data.Phd;
        _originalPhd = Background;
        HasRecipeVocabulary = recipeVocabulary?.Any() == true;
        ItemVocabulary = BuildVocabulary(data.ItemsPickedUp.Concat(data.CraftedItems), itemVocabulary);
        MapVocabulary = BuildVocabulary(data.MapsUnlocked, mapVocabulary);
        ItemsPickedUp = data.ItemsPickedUp.ToHashSet(StringComparer.Ordinal);
        CraftedItems = data.CraftedItems.ToHashSet(StringComparer.Ordinal);
        MapsUnlocked = data.MapsUnlocked.ToHashSet(StringComparer.Ordinal);
        _originalItemsPickedUp = new(ItemsPickedUp, StringComparer.Ordinal);
        _originalCraftedItems = new(CraftedItems, StringComparer.Ordinal);
        _originalMapsUnlocked = new(MapsUnlocked, StringComparer.Ordinal);
        Equipment = data.Inventory.Equipment.Select(slot => new PlayerInventorySlotEdit(slot)).ToList();
        Hotbar = data.Inventory.Hotbar.Select(slot => new PlayerInventorySlotEdit(slot)).ToList();
        Backpack = data.Inventory.Main.Select(slot => new PlayerInventorySlotEdit(slot)).ToList();
        Transmog = data.TransmogSlots.Select(slot => new PlayerInventorySlotEdit(slot)).ToList();
        TransmogVisibility = data.TransmogVisibility.Select((visible, index) => new TransmogVisibilityEdit(index, visible)).ToList();
        Respawn = new PlayerRespawnEdit(data.RespawnX, data.RespawnY, data.RespawnZ, data.RespawnLevelGuid, data.TerminalRespawnId);
        _originalRespawn = Respawn.Clone();
        CarriedPets = data.CarriedPets.Select(pet => new CarriedPetEdit(pet)).ToList();
        Codex = PlayerCodexEdit.Create(data, codexVocabulary ?? CodexVocabulary.Empty, codexLocalize);
        _originalEmails = Codex.CurrentEmails();
        _originalJournals = Codex.CurrentJournals();
        _originalCompendium = Codex.CurrentCompendium();
        _originalFish = Codex.CurrentFish();
        _originalKills = Codex.CurrentKills();
        SteamIdentifier = PlayerIdentifier.TryParseFromPlayerFileName(path, out var id) ? id : null;
        ItemUpgrades = itemUpgrades ?? ItemUpgradeCatalog.Empty;
    }

    public PlayerVitals Vitals { get; private set; }
    public IReadOnlyList<PlayerSkillEdit> Skills { get; }
    private readonly List<PlayerRecipeEdit> _recipes;
    /// <summary>Recipes known from the installed game's data, plus any legacy rows in this save.</summary>
    public IReadOnlyList<PlayerRecipeEdit> Recipes => _recipes;

    /// <summary>
    /// Ensures a staged row exists for every given recipe id. Rows added here start locked,
    /// so calling this never changes what the save would write; it lets a recipe browser that
    /// loaded the game's vocabulary later than this session still stage unlock edits.
    /// </summary>
    public void EnsureRecipeRows(IEnumerable<string> ids)
    {
        var known = _recipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && known.Add(id)) _recipes.Add(new PlayerRecipeEdit(id, false));
        }
    }
    public List<string> Traits { get; }
    public string? Background { get; set; }
    public IReadOnlyList<string> ItemVocabulary { get; }
    public IReadOnlyList<string> MapVocabulary { get; }
    public bool HasRecipeVocabulary { get; }
    public bool HasItemVocabulary => ItemVocabulary.Count > 0;
    public bool HasMapVocabulary => MapVocabulary.Count > 0;
    public HashSet<string> ItemsPickedUp { get; }
    public HashSet<string> CraftedItems { get; }
    public HashSet<string> MapsUnlocked { get; }
    public IReadOnlyList<PlayerInventorySlotEdit> Equipment { get; }
    public IReadOnlyList<PlayerInventorySlotEdit> Hotbar { get; }
    public IReadOnlyList<PlayerInventorySlotEdit> Backpack { get; }
    public IReadOnlyList<PlayerInventorySlotEdit> Transmog { get; }
    public IReadOnlyList<TransmogVisibilityEdit> TransmogVisibility { get; }
    public PlayerRespawnEdit Respawn { get; }
    public IReadOnlyList<RespawnTerminal> RespawnTerminals => RespawnTerminalCatalog.All;
    public List<CarriedPetEdit> CarriedPets { get; }
    public PlayerCodexEdit Codex { get; }

    /// <summary>
    /// Applies a codex vocabulary that finished loading after this session was created (the
    /// GATEPal tab loads game data on demand; selecting a save never scans game paks).
    /// Staged codex ticks survive the rebuild. The kill-count baseline is refreshed because
    /// tallies only become visible once the vocabulary names their compendium rows, and
    /// merely seeing the save's own numbers must not count as a staged edit.
    /// </summary>
    public bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        if (!Codex.ApplyVocabulary(_data, vocabulary, localize ?? _codexLocalize)) return false;
        _originalKills = Codex.CurrentKills();
        return true;
    }

    public IReadOnlyList<RawSaveProperty> RawProperties => RawSavePropertyEditor.List(_data.Raw)
        .Select(property => _rawEdits.TryGetValue(property.Name, out var staged)
            ? property with { Value = staged } : property).ToArray();
    /// <summary>Steam account id inferred from the player filename, when applicable.</summary>
    public string? SteamIdentifier { get; }
    public string Path => _path;
    public string JsonPath => _path + ".json";
    public bool JsonFileExists => File.Exists(JsonPath);
    public ItemUpgradeCatalog ItemUpgrades { get; }
    public int UnlockedRecipeCount => Recipes.Count(recipe => recipe.IsUnlocked);
    public int RecipeCount => Recipes.Count;
    public bool IsDirty => !SameVitals(Vitals, _original) || Skills.Any(skill => skill.IsDirty)
        || !CurrentRecipeSet().SetEquals(_originalRecipes)
        || !Traits.SequenceEqual(_originalTraits, StringComparer.Ordinal)
        || !string.Equals(Background, _originalPhd, StringComparison.Ordinal)
        || !ItemsPickedUp.SetEquals(_originalItemsPickedUp)
        || !CraftedItems.SetEquals(_originalCraftedItems)
        || !MapsUnlocked.SetEquals(_originalMapsUnlocked)
        || AllInventorySlots().Any(slot => slot.IsDirty) || Transmog.Any(slot => slot.IsDirty)
        || TransmogVisibility.Any(toggle => toggle.IsDirty) || Respawn.IsDifferentFrom(_originalRespawn)
        || CarriedPets.Any(pet => pet.IsDirty)
        || !Codex.CurrentEmails().SetEquals(_originalEmails)
        || !Codex.CurrentJournals().SetEquals(_originalJournals)
        || !Codex.CurrentCompendium().SetEquals(_originalCompendium)
        || !Codex.CurrentFish().SetEquals(_originalFish)
        || !Codex.CurrentKills().OrderBy(x => x.Key).SequenceEqual(_originalKills.OrderBy(x => x.Key))
        || _rawEdits.Count > 0;
    public string? Status { get; private set; }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlayerSaveWriter.ApplyStats(_data, new CharacterStats(
            Vitals.Hunger, Vitals.Thirst, Vitals.Sanity, Vitals.Fatigue, Vitals.Continence,
            (int)Math.Round(Vitals.Money)));
        PlayerSaveWriter.ApplyLimbHealth(_data, new LimbHealth(
            Vitals.Head, Vitals.Torso, Vitals.LeftArm, Vitals.RightArm, Vitals.LeftLeg, Vitals.RightLeg));
        PlayerSaveWriter.ApplySkills(_data, Skills.Select(skill => skill.ToPlayerSkill()).ToList());
        PlayerSaveWriter.ApplyRecipes(_data, CurrentRecipeSet()
            .OrderBy(recipe => recipe, StringComparer.Ordinal)
            .ToList());
        PlayerSaveWriter.ApplyTraits(_data, Traits);
        if (!string.IsNullOrWhiteSpace(Background)) PlayerSaveWriter.ApplyPhd(_data, Background);
        PlayerSaveWriter.ApplyItemsPickedUp(_data, ItemsPickedUp.OrderBy(id => id, StringComparer.Ordinal).ToList());
        PlayerSaveWriter.ApplyCraftedItems(_data, CraftedItems.OrderBy(id => id, StringComparer.Ordinal).ToList());
        PlayerSaveWriter.ApplyMapsUnlocked(_data, MapsUnlocked.OrderBy(id => id, StringComparer.Ordinal).ToList());
        PlayerSaveWriter.ApplyInventory(_data, new PlayerInventory(
            Equipment.Select(slot => slot.ToInventorySlot()).ToList(),
            Hotbar.Select(slot => slot.ToInventorySlot()).ToList(),
            Backpack.Select(slot => slot.ToInventorySlot()).ToList()));
        PlayerSaveWriter.ApplyTransmogSlots(_data, Transmog.Select(slot => slot.ToInventorySlot()).ToList());
        PlayerSaveWriter.ApplyTransmogVisibility(_data, TransmogVisibility.Select(toggle => toggle.IsVisible).ToList());
        if (Respawn.IsDifferentFrom(_originalRespawn))
        {
            PlayerSaveWriter.ApplyRespawn(_data, Respawn.X, Respawn.Y, Respawn.Z, Respawn.LevelGuid);
            if (!string.IsNullOrWhiteSpace(Respawn.TerminalGuid)) PlayerSaveWriter.ApplyRespawnTerminal(_data, Respawn.TerminalGuid);
        }
        foreach (var pet in CarriedPets.Where(pet => pet.IsDeleted && !pet.IsNew))
            PlayerSaveWriter.RemoveCarriedPet(_data, pet.Slot, pet.Index);
        foreach (var pet in CarriedPets.Where(pet => pet.IsNew && !pet.IsDeleted))
            if (PlayerSaveWriter.AddCarriedPetToSlot(_data, pet.Slot, pet.Index, pet.ToCarriedPet()) < 0)
                throw new InvalidOperationException($"The staged destination for '{pet.DisplayName}' is no longer empty.");
        foreach (var pet in CarriedPets.Where(pet => !pet.IsNew && !pet.IsDeleted && pet.IsDirty))
            PlayerSaveWriter.ApplyCarriedPet(_data, pet.ToCarriedPet());
        PlayerSaveWriter.ApplyEmailsRead(_data, Codex.CurrentEmails().OrderBy(id => id, StringComparer.Ordinal).ToList());
        PlayerSaveWriter.ApplyJournals(_data, Codex.CurrentJournals().OrderBy(id => id, StringComparer.Ordinal).ToList());
        var compendium = Codex.CompendiumArrays();
        PlayerSaveWriter.ApplyCompendium(_data, compendium.Email, compendium.Narrative, compendium.Exploration);
        PlayerSaveWriter.ApplyFishCaught(_data, Codex.CurrentFish().OrderBy(id => id, StringComparer.Ordinal).ToList());
        PlayerSaveWriter.ApplyKillCounts(_data, Codex.CurrentKills().Select(k => new KillCount(k.Key, k.Value)).ToList());
        ApplyRawEdits(_data);
        await AbioticEditor.Web.Services.SaveFilePersistence
            .WriteAsync(_files, _path, _data.Raw, cancellationToken).ConfigureAwait(false);
        _original = Vitals.Clone();
        foreach (var skill in Skills) skill.AcceptCurrentAsBaseline();
        _originalRecipes = CurrentRecipeSet();
        _originalTraits = Traits.ToList();
        _originalPhd = Background;
        _originalItemsPickedUp = new(ItemsPickedUp, StringComparer.Ordinal);
        _originalCraftedItems = new(CraftedItems, StringComparer.Ordinal);
        _originalMapsUnlocked = new(MapsUnlocked, StringComparer.Ordinal);
        foreach (var slot in AllInventorySlots()) slot.AcceptCurrentAsBaseline();
        foreach (var slot in Transmog) slot.AcceptCurrentAsBaseline();
        foreach (var toggle in TransmogVisibility) toggle.AcceptCurrentAsBaseline();
        _originalRespawn = Respawn.Clone();
        // Removed pets are gone from the file now, so drop them from the list too instead
        // of letting AcceptCurrentAsBaseline resurrect them as never-deleted rows.
        CarriedPets.RemoveAll(pet => pet.IsDeleted);
        foreach (var pet in CarriedPets) pet.AcceptCurrentAsBaseline();
        _originalEmails = Codex.CurrentEmails();
        _originalJournals = Codex.CurrentJournals();
        _originalCompendium = Codex.CurrentCompendium();
        _originalFish = Codex.CurrentFish();
        _originalKills = Codex.CurrentKills();
        _rawEdits.Clear();
        Status = "Saved (a .bak backup was created).";
    }

    public void Revert()
    {
        Vitals = _original.Clone();
        foreach (var skill in Skills) skill.Revert();
        foreach (var recipe in Recipes) recipe.IsUnlocked = _originalRecipes.Contains(recipe.Id);
        Traits.Clear(); Traits.AddRange(_originalTraits);
        Background = _originalPhd;
        ResetSet(ItemsPickedUp, _originalItemsPickedUp);
        ResetSet(CraftedItems, _originalCraftedItems);
        ResetSet(MapsUnlocked, _originalMapsUnlocked);
        foreach (var slot in AllInventorySlots()) slot.Revert();
        foreach (var slot in Transmog) slot.Revert();
        foreach (var toggle in TransmogVisibility) toggle.Revert();
        Respawn.CopyFrom(_originalRespawn);
        CarriedPets.RemoveAll(pet => pet.IsNew);
        foreach (var pet in CarriedPets) pet.Revert();
        Codex.SetFrom(_originalEmails, _originalJournals, _originalCompendium, _originalFish, _originalKills);
        _rawEdits.Clear();
        Status = "Changes reverted.";
    }

    public void MarkChanged() => Status = IsDirty ? "Unsaved changes" : null;

    public void MaxAllSkills()
    {
        foreach (var skill in Skills) skill.Level = SkillCatalog.MaxLevel;
        MarkChanged();
    }

    public void UnlockAllRecipes()
    {
        foreach (var recipe in Recipes) recipe.IsUnlocked = true;
        MarkChanged();
    }

    public void DiscoverAllItems() { ItemsPickedUp.UnionWith(ItemVocabulary); MarkChanged(); }
    public void DiscoverAllCraftedItems() { CraftedItems.UnionWith(ItemVocabulary); MarkChanged(); }
    public void UnlockAllMaps() { MapsUnlocked.UnionWith(MapVocabulary); MarkChanged(); }

    // The pak-backed vocabularies are loaded on demand by the tabs (save selection deliberately
    // never scans game paks), so the bulk actions also accept a vocabulary supplied after this
    // session was built. Unknown recipe ids become new staged rows, exactly as if the session
    // had known them at load time.
    public void DiscoverAllItems(IEnumerable<string> vocabulary) { ItemsPickedUp.UnionWith(CleanIds(vocabulary)); MarkChanged(); }
    public void DiscoverAllCraftedItems(IEnumerable<string> vocabulary) { CraftedItems.UnionWith(CleanIds(vocabulary)); MarkChanged(); }
    public void UnlockAllMaps(IEnumerable<string> vocabulary) { MapsUnlocked.UnionWith(CleanIds(vocabulary)); MarkChanged(); }

    /// <summary>Unlocks every known recipe, adding rows for catalog ids this session had not seen.</summary>
    public void UnlockAllRecipes(IEnumerable<string> vocabulary)
    {
        var known = _recipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in CleanIds(vocabulary))
            if (known.Add(id)) _recipes.Add(new PlayerRecipeEdit(id, false));
        _recipes.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id));
        UnlockAllRecipes();
    }

    private static IEnumerable<string> CleanIds(IEnumerable<string> ids)
        => ids.Where(id => !string.IsNullOrWhiteSpace(id));

    /// <summary>Returns a diagnostic JSON projection of the currently loaded raw save.</summary>
    public string ExportRawJson() => SaveJsonBridge.ToJson(_data.Raw);

    /// <summary>Exports the complete save JSON beside the save for editing with an external tool.</summary>
    public Task ExportJsonToFileAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => SaveJsonBridge.ExportJsonToFile(_data.Raw, JsonPath), cancellationToken);

    /// <summary>
    /// The same JSON as bytes, for hosts that have nowhere to write a file beside the save.
    /// A browser has no folder to put it in, so it hands these bytes to the player as a download.
    /// </summary>
    public Task<byte[]> ExportJsonBytesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => System.Text.Encoding.UTF8.GetBytes(ExportRawJson()), cancellationToken);

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
    /// file system, so this works in a browser too - it used to write straight to a disk path,
    /// which a browser tab does not have.
    /// </remarks>
    public async Task ImportJsonFromFileAsync(string jsonPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        var bytes = await Task.Run(() => SaveJsonBridge.ReadJsonAsSaveBytes(jsonPath), cancellationToken).ConfigureAwait(false);
        await AbioticEditor.Web.Services.SaveFilePersistence
            .WriteBytesAsync(_files, _path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stages one exact top-level primitive property edit after validating it on a clone.</summary>
    public bool TryStageRawEdit(string name, string? value, out string? error)
    {
        var candidate = CloneData(_data);
        if (!RawSavePropertyEditor.TryApply(candidate.Raw, name, value, out error)) return false;
        _rawEdits[name] = value ?? string.Empty;
        MarkChanged();
        return true;
    }

    public void DiscardRawEdits() { _rawEdits.Clear(); MarkChanged(); }

    /// <summary>Stages a world pet into a free companion slot, falling back to the hotbar.</summary>
    public bool TryAddWorldPet(WorldPet pet, PetSlotKind preferred, out string message)
    {
        var itemRow = PetItemCatalog.ItemRowFor(pet.NpcClass);
        if (itemRow is null) { message = $"No carried-item form is known for '{pet.ShortClass}'."; return false; }
        var target = FindFreePetSlot(preferred) ?? (preferred == PetSlotKind.Equipment ? FindFreePetSlot(PetSlotKind.Hotbar) : FindFreePetSlot(PetSlotKind.Equipment));
        if (target is null) { message = "The companion slot and hotbar are full. Free a slot and try again."; return false; }
        var health = pet.LimbHealth.Values.Sum();
        if (health <= 0) health = PetItemCatalog.DefaultMaxHealth;
        CarriedPets.Add(new CarriedPetEdit(new CarriedPet(target.Value.Kind, target.Value.Index, itemRow, pet.CustomName, health, health, pet.Xp, 3, 1), isNew: true));
        MarkChanged();
        message = $"Staged {pet.DisplayName} for the {target.Value.Label}. Save this player after saving the world.";
        return true;
    }

    private (PetSlotKind Kind, int Index, string Label)? FindFreePetSlot(PetSlotKind kind)
    {
        var slots = kind switch { PetSlotKind.Equipment => Equipment, PetSlotKind.Hotbar => Hotbar, _ => Backpack };
        var candidate = kind == PetSlotKind.Equipment
            ? slots.FirstOrDefault(slot => slot.Index == 12 && slot.IsEmpty)
            : slots.FirstOrDefault(slot => slot.IsEmpty);
        return candidate is null ? null : (kind, candidate.Index, kind == PetSlotKind.Equipment ? "companion slot" : $"{kind} slot {candidate.Index}");
    }

    /// <summary>Swaps two existing slots without changing either array's shape.</summary>
    public bool TrySwapInventorySlots(PlayerInventoryArea area, int firstIndex, int secondIndex)
        => TrySwapInventorySlots(area, firstIndex, area, secondIndex);

    /// <summary>
    /// Swaps two player slots, including slots in different inventory areas. Both
    /// destinations are resolved before either projection is changed so an invalid
    /// request cannot leave a partially staged transfer.
    /// </summary>
    public bool TrySwapInventorySlots(
        PlayerInventoryArea firstArea, int firstIndex,
        PlayerInventoryArea secondArea, int secondIndex)
    {
        var first = FindSlot(firstArea, firstIndex);
        var second = FindSlot(secondArea, secondIndex);
        if (first is null || second is null || ReferenceEquals(first, second)) return false;

        var firstValue = first.ToInventorySlot();
        var secondValue = second.ToInventorySlot();
        first.LoadFrom(secondValue with { Index = first.Index });
        second.LoadFrom(firstValue with { Index = second.Index });
        MarkChanged();
        return true;
    }

    /// <summary>Sorts occupied slots by item row and leaves empty slots at the end.</summary>
    public void SortInventorySlots(PlayerInventoryArea area)
    {
        var slots = SlotsFor(area);
        var ordered = slots.Select(slot => slot.ToInventorySlot())
            .OrderBy(slot => slot.IsEmpty)
            .ThenBy(slot => slot.ItemId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(slot => slot.PlayerMadeString, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < slots.Count; index++) slots[index].LoadFrom(ordered[index] with { Index = slots[index].Index });
        MarkChanged();
    }

    public bool TryGetInventorySlot(PlayerInventoryArea area, int index, out InventoryItemSlot slot)
    {
        var edit = FindSlot(area, index);
        if (edit is not null) { slot = edit.ToInventorySlot(); return true; }
        slot = default!;
        return false;
    }

    public bool TrySetInventorySlot(PlayerInventoryArea area, int index, InventoryItemSlot slot)
    {
        var edit = FindSlot(area, index);
        if (edit is null) return false;
        edit.LoadFrom(slot with { Index = edit.Index });
        MarkChanged();
        return true;
    }

    public bool TryApplyItemUpgrade(PlayerInventoryArea area, int index, bool downgrade)
    {
        if (!TryGetInventorySlot(area, index, out var slot) || slot.IsEmpty) return false;
        var edge = downgrade ? ItemUpgrades.SourceOf(slot.ItemId) : ItemUpgrades.UpgradeFor(slot.ItemId);
        if (edge is null) return false;
        return TrySetInventorySlot(area, index, slot with { ItemId = downgrade ? edge.SourceId : edge.OutputId, AssetId = null });
    }

    private void ApplyRawEdits(PlayerSaveData data)
    {
        foreach (var edit in _rawEdits)
            if (!RawSavePropertyEditor.TryApply(data.Raw, edit.Key, edit.Value, out var error))
                throw new InvalidOperationException($"Raw edit '{edit.Key}' is no longer valid: {error}");
    }

    private static PlayerSaveData CloneData(PlayerSaveData source)
    {
        using var stream = new MemoryStream();
        source.Raw.WriteTo(stream);
        stream.Position = 0;
        return PlayerSaveReader.ReadFromStream(stream);
    }

    private HashSet<string> CurrentRecipeSet() => Recipes
        .Where(recipe => recipe.IsUnlocked)
        .Select(recipe => recipe.Id)
        .ToHashSet(StringComparer.Ordinal);

    private IEnumerable<PlayerInventorySlotEdit> AllInventorySlots()
        => Equipment.Concat(Hotbar).Concat(Backpack);

    private IReadOnlyList<PlayerInventorySlotEdit> SlotsFor(PlayerInventoryArea area) => area switch
    {
        PlayerInventoryArea.Equipment => Equipment,
        PlayerInventoryArea.Hotbar => Hotbar,
        PlayerInventoryArea.Transmog => Transmog,
        _ => Backpack,
    };

    private PlayerInventorySlotEdit? FindSlot(PlayerInventoryArea area, int index)
        => SlotsFor(area).FirstOrDefault(slot => slot.Index == index);

    private static List<PlayerRecipeEdit> BuildRecipes(
        IEnumerable<string> unlocked, IEnumerable<string>? vocabulary)
    {
        var unlockedSet = unlocked.ToHashSet(StringComparer.Ordinal);
        var ids = (vocabulary ?? Array.Empty<string>())
            .Concat(unlockedSet)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        return ids.Select(id => new PlayerRecipeEdit(id, unlockedSet.Contains(id))).ToList();
    }

    private static List<string> BuildVocabulary(IEnumerable<string> saved, IEnumerable<string>? vocabulary)
        => (vocabulary ?? Array.Empty<string>()).Concat(saved).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

    private static void ResetSet(HashSet<string> target, HashSet<string> source)
    {
        target.Clear(); target.UnionWith(source);
    }

    private static PlayerVitals ToVitals(PlayerSaveData data) => new()
    {
        Hunger = data.Stats.Hunger, Thirst = data.Stats.Thirst, Sanity = data.Stats.Sanity,
        Fatigue = data.Stats.Fatigue, Continence = data.Stats.Continence, Money = data.Stats.Money,
        Head = data.Health.Head, Torso = data.Health.Torso, LeftArm = data.Health.LeftArm,
        RightArm = data.Health.RightArm, LeftLeg = data.Health.LeftLeg, RightLeg = data.Health.RightLeg,
    };

    private static bool SameVitals(PlayerVitals left, PlayerVitals right) =>
        left.Hunger == right.Hunger && left.Thirst == right.Thirst && left.Sanity == right.Sanity
        && left.Fatigue == right.Fatigue && left.Continence == right.Continence && left.Money == right.Money
        && left.Head == right.Head && left.Torso == right.Torso && left.LeftArm == right.LeftArm
        && left.RightArm == right.RightArm && left.LeftLeg == right.LeftLeg && left.RightLeg == right.RightLeg;

    private static SkillDefinition SkillDefinitionFor(int index)
        => SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, index + 1)[index];
}

public enum PlayerInventoryArea { Equipment, Hotbar, Backpack, Transmog }

public sealed class TransmogVisibilityEdit
{
    private bool _original;
    public TransmogVisibilityEdit(int index, bool isVisible) { Index = index; _original = isVisible; IsVisible = isVisible; }
    public int Index { get; }
    public string Label => Index switch { 0 => "Head", 1 => "Chest", 2 => "Legs", 3 => "Feet", 4 => "Back", 5 => "Arms", _ => $"Slot {Index + 1}" };
    public bool IsVisible { get; set; }
    public bool IsDirty => IsVisible != _original;
    public void AcceptCurrentAsBaseline() => _original = IsVisible;
    public void Revert() => IsVisible = _original;
}

public sealed class PlayerRespawnEdit
{
    public PlayerRespawnEdit(double x, double y, double z, string? levelGuid, string? terminalGuid)
        => (X, Y, Z, LevelGuid, TerminalGuid) = (x, y, z, levelGuid, terminalGuid);
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? LevelGuid { get; set; }
    public string? TerminalGuid { get; set; }
    public PlayerRespawnEdit Clone() => new(X, Y, Z, LevelGuid, TerminalGuid);
    public void CopyFrom(PlayerRespawnEdit source) => (X, Y, Z, LevelGuid, TerminalGuid) = (source.X, source.Y, source.Z, source.LevelGuid, source.TerminalGuid);
    public bool IsDifferentFrom(PlayerRespawnEdit other) => Math.Abs(X - other.X) > 0.001 || Math.Abs(Y - other.Y) > 0.001 || Math.Abs(Z - other.Z) > 0.001 || !string.Equals(LevelGuid, other.LevelGuid, StringComparison.OrdinalIgnoreCase) || !string.Equals(TerminalGuid, other.TerminalGuid, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Staged, editable projection of one existing inventory slot.</summary>
public sealed class PlayerInventorySlotEdit
{
    private InventoryItemSlot _original;
    public PlayerInventorySlotEdit(InventoryItemSlot source) { _original = source; Load(source); }
    public int Index => _original.Index;
    public string? ItemId { get; set; }
    public int Count { get; set; }
    public double Durability { get; set; }
    public double MaxDurability { get; set; }
    public int AmmoInMagazine { get; set; }
    public int LiquidLevel { get; set; }
    public string? LiquidType { get; set; }
    public bool DynamicState { get; set; }
    public string? PlayerMadeString { get; set; }
    public string? AssetId { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(ItemId) || ItemId is "None" or "Empty";
    public string DisplayName => IsEmpty ? "Empty" : ItemId!;
    public bool IsDirty => !Equals(ToInventorySlot(), _original);
    public void Clear() { ItemId = PlayerSaveWriter.EmptySlotRowName; Count = 0; }
    // Do not normalize loaded values here. Empty slots legitimately use sentinel values such
    // as LiquidLevel = -1; normalizing them would make a newly opened session dirty and cause
    // unrelated player edits to rewrite every such slot.
    public InventoryItemSlot ToInventorySlot() => new(Index, string.IsNullOrWhiteSpace(ItemId) ? PlayerSaveWriter.EmptySlotRowName : ItemId,
        Count, Durability, MaxDurability, AmmoInMagazine, LiquidLevel, LiquidType, DynamicState, PlayerMadeString, AssetId);
    public void AcceptCurrentAsBaseline() => _original = ToInventorySlot();
    public void Revert() => Load(_original);
    public void LoadFrom(InventoryItemSlot source) { ItemId = source.ItemId; Count = source.Count; Durability = source.Durability; MaxDurability = source.MaxDurability; AmmoInMagazine = source.AmmoInMagazine; LiquidLevel = source.LiquidLevel; LiquidType = source.LiquidType; DynamicState = source.DynamicState; PlayerMadeString = source.PlayerMadeString; AssetId = source.AssetId; }
    private void Load(InventoryItemSlot source) => LoadFrom(source);
}

/// <summary>Staged editable projection of a carried pet item.</summary>
public sealed class CarriedPetEdit
{
    private CarriedPet _original;
    public CarriedPetEdit(CarriedPet source, bool isNew = false) { _original = source; IsNew = isNew; Load(source); }
    public PetSlotKind Slot => _original.Slot;
    public int Index => _original.Index;
    public string ItemRow { get; set; } = string.Empty;
    public string? Name { get; set; }
    public double Health { get; set; }
    public double MaxHealth { get; set; }
    public int Xp { get; set; }
    public int MutationProgress { get; set; }
    public int PetMutation { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsNew { get; private set; }
    /// <summary>Level and XP are two views of one value, like the native pet editor's stepper.</summary>
    public int Level
    {
        get => PetCatalog.LevelForXp(Math.Max(0, Xp));
        set { var level = Math.Clamp(value, 0, PetCatalog.MaxLevel); if (level != Level) Xp = PetCatalog.XpForLevel(level); }
    }
    public bool IsCompanionSlot => _original.IsCompanionSlot;
    public string Variant => PetItemCatalog.FriendlyName(ItemRow) ?? ItemRow;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (PetItemCatalog.FriendlyName(ItemRow) ?? ItemRow) : Name;
    public string SlotLabel => _original.IsCompanionSlot ? "Companion slot" : $"{Slot} slot {Index}";
    public bool IsDirty => IsNew || IsDeleted || !Equals(ToCarriedPet(), _original);
    public CarriedPet ToCarriedPet() => new(Slot, Index, ItemRow, string.IsNullOrWhiteSpace(Name) ? null : Name, Math.Max(0, Health), Math.Max(0, MaxHealth), Math.Max(0, Xp), Math.Max(0, MutationProgress), PetMutation);
    public void Heal() => Health = MaxHealth > 0 ? MaxHealth : PetItemCatalog.DefaultMaxHealth;
    public void AcceptCurrentAsBaseline() { _original = ToCarriedPet(); IsDeleted = false; IsNew = false; }
    public void Revert() { IsDeleted = false; Load(_original); }
    private void Load(CarriedPet source) { ItemRow = source.ItemRow; Name = source.Name; Health = source.Health; MaxHealth = source.MaxHealth; Xp = source.Xp; MutationProgress = source.MutationProgress; PetMutation = source.PetMutation; }
}

/// <summary>A mutable unlocked flag for one recipe row. Unknown legacy rows are retained by id.</summary>
public sealed class PlayerRecipeEdit
{
    public PlayerRecipeEdit(string id, bool isUnlocked)
    {
        Id = id;
        IsUnlocked = isUnlocked;
    }

    public string Id { get; }
    public bool IsUnlocked { get; set; }
}

/// <summary>A mutable Razor editing projection of a positional player skill.</summary>
public sealed class PlayerSkillEdit
{
    private PlayerSkill _original;
    private float _xp;
    private float _multiplier;

    public PlayerSkillEdit(PlayerSkill skill, SkillDefinition definition)
    {
        _original = skill;
        Definition = definition;
        _xp = skill.Xp;
        _multiplier = skill.XpMultiplier;
    }

    public SkillDefinition Definition { get; }
    public string Name => Definition.DisplayName;
    public string? Description => Definition.Description;
    public float Xp { get => _xp; set => _xp = Math.Max(0, value); }
    public int Level { get => SkillCatalog.LevelForXp(Xp); set => Xp = SkillCatalog.XpForLevel(Math.Clamp(value, 0, SkillCatalog.MaxLevel)); }
    /// <summary>Per-skill XP gain rate (1.0 = normal), round-tripped from the save.</summary>
    public float Multiplier { get => _multiplier; set => _multiplier = Math.Max(0, value); }
    /// <summary>XP gain rate as the player-facing percentage the native XP RATE field edits.</summary>
    public float MultiplierPercent { get => _multiplier * 100f; set => Multiplier = Math.Max(0, value) / 100f; }
    public bool IsMaxed => Level >= SkillCatalog.MaxLevel;
    /// <summary>The XP slider ceiling: the max-level threshold, or the save's own larger XP.</summary>
    public double MaxXp => Math.Max(SkillCatalog.XpForLevel(SkillCatalog.MaxLevel), _xp);
    public bool IsDirty => Math.Abs(Xp - _original.Xp) > 0.001f || Math.Abs(_multiplier - _original.XpMultiplier) > 0.001f;
    public PlayerSkill ToPlayerSkill() => _original with { Xp = Xp, XpMultiplier = _multiplier };
    public void AcceptCurrentAsBaseline() => _original = ToPlayerSkill();
    public void Revert() { Xp = _original.Xp; _multiplier = _original.XpMultiplier; }

    /// <summary>Whether this skill's own level clears the given milestone's requirement.</summary>
    public bool IsUnlocked(SkillMilestone milestone) => Level >= milestone.Level;
}
