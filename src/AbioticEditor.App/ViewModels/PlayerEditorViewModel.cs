using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Editing surface for one loaded player save. Holds mutable copies of the stat values
/// bound to sliders/entries; tracks dirty state and exposes a Save command that writes
/// changes back to disk via <see cref="PlayerSaveWriter"/>.
/// </summary>
public sealed class PlayerEditorViewModel : INotifyPropertyChanged
{
    private readonly PlayerSaveData _data;
    private readonly string _path;

    private CharacterStats _baseline;
    private double _hunger, _thirst, _sanity, _fatigue, _continence;
    private int _money;
    private bool _isSaving;
    private string? _saveStatus;
    private InventorySlotViewModel? _selectedSlot;

    public PlayerEditorViewModel(PlayerSaveData data, string path)
    {
        _data = data;
        _path = path;
        _baseline = data.Stats;

        // Version sanity: warn when the save is newer than this editor knows, or its
        // save class wasn't recognized at all.
        CompatibilityWarning = Core.Compatibility.SaveCompatibility.WarningFor(data.Raw);
        if (CompatibilityWarning is not null)
        {
            Core.Diagnostics.EditorLog.Warn(
                "Compatibility", $"{Path.GetFileName(path)}: {CompatibilityWarning}");
        }

        _hunger = _baseline.Hunger;
        _thirst = _baseline.Thirst;
        _sanity = _baseline.Sanity;
        _fatigue = _baseline.Fatigue;
        _continence = _baseline.Continence;
        _money = _baseline.Money;

        // Build slot view-models. Catalog + icons may not be loaded yet - they get
        // wired in on first read; if absent, slots fall back to raw item IDs.
        Equipment = BuildSlotList(InventoryKind.Equipment, data.Inventory.Equipment, EquipmentRoleMap);
        Hotbar = BuildSlotList(InventoryKind.Hotbar, data.Inventory.Hotbar);
        // Special backpack slots (cold/freezer/shielded/warm) are fixed indices defined
        // by the equipped backpack's data asset - surface them as slot roles.
        var backpackId = data.Inventory.Equipment.Count > 3 ? data.Inventory.Equipment[3].ItemId : null;
        Main = BuildSlotList(InventoryKind.Main, data.Inventory.Main,
            Services.GameDataServices.BackpackSlots.For(backpackId));
        Transmog = BuildSlotList(InventoryKind.Main, data.TransmogSlots, TransmogRoleMap);
        _carriedPets = data.CarriedPets.Select(p => new CarriedPetViewModel(p, Refresh)).ToList();
        TransmogToggles = data.TransmogVisibility
            .Select((v, i) => new TransmogToggleViewModel(i, v, Refresh))
            .ToList();
        // The save's array covers all 12 equip slots, but only the six visual gear
        // slots (chest/head/legs/backpack/arms/suit, indices 0-5) render on the
        // character - headlamp/trinket/watch/hacker toggles would be no-ops, so the
        // UI hides them; their stored flags round-trip untouched.
        VisibleTransmogToggles = TransmogToggles.Take(6).ToList();

        // Watch every slot for edits -> bubble dirty + command CanExecute refresh.
        foreach (var s in Equipment) s.PropertyChanged += OnSlotChanged;
        foreach (var s in Hotbar) s.PropertyChanged += OnSlotChanged;
        foreach (var s in Main) s.PropertyChanged += OnSlotChanged;
        foreach (var s in Transmog) s.PropertyChanged += OnSlotChanged;

        Skills = BuildSkillList(data.Skills);
        foreach (var s in Skills) s.PropertyChanged += OnSkillChanged;

        // Traits + background.
        _traitBaseline = data.Traits.ToList();
        Traits = new System.Collections.ObjectModel.ObservableCollection<TraitItemViewModel>(
            data.Traits.Select(t => new TraitItemViewModel(t)));
        _phd = _phdBaseline = data.Phd;

        // Limb health + recipes.
        _healthBaseline = data.Health;
        (_head, _torso, _leftArm, _rightArm, _leftLeg, _rightLeg) =
            (data.Health.Head, data.Health.Torso, data.Health.LeftArm, data.Health.RightArm, data.Health.LeftLeg, data.Health.RightLeg);
        RecipeBrowser = new RecipeListViewModel(data.Recipes, Refresh);
        Codex = new CodexViewModel(
            data.EmailsRead, data.Journals,
            data.CompendiumEmail, data.CompendiumNarrative, data.CompendiumExploration,
            data.KillCounts, data.FishCaught,
            Refresh);

        // Discovery lists (items seen / crafted / maps).
        _itemsPickedUp = data.ItemsPickedUp.ToList();
        _craftedItems = data.CraftedItems.ToList();
        _mapsUnlocked = data.MapsUnlocked.ToList();
        (_itemsPickedUpBaseline, _craftedItemsBaseline, _mapsBaseline) =
            (_itemsPickedUp.Count, _craftedItems.Count, _mapsUnlocked.Count);
        DiscoverAllItemsCommand = new RelayCommand(
            () => DiscoverAll(_itemsPickedUp, AllItemIds(), nameof(ItemsPickedUpText)),
            () => GameDataServices.Catalog is not null);
        DiscoverAllCraftedCommand = new RelayCommand(
            () => DiscoverAll(_craftedItems, AllItemIds(), nameof(CraftedItemsText)),
            () => GameDataServices.Catalog is not null);
        UnlockAllMapsCommand = new RelayCommand(
            () => DiscoverAll(_mapsUnlocked, GameDataServices.AllMaps, nameof(MapsText)),
            () => GameDataServices.AllMaps.Count > 0);

        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => IsDirty && !IsSaving);
        RevertCommand = new RelayCommand(Revert, () => IsDirty && !IsSaving);
        ClearSelectionCommand = new RelayCommand(() => SelectedSlot = null);
        ShowGeneralCommand = new RelayCommand(() => ActiveTab = PlayerTab.General);
        ShowVitalsCommand = new RelayCommand(() => ActiveTab = PlayerTab.Vitals);
        ShowCharacterCommand = new RelayCommand(() => ActiveTab = PlayerTab.Character);
        ShowTransmogCommand = new RelayCommand(() => ActiveTab = PlayerTab.Transmog);
        ShowSpawnCommand = new RelayCommand(() => ActiveTab = PlayerTab.Spawn);
        ShowInventoryCommand = new RelayCommand(() => ActiveTab = PlayerTab.Inventory);
        ShowPetsCommand = new RelayCommand(() => ActiveTab = PlayerTab.Pets);
        SendPetToWorldCommand = new RelayCommand(async () => await SendPetToWorldAsync());
        ShowSkillsCommand = new RelayCommand(() => ActiveTab = PlayerTab.Skills);
        ShowRecipesCommand = new RelayCommand(() => ActiveTab = PlayerTab.Recipes);
        ShowCodexCommand = new RelayCommand(() => ActiveTab = PlayerTab.Codex);
        ShowAchievementsCommand = new RelayCommand(() => { AchievementsVm.EnsureLoaded(); ActiveTab = PlayerTab.Achievements; });
        ShowRawCommand = new RelayCommand(() => ActiveTab = PlayerTab.Raw);
        ExportJsonCommand = new RelayCommand(async () => await ExportJsonAsync(), () => !IsSaving);
        ImportJsonCommand = new RelayCommand(async () => await ImportJsonAsync(), () => !IsSaving && JsonFileExists);
        MaxAllSkillsCommand = new RelayCommand(() =>
        {
            Core.Diagnostics.EditorLog.Info("Edit", $"Max all skills ({Skills.Count} skill(s) -> level {SkillCatalog.MaxLevel})");
            foreach (var s in Skills) s.Level = SkillCatalog.MaxLevel;
        });
        RemoveTraitCommand = new RelayCommand<TraitItemViewModel>(RemoveTrait);
        AddTraitCommand = new RelayCommand(AddSelectedTrait, () => SelectedNewTrait is not null);
        HealAllCommand = new RelayCommand(() =>
        {
            Core.Diagnostics.EditorLog.Info("Edit", "Heal all limbs -> 100");
            Head = Torso = LeftArm = RightArm = LeftLeg = RightLeg = 100;
        });

        InitializeRespawn();
    }

    // ---------- Limb health ----------

    private LimbHealth _healthBaseline;
    private double _head, _torso, _leftArm, _rightArm, _leftLeg, _rightLeg;

    public double Head { get => _head; set => Set(ref _head, value); }
    public double Torso { get => _torso; set => Set(ref _torso, value); }
    public double LeftArm { get => _leftArm; set => Set(ref _leftArm, value); }
    public double RightArm { get => _rightArm; set => Set(ref _rightArm, value); }
    public double LeftLeg { get => _leftLeg; set => Set(ref _leftLeg, value); }
    public double RightLeg { get => _rightLeg; set => Set(ref _rightLeg, value); }

    public ICommand HealAllCommand { get; }

    private LimbHealth CurrentHealth() => new(_head, _torso, _leftArm, _rightArm, _leftLeg, _rightLeg);

    private bool IsHealthDirty() =>
        Drifted(_head, _healthBaseline.Head) ||
        Drifted(_torso, _healthBaseline.Torso) ||
        Drifted(_leftArm, _healthBaseline.LeftArm) ||
        Drifted(_rightArm, _healthBaseline.RightArm) ||
        Drifted(_leftLeg, _healthBaseline.LeftLeg) ||
        Drifted(_rightLeg, _healthBaseline.RightLeg);

    /// <summary>
    /// Tolerant comparison for slider-bound stats. The platform Slider quantizes the
    /// value it writes back whenever a tab re-realizes it, so an untouched stat can
    /// "change" by a fraction; treating that as an edit produced false unsaved-changes
    /// prompts. Real edits move whole points.
    /// </summary>
    private static bool Drifted(double current, double baseline) => Math.Abs(current - baseline) > 0.5;

    // ---------- Recipes ----------

    /// <summary>The full recipe browser (search, per-recipe toggles, unlock-all).</summary>
    public RecipeListViewModel RecipeBrowser { get; }

    /// <summary>Emails / journal / compendium browser with read-state editing.</summary>
    public CodexViewModel Codex { get; }

    // ---------- Discovery lists ----------

    private readonly List<string> _itemsPickedUp;
    private readonly List<string> _craftedItems;
    private readonly List<string> _mapsUnlocked;
    private int _itemsPickedUpBaseline, _craftedItemsBaseline, _mapsBaseline;

    public ICommand DiscoverAllItemsCommand { get; }
    public ICommand DiscoverAllCraftedCommand { get; }
    public ICommand UnlockAllMapsCommand { get; }

    public string ItemsPickedUpText => $"{_itemsPickedUp.Count} item(s) seen";
    public string CraftedItemsText => $"{_craftedItems.Count} item(s) crafted";
    public string MapsText => GameDataServices.AllMaps.Count > 0
        ? $"{_mapsUnlocked.Count} of {GameDataServices.AllMaps.Count} maps"
        : $"{_mapsUnlocked.Count} maps";

    private static IReadOnlyList<string> AllItemIds()
        => GameDataServices.Catalog?.Entries.Select(e => e.Id).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    private void DiscoverAll(List<string> target, IReadOnlyList<string> all, string countProperty)
    {
        var before = target.Count;
        var have = new HashSet<string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var id in all)
        {
            if (have.Add(id)) target.Add(id);
        }
        var label = countProperty switch
        {
            nameof(ItemsPickedUpText) => "items discovered",
            nameof(CraftedItemsText) => "crafted items discovered",
            nameof(MapsText) => "maps unlocked",
            _ => countProperty,
        };
        Core.Diagnostics.EditorLog.Info("Edit", $"Discover all ({label}): {before} -> {target.Count}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(countProperty));
        Refresh();
    }

    private bool IsDiscoveryDirty()
        => _itemsPickedUp.Count != _itemsPickedUpBaseline
        || _craftedItems.Count != _craftedItemsBaseline
        || _mapsUnlocked.Count != _mapsBaseline;

    /// <summary>
    /// Adds every item currently held (across the equipment, hotbar and main inventory) to
    /// <paramref name="pickedUp"/>, skipping empties and ids the game wouldn't index. Holding an item
    /// implies the player has seen it, so this keeps the "items seen" field honest when items are
    /// added through the editor - the game otherwise shows an added item as undiscovered.
    /// </summary>
    private static void MarkHeldItemsDiscovered(
        List<string> pickedUp, params IReadOnlyList<Core.PlayerSaves.InventoryItemSlot>[] inventories)
    {
        var have = new HashSet<string>(pickedUp, StringComparer.OrdinalIgnoreCase);
        foreach (var inv in inventories)
        {
            foreach (var slot in inv)
            {
                if (slot.IsEmpty || slot.ItemId is not { } id) continue;
                if (have.Add(id)) pickedUp.Add(id);
            }
        }
    }

    // ---------- Steam achievements ----------

    private AchievementsViewModel? _achievementsVm;

    /// <summary>Achievements browser (local cache + Steam web, spoilers, compare).</summary>
    public AchievementsViewModel AchievementsVm => _achievementsVm ??= new AchievementsViewModel(SteamId64, BuildCompareCandidates());

    /// <summary>Opaque owner id from the <c>Player_&lt;id&gt;.sav</c> file name (SteamID64 on
    /// Steam, an account token on non-Steam); empty when the name has no id.</summary>
    public string OwnerId =>
        Core.PlayerSaves.PlayerIdentifier.TryParseFromPlayerFileName(_path, out var id) ? id : string.Empty;

    /// <summary>True when this save is owned by a Steam account; gates Steam-only features.</summary>
    public bool IsSteamSave => Core.PlayerSaves.PlayerIdentifier.IsSteamId(OwnerId);

    // ----- General tab account row (Steam vs Game Pass / non-Steam) -----

    /// <summary>Label for the account-id row: a SteamID64 on Steam, a generic account id otherwise.</summary>
    public string OwnerIdLabel => IsSteamSave ? "STEAM ID 64" : "ACCOUNT ID";

    /// <summary>The id shown in the account field (the real owner id, not the numeric 0 a non-Steam
    /// save would show through <see cref="SteamId64"/>).</summary>
    public string OwnerIdDisplay => OwnerId;

    /// <summary>Account-id editing (re-homing) is only offered for Steam saves; a Game Pass / Xbox id
    /// is locked here (re-home it via SAVE CONVERSION in Settings instead).</summary>
    public bool CanEditOwnerId => IsSteamSave;

    public bool OwnerIdLocked => !IsSteamSave;

    public string OwnerIdNote => IsSteamSave
        ? string.Empty
        : LocalizationResourceManager.Instance["PlayerEditor_OwnerIdNote"];

    /// <summary>SteamID64 parsed from the <c>Player_&lt;id&gt;.sav</c> file name, or 0 for a
    /// non-Steam save. Used by the Steam-only paths (achievements, customization defaults).</summary>
    public long SteamId64 =>
        Core.PlayerSaves.PlayerIdentifier.TryParseSteamId(OwnerId, out var id) ? (long)id : 0;

    /// <summary>Other accounts in the same PlayerData folder, for achievement comparison.</summary>
    private IReadOnlyList<AchievementsViewModel.CompareCandidate> BuildCompareCandidates()
    {
        var dir = Path.GetDirectoryName(_path);
        if (dir is null) return Array.Empty<AchievementsViewModel.CompareCandidate>();

        var result = new List<AchievementsViewModel.CompareCandidate>();
        foreach (var file in Directory.EnumerateFiles(dir, "Player_*.sav"))
        {
            if (string.Equals(file, _path, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            if (long.TryParse(name["Player_".Length..], out var id))
            {
                result.Add(new AchievementsViewModel.CompareCandidate(id, name["Player_".Length..]));
            }
        }
        return result;
    }

    // ---------- Skills ----------

    public IReadOnlyList<SkillViewModel> Skills { get; }
    public ICommand MaxAllSkillsCommand { get; }

    private static List<SkillViewModel> BuildSkillList(IReadOnlyList<Core.PlayerSaves.PlayerSkill> skills)
    {
        // Pad with "Unknown skill #N" placeholders when the save's positional Skills_
        // array outgrew the catalog (newer game version) - every entry must stay
        // visible and round-trip at its position.
        var defs = SkillCatalog.WithUnknownPlaceholders(GameDataServices.SkillDefinitions, skills.Count);
        var result = new List<SkillViewModel>(skills.Count);
        foreach (var skill in skills)
        {
            var def = skill.Index < defs.Count
                ? defs[skill.Index]
                : new SkillDefinition(skill.Index, $"UnknownSkill{skill.Index}", $"Unknown skill #{skill.Index}", null, null);

            result.Add(new SkillViewModel(skill, def, iconPath: null));
        }

        // Icon extraction is file IO (plus a first-time texture decode) - run it on one
        // background task and let the icons pop in, instead of blocking the editor load.
        if (GameDataServices.Provider is { } provider)
        {
            var pending = result.Where(vm => vm.Definition.IconAssetPath is not null).ToList();
            if (pending.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    foreach (var vm in pending)
                    {
                        try
                        {
                            var icon = provider.ExtractTextureByGameRef(vm.Definition.IconAssetPath!);
                            if (icon is not null)
                            {
                                MainThread.BeginInvokeOnMainThread(() => vm.IconPath = icon);
                            }
                        }
                        catch
                        {
                            // Icons are cosmetic.
                        }
                    }
                });
            }
        }
        return result;
    }

    private void OnSkillChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SkillViewModel.IsDirty))
        {
            Refresh();
        }
    }

    // ---------- Traits + background ----------

    private readonly List<string> _traitBaseline;
    private string? _phd;
    private string? _phdBaseline;
    private TraitOption? _selectedNewTrait;

    public System.Collections.ObjectModel.ObservableCollection<TraitItemViewModel> Traits { get; }

    public sealed record TraitOption(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>All known trait ids, for the add-trait picker.</summary>
    public IReadOnlyList<TraitOption> KnownTraits { get; } = TraitCatalog.Traits
        .Select(kv => new TraitOption(kv.Key, kv.Value))
        .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<TraitOption> KnownBackgrounds { get; } = TraitCatalog.Backgrounds
        .Select(kv => new TraitOption(kv.Key, kv.Value))
        .ToList();

    public TraitOption? SelectedNewTrait
    {
        get => _selectedNewTrait;
        set
        {
            if (_selectedNewTrait == value) return;
            _selectedNewTrait = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNewTrait)));
            (AddTraitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand AddTraitCommand { get; }
    public ICommand RemoveTraitCommand { get; }

    /// <summary>Background/job as a picker option (id round-trips through TraitOption).</summary>
    public TraitOption? SelectedBackground
    {
        get => _phd is null ? null : KnownBackgrounds.FirstOrDefault(b => b.Id == _phd)
            ?? new TraitOption(_phd, _phd);
        set
        {
            if (value?.Id == _phd) return;
            _phd = value?.Id;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBackground)));
            Refresh();
        }
    }

    private void AddSelectedTrait()
    {
        if (_selectedNewTrait is null) return;
        if (Traits.Any(t => t.Id == _selectedNewTrait.Id)) return;
        Traits.Add(new TraitItemViewModel(_selectedNewTrait.Id));
        Core.Diagnostics.EditorLog.Info("Edit", $"Trait ADDED: {_selectedNewTrait.Id}");
        Refresh();
    }

    // ---------- Trait browser (see what a trait does before adding it) ----------

    private string _traitSearch = string.Empty;
    private IReadOnlyList<TraitBrowserRow>? _allTraitRows;

    public string TraitSearchText
    {
        get => _traitSearch;
        set
        {
            if (_traitSearch == value) return;
            _traitSearch = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TraitSearchText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleTraitOptions)));
        }
    }

    public IReadOnlyList<TraitBrowserRow> VisibleTraitOptions
    {
        get
        {
            _allTraitRows ??= BuildTraitRows();
            IEnumerable<TraitBrowserRow> q = _allTraitRows;
            if (!string.IsNullOrWhiteSpace(_traitSearch))
            {
                var f = _traitSearch.Trim();
                q = q.Where(t => t.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
                              || (t.Description?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            var have = new HashSet<string>(Traits.Select(t => t.Id), StringComparer.Ordinal);
            return q.Where(t => !have.Contains(t.Id)).ToList();
        }
    }

    private static List<TraitBrowserRow> BuildTraitRows()
    {
        var details = GameDataServices.TraitDetails;
        return TraitCatalog.Traits.Keys
            .Select(id =>
            {
                details.TryGetValue(id, out var d);
                // Cut traits ship without any description text and are never offered
                // by the in-game creator - label them instead of showing a blank.
                var description = d?.Description
                    ?? (d is { AvailableOnStart: false }
                        ? LocalizationResourceManager.Instance["PlayerEditor_CutTraitDescription"]
                        : null);
                return new TraitBrowserRow(id, d?.DisplayName ?? TraitCatalog.DisplayNameFor(id), description, d?.PointCost ?? 0);
            })
            .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ICommand AddTraitRowCommand => _addTraitRowCommand ??= new RelayCommand<TraitBrowserRow>(row =>
    {
        if (row is null || Traits.Any(t => t.Id == row.Id)) return;
        Traits.Add(new TraitItemViewModel(row.Id));
        Core.Diagnostics.EditorLog.Info("Edit", $"Trait ADDED: {row.Id}");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleTraitOptions)));
        Refresh();
    });
    private RelayCommand<TraitBrowserRow>? _addTraitRowCommand;

    private void RemoveTrait(TraitItemViewModel? trait)
    {
        if (trait is null) return;
        if (Traits.Remove(trait))
        {
            Core.Diagnostics.EditorLog.Info("Edit", $"Trait REMOVED: {trait.Id}");
            Refresh();
        }
    }

    private bool AreTraitsDirty()
    {
        if (Traits.Count != _traitBaseline.Count) return true;
        for (var i = 0; i < Traits.Count; i++)
        {
            if (!string.Equals(Traits[i].Id, _traitBaseline[i], StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // ---------- Tabs ----------

    public enum PlayerTab { General, Vitals, Character, Transmog, Spawn, Inventory, Pets, Skills, Recipes, Codex, Achievements, Raw }

    private PlayerTab _activeTab = PlayerTab.Vitals;
    private string? _rawStatus;

    public PlayerTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab == value) return;
            _activeTab = value;
            foreach (var n in new[] { nameof(IsGeneralTab), nameof(IsVitalsTab), nameof(IsCharacterTab), nameof(IsTransmogTab), nameof(IsSpawnTab), nameof(IsInventoryTab), nameof(IsPetsTab), nameof(IsSkillsTab), nameof(IsRecipesTab), nameof(IsCodexTab), nameof(IsAchievementsTab), nameof(IsRawTab) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    public bool IsGeneralTab => _activeTab == PlayerTab.General;
    public bool IsVitalsTab => _activeTab == PlayerTab.Vitals;
    public bool IsCharacterTab => _activeTab == PlayerTab.Character;
    public bool IsTransmogTab => _activeTab == PlayerTab.Transmog;
    public bool IsSpawnTab => _activeTab == PlayerTab.Spawn;
    public bool IsInventoryTab => _activeTab == PlayerTab.Inventory;
    public bool IsPetsTab => _activeTab == PlayerTab.Pets;
    public bool IsSkillsTab => _activeTab == PlayerTab.Skills;
    public bool IsRecipesTab => _activeTab == PlayerTab.Recipes;
    public bool IsCodexTab => _activeTab == PlayerTab.Codex;
    public bool IsAchievementsTab => _activeTab == PlayerTab.Achievements;
    public bool IsRawTab => _activeTab == PlayerTab.Raw;

    /// <summary>Whether to offer the Achievements tab. It is a Steam-only feature (it reads Steam's
    /// local cache and queries the Steam Web API by SteamID64), so it is hidden for Game Pass / Xbox
    /// saves whose owner id is an XUID, not a SteamID64, and there is nothing for it to show.</summary>
    public bool ShowAchievementsTab => IsSteamSave;

    public string? RawStatus { get => _rawStatus; private set => Set(ref _rawStatus, value); }

    public ICommand ShowGeneralCommand { get; private set; } = null!;
    public ICommand ShowVitalsCommand { get; private set; } = null!;
    public ICommand ShowCharacterCommand { get; private set; } = null!;
    public ICommand ShowTransmogCommand { get; private set; } = null!;
    public ICommand ShowSpawnCommand { get; private set; } = null!;
    public ICommand ShowInventoryCommand { get; private set; } = null!;
    public ICommand ShowSkillsCommand { get; private set; } = null!;
    public ICommand ShowRecipesCommand { get; private set; } = null!;
    public ICommand ShowCodexCommand { get; private set; } = null!;
    public ICommand ShowPetsCommand { get; private set; } = null!;
    public ICommand ShowAchievementsCommand { get; private set; } = null!;
    public ICommand ShowRawCommand { get; private set; } = null!;
    public ICommand ExportJsonCommand { get; private set; } = null!;
    public ICommand ImportJsonCommand { get; private set; } = null!;

    public string FilePath => _path;

    /// <summary>Best-effort Steam persona name for this save's account, for logging; null when unknown.</summary>
    private string? ResolvePersonaName()
    {
        try
        {
            return Core.Steam.SteamPersonaIndex.LoadMachineAccounts().TryGetValue(OwnerId, out var name)
                ? name
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The sidecar JSON file the export/import workflow uses.</summary>
    public string JsonPath => _path + ".json";

    public bool JsonFileExists => File.Exists(JsonPath);

    public async Task ExportJsonAsync()
    {
        try
        {
            IsSaving = true;
            RawStatus = "Exporting JSON…";
            await Task.Run(() => SaveJsonBridge.ExportJsonToFile(_data.Raw, JsonPath));
            RawStatus = $"Exported {new FileInfo(JsonPath).Length / 1024.0 / 1024.0:F1} MB → {Path.GetFileName(JsonPath)}";
            Core.Diagnostics.EditorLog.Info("Json", $"Exported player JSON: {Path.GetFileName(JsonPath)}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JsonFileExists)));
            (ImportJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Hand off to the user's default .json editor / share sheet.
            await Launcher.Default.OpenAsync(new OpenFileRequest("Exported save JSON", new ReadOnlyFile(JsonPath)));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("Json", $"Export player JSON failed: {Path.GetFileName(JsonPath)}", ex);
            RawStatus = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task ImportJsonAsync()
    {
        if (!JsonFileExists) return;
        try
        {
            IsSaving = true;
            RawStatus = "Importing JSON…";
            await Task.Run(() => SaveJsonBridge.ImportJsonFromFile(JsonPath, _path));
            Core.Diagnostics.EditorLog.Info("Json", $"Imported player JSON into {Path.GetFileName(_path)} from {Path.GetFileName(JsonPath)}");
            RawStatus = $"Imported at {DateTime.Now:HH:mm:ss} · reload the file from the sidebar to see the changes.";
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("Json", $"Import player JSON failed: {Path.GetFileName(JsonPath)}", ex);
            RawStatus = $"Import failed (save untouched): {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any slot mutation -> re-evaluate dirty/save state + weight readout.
        if (e.PropertyName is nameof(InventorySlotViewModel.Count) or nameof(InventorySlotViewModel.ItemId))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalWeight)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OccupiedSlotCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackpackTitle)));
        }
        if (e.PropertyName is nameof(InventorySlotViewModel.IsDirty))
        {
            Refresh();
        }
    }

    public PlayerInventory Inventory => _data.Inventory;
    public string FileName => Path.GetFileName(_path);

    // ---------- save version compatibility ----------

    /// <summary>The save's ABF_SAVE_VERSION header value (null when the save class is unknown).</summary>
    public int? AbfVersion => _data.AbfVersion;

    /// <summary>
    /// Non-null when this save is newer than the version the editor was built against,
    /// or its save class wasn't recognized. Bind a warning banner to this.
    /// </summary>
    public string? CompatibilityWarning { get; }

    public bool HasCompatibilityWarning => CompatibilityWarning is not null;

    public IReadOnlyList<InventorySlotViewModel> Equipment { get; }
    public IReadOnlyList<InventorySlotViewModel> Hotbar { get; }
    public IReadOnlyList<InventorySlotViewModel> Main { get; }

    // ---------- transmog (cosmetic armor overrides, stored per player save) ----------

    /// <summary>The 6 TransmogInventory_ slots - only the item id drives appearance.</summary>
    public IReadOnlyList<InventorySlotViewModel> Transmog { get; }

    /// <summary>The 12 TransmogVisibility_ "show this armor piece" flags (all written back).</summary>
    public IReadOnlyList<TransmogToggleViewModel> TransmogToggles { get; }

    /// <summary>The UI subset of <see cref="TransmogToggles"/>: visual gear slots only.</summary>
    public IReadOnlyList<TransmogToggleViewModel> VisibleTransmogToggles { get; }

    public bool HasTransmog => Transmog.Count > 0;

    // ---------- respawn / home (editable; bed claims resolve in MainViewModel) ----------

    private double _respawnX, _respawnY, _respawnZ;
    private (double X, double Y, double Z, string? OptionGuid) _respawnBaseline;
    private IReadOnlyList<WorldLevelOption> _levelOptions = Array.Empty<WorldLevelOption>();
    private WorldLevelOption? _selectedLevel;

    public bool HasRespawnInfo => _data.RespawnLevelGuid is not null;

    public double RespawnX { get => _respawnX; set => Set(ref _respawnX, value); }
    public double RespawnY { get => _respawnY; set => Set(ref _respawnY, value); }
    public double RespawnZ { get => _respawnZ; set => Set(ref _respawnZ, value); }

    /// <summary>Sibling world saves' LevelGUIDs, labeled by region name.</summary>
    public IReadOnlyList<WorldLevelOption> LevelOptions
    {
        get => _levelOptions;
        private set
        {
            _levelOptions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLevel)));
        }
    }

    public WorldLevelOption? SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (Equals(_selectedLevel, value)) return;
            _selectedLevel = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLevel)));
            // A user-picked region keeps the spawn coordinates honest: snap to that
            // region's punch-card terminal (a guaranteed-walkable anchor) when one
            // exists. Guarded against the save's own region - Picker binding churn
            // during load replays the stored value, which must not move the spot.
            if (value is not null
                && !string.Equals(value.OptionGuid, _data.RespawnLevelGuid, StringComparison.OrdinalIgnoreCase)
                && Core.PlayerSaves.RespawnTerminalCatalog.ForRegionLabel(value.Label) is { } anchor)
            {
                RespawnX = anchor.X;
                RespawnY = anchor.Y;
                RespawnZ = anchor.Z;
            }
            Refresh();
        }
    }

    // The 10 static punch-card terminals (GUID -> area name). The save's value may be a
    // terminal the catalog doesn't know (new game sector) - keep it selectable raw.
    private RespawnTerminalOption? _selectedTerminal;
    private string? _terminalBaseline;

    public IReadOnlyList<RespawnTerminalOption> TerminalOptions { get; private set; } = Array.Empty<RespawnTerminalOption>();

    public RespawnTerminalOption? SelectedTerminal
    {
        get => _selectedTerminal;
        set
        {
            if (Equals(_selectedTerminal, value)) return;
            _selectedTerminal = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTerminal)));
            // Terminal positions are baked into the Facility map - picking one also
            // snaps the spawn coordinates onto it. Guarded against the save's own
            // terminal - Picker binding churn during load replays the stored value,
            // which must not move the spot.
            if (value is not null
                && !string.Equals(value.OptionGuid, _terminalBaseline, StringComparison.OrdinalIgnoreCase)
                && Core.PlayerSaves.RespawnTerminalCatalog.Find(value.OptionGuid) is { } terminal)
            {
                RespawnX = terminal.X;
                RespawnY = terminal.Y;
                RespawnZ = terminal.Z;
            }
            Refresh();
        }
    }

    private void InitializeTerminalOptions()
    {
        _terminalBaseline = _data.TerminalRespawnId;
        var options = RespawnTerminalCatalog.All
            .Select(t => new RespawnTerminalOption(t.TerminalGuid, t.LocationName))
            .ToList();
        if (_data.TerminalRespawnId is { } current
            && RespawnTerminalCatalog.NameFor(current) is null)
        {
            options.Insert(0, new RespawnTerminalOption(current, $"Unknown terminal ({current[..Math.Min(8, current.Length)]}…)"));
        }
        TerminalOptions = options;
        _selectedTerminal = options.FirstOrDefault(o =>
            string.Equals(o.OptionGuid, _data.TerminalRespawnId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsTerminalDirty() =>
        _selectedTerminal is not null
        && !string.Equals(_selectedTerminal.OptionGuid, _terminalBaseline, StringComparison.OrdinalIgnoreCase);

    // Threshold 0.75: the XYZ entries display rounded values (F0) and write them back
    // through the two-way binding on load - sub-unit drift must not read as an edit.
    private bool IsRespawnDirty() =>
        Math.Abs(_respawnX - _respawnBaseline.X) > 0.75 ||
        Math.Abs(_respawnY - _respawnBaseline.Y) > 0.75 ||
        Math.Abs(_respawnZ - _respawnBaseline.Z) > 0.75 ||
        IsTerminalDirty() ||
        !string.Equals(_selectedLevel?.OptionGuid ?? _respawnBaseline.OptionGuid, _respawnBaseline.OptionGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>Move the spawn location (used by the bed picker in the shell).</summary>
    public void SetRespawnLocation(double x, double y, double z)
    {
        RespawnX = x;
        RespawnY = y;
        RespawnZ = z;
    }

    private void InitializeRespawn()
    {
        (_respawnX, _respawnY, _respawnZ) = (_data.RespawnX, _data.RespawnY, _data.RespawnZ);
        _respawnBaseline = (_data.RespawnX, _data.RespawnY, _data.RespawnZ, _data.RespawnLevelGuid);
        InitializeTerminalOptions();

        // Level options come from a raw scan of the sibling world saves - fast, but file
        // IO, so off the UI thread.
        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(_path));
        if (worldDir is null) return;
        _ = Task.Run(() =>
        {
            var scanned = Core.WorldSaves.WorldLevelIndex.ScanFolder(worldDir);
            var levels = scanned
                .Select(l => new WorldLevelOption(l.LevelGuid, l.DisplayName))
                .ToList();
            var currentFile = scanned.FirstOrDefault(l =>
                string.Equals(l.LevelGuid, _data.RespawnLevelGuid, StringComparison.OrdinalIgnoreCase))?.FileName;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LevelOptions = levels;
                _selectedLevel = levels.FirstOrDefault(l =>
                    string.Equals(l.OptionGuid, _data.RespawnLevelGuid, StringComparison.OrdinalIgnoreCase));
                RespawnLevelFileName = currentFile is { } f && f.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase)
                    ? f["WorldSave_".Length..]
                    : currentFile;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLevel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RespawnLevelText)));
            });
        });
    }

    /// <summary>The player's last safe position (the spawn anchor written by the game).</summary>
    public (double X, double Y, double Z) LastSafePosition
        => (_data.RespawnX, _data.RespawnY, _data.RespawnZ);

    /// <summary>
    /// The cooked level name of the player's last region ("Facility_Office1"), resolved
    /// from the level GUID against the sibling world saves; null until the scan lands.
    /// The shell uses it to find ground items near the player.
    /// </summary>
    public string? RespawnLevelFileName
    {
        get;
        private set
        {
            field = value;
            GroundContextReady?.Invoke();
        }
    }

    /// <summary>Raised when <see cref="RespawnLevelFileName"/> resolves (off-thread scan).</summary>
    internal event Action? GroundContextReady;

    /// <summary>"in level Facility Office1" - friendly name when resolvable, else the GUID.</summary>
    public string RespawnLevelText
    {
        get
        {
            var guid = _data.RespawnLevelGuid;
            if (guid is null) return "No respawn level recorded.";
            var match = _levelOptions.FirstOrDefault(l => string.Equals(l.OptionGuid, guid, StringComparison.OrdinalIgnoreCase));
            return match is not null ? $"Level: {match.Label}" : $"Level GUID: {guid}";
        }
    }

    // ---------- appearance (per-account ScientistCustomization save) ----------

    private CustomizationViewModel? _appearance;
    private GamePassSaveSet? _gamePassSet;

    /// <summary>
    /// Provides the Game Pass save set so appearance editing can read/write the
    /// <c>ProfileScientistCustomization_&lt;n&gt;</c> wgs container. Must be called before
    /// the APPEARANCE tab is first opened (while <see cref="Appearance"/> is still null).
    /// </summary>
    public void SetGamePassContext(GamePassSaveSet set) => _gamePassSet = set;

    /// <summary>Head/hair/clothing editor over the per-account customization save.</summary>
    public CustomizationViewModel Appearance => _appearance ??= new CustomizationViewModel(SteamId64, _gamePassSet);

    // Named accessors so the XAML can position equipment slots paper-doll style.
    // Indexes were verified against this account's player save dump - they may need
    // adjusting once we sample more characters.
    public InventorySlotViewModel? Helmet     => At(Equipment, 1);
    public InventorySlotViewModel? Chest      => At(Equipment, 0);
    public InventorySlotViewModel? Legs       => At(Equipment, 2);
    public InventorySlotViewModel? Backpack   => At(Equipment, 3);
    public InventorySlotViewModel? Arms       => At(Equipment, 4);
    public InventorySlotViewModel? EyesEmpty  => At(Equipment, 5);
    public InventorySlotViewModel? Headlamp   => At(Equipment, 6);
    public InventorySlotViewModel? TrinketA   => At(Equipment, 7);
    public InventorySlotViewModel? Watch      => At(Equipment, 8);
    public InventorySlotViewModel? Tool       => At(Equipment, 9);
    public InventorySlotViewModel? Shield     => At(Equipment, 10);
    public InventorySlotViewModel? TrinketB   => At(Equipment, 11);
    public InventorySlotViewModel? ExtraEmpty => At(Equipment, 12);

    /// <summary>
    /// Backpack-grid title like the game's: the equipped pack's name + its capacity
    /// (ItemTable EquipmentData.ContainerCapacity). The save's Inventory_ length tracks
    /// the equipped pack, so a mismatch means the pack was swapped externally.
    /// </summary>
    public string BackpackTitle
    {
        get
        {
            var entry = GameDataServices.Catalog?.Find(Backpack?.ItemId);
            if (entry is null || entry.ContainerCapacity <= 0) return "POCKETS";
            var name = entry.DisplayName.ToUpperInvariant();
            return entry.ContainerCapacity == Main.Count
                ? $"{name} - {entry.ContainerCapacity} SLOTS"
                : $"{name} - capacity {entry.ContainerCapacity}, save has {Main.Count} (game resizes on equip)";
        }
    }

    /// <summary>Sum of <c>Count × catalog.Weight</c> across every inventory.</summary>
    public double TotalWeight
    {
        get
        {
            double w = 0;
            foreach (var s in Equipment) w += s.Count * (s.Entry?.Weight ?? 0);
            foreach (var s in Hotbar) w += s.Count * (s.Entry?.Weight ?? 0);
            foreach (var s in Main) w += s.Count * (s.Entry?.Weight ?? 0);
            return w;
        }
    }

    public int OccupiedSlotCount => Equipment.Count(s => !s.IsEmpty)
                                  + Hotbar.Count(s => !s.IsEmpty)
                                  + Main.Count(s => !s.IsEmpty);
    public int TotalSlotCount => Equipment.Count + Hotbar.Count + Main.Count;

    private static InventorySlotViewModel? At(IReadOnlyList<InventorySlotViewModel> list, int i)
        => i >= 0 && i < list.Count ? list[i] : null;

    // Equipment slot semantics, verified against the game's own W_Inventory_EquipSlots
    // widget (per-slot SlotIndex + tooltip; see docs/research-transmog-appearance.md).
    internal static readonly Dictionary<int, string> EquipmentRoleMap = new()
    {
        { 0, "CHEST" },
        { 1, "HEAD" },
        { 2, "LEGS" },
        { 3, "BACK" },
        { 4, "ARMS" },
        { 5, "SUIT" },      // full body suit (hazmat etc.)
        { 6, "HEADLAMP" },
        { 7, "TRINKET" },
        { 8, "WATCH" },
        { 9, "HACKER" },    // hacking device (keypad hacker)
        { 10, "SHIELD" },
        { 11, "TRINKET" },
        { 12, "PET" },      // companion slot
    };

    // The 6 transmog slots mirror equipment indices 0–5 (W_Inventory_Transmog widget).
    internal static readonly Dictionary<int, string> TransmogRoleMap = new()
    {
        { 0, "CHEST" },
        { 1, "HEAD" },
        { 2, "LEGS" },
        { 3, "BACK" },
        { 4, "ARMS" },
        { 5, "SUIT" },
    };

    private static List<InventorySlotViewModel> BuildSlotList(
        InventoryKind kind,
        IReadOnlyList<InventoryItemSlot> slots,
        IReadOnlyDictionary<int, string>? roleMap = null)
    {
        var catalog = GameDataServices.Catalog;
        var result = new List<InventorySlotViewModel>(slots.Count);
        foreach (var s in slots)
        {
            var entry = s.IsEmpty ? null : catalog?.Find(s.ItemId);
            string? role = null;
            roleMap?.TryGetValue(s.Index, out role);
            var vm = new InventorySlotViewModel(kind, s, entry, iconPath: null, role);
            vm.EnsureIcon(); // async - icons pop in without blocking the load
            result.Add(vm);
        }
        return result;
    }

    public double Hunger { get => _hunger; set => Set(ref _hunger, value); }
    public double Thirst { get => _thirst; set => Set(ref _thirst, value); }
    public double Sanity { get => _sanity; set => Set(ref _sanity, value); }
    public double Fatigue { get => _fatigue; set => Set(ref _fatigue, value); }
    public double Continence { get => _continence; set => Set(ref _continence, value); }
    public int Money { get => _money; set => Set(ref _money, value); }

    public bool IsSaving
    {
        get => _isSaving;
        private set => Set(ref _isSaving, value);
    }

    public string? SaveStatus
    {
        get => _saveStatus;
        private set => Set(ref _saveStatus, value);
    }

    // ---------- Carried pets (Companion slot / hotbar pet items) ----------

    private readonly List<CarriedPetViewModel> _carriedPets;
    private CarriedPetViewModel? _selectedCarriedPet;

    public IReadOnlyList<CarriedPetViewModel> CarriedPets => _carriedPets;
    public bool HasCarriedPets => _carriedPets.Count > 0;
    private bool ArePetsDirty() => _carriedPets.Any(p => p.IsDirty);

    /// <summary>The carried pet shown in the COMPANIONS detail pane.</summary>
    public CarriedPetViewModel? SelectedCarriedPet
    {
        get => _selectedCarriedPet;
        set
        {
            if (ReferenceEquals(_selectedCarriedPet, value)) return;
            _selectedCarriedPet = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCarriedPet)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedCarriedPet)));
        }
    }

    public bool HasSelectedCarriedPet => _selectedCarriedPet is not null;

    // ---------- Send a carried pet to the world (cross-save) ----------

    private IReadOnlyList<SaveTarget>? _siblingWorlds;
    private SaveTarget? _selectedSiblingWorld;
    private string? _petMoveStatus;

    /// <summary>World saves found next to this player (one folder up from PlayerData/).</summary>
    public IReadOnlyList<SaveTarget> SiblingWorlds => _siblingWorlds ??=
        Core.WorldSaves.PetSaveLocator.SiblingWorldSaves(_path)
            .Select(p => new SaveTarget(p, Path.GetFileName(p))).ToList();

    public bool HasSiblingWorlds => SiblingWorlds.Count > 0;

    public SaveTarget? SelectedSiblingWorld
    {
        get => _selectedSiblingWorld ??= (SiblingWorlds.Count > 0 ? SiblingWorlds[0] : null);
        set
        {
            if (ReferenceEquals(_selectedSiblingWorld, value)) return;
            _selectedSiblingWorld = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSiblingWorld)));
            _ = LoadBedsAsync(value);
        }
    }

    // The pet beds in the selected world, loaded off-thread when the world changes, so the user
    // sends a pet to a chosen bed rather than silently to the first one.
    private readonly System.Collections.ObjectModel.ObservableCollection<PetBedOption> _siblingWorldBeds = new();
    private bool _bedsRequested;
    private PetBedOption? _selectedBed;

    public System.Collections.ObjectModel.ObservableCollection<PetBedOption> SiblingWorldBeds
    {
        get
        {
            if (!_bedsRequested)
            {
                _bedsRequested = true;
                _ = LoadBedsAsync(SelectedSiblingWorld);
            }
            return _siblingWorldBeds;
        }
    }

    public bool HasSiblingWorldBeds => _siblingWorldBeds.Count > 0;

    public PetBedOption? SelectedBed
    {
        get => _selectedBed;
        set { _selectedBed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBed))); }
    }

    private async Task LoadBedsAsync(SaveTarget? world)
    {
        _siblingWorldBeds.Clear();
        SelectedBed = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSiblingWorldBeds)));
        if (world is null)
        {
            return;
        }
        try
        {
            var beds = await Task.Run(() =>
            {
                var data = Core.WorldSaves.WorldSaveReader.ReadFromFile(world.Path);
                var area = Core.WorldSaves.WorldAreaCatalog.FriendlyNameFromSaveFile(world.Path);
                return data.Deployables.Where(d => d.IsPetBed)
                    .Select((d, i) => new PetBedOption(
                        d.X, d.Y, d.Z,
                        $"Bed {i + 1}: {d.DisplayName}"
                            + (area is { Length: > 0 } ? $" - {area}" : string.Empty)))
                    .ToList();
            });
            foreach (var b in beds)
            {
                _siblingWorldBeds.Add(b);
            }
            SelectedBed = _siblingWorldBeds.FirstOrDefault();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSiblingWorldBeds)));
        }
        catch (Exception ex)
        {
            PetMoveStatus = $"Could not read pet beds in {world.Name}: {ex.Message}";
        }
    }

    public string? PetMoveStatus
    {
        get => _petMoveStatus;
        private set { _petMoveStatus = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PetMoveStatus))); }
    }

    public ICommand SendPetToWorldCommand { get; private set; } = null!;

    private async Task SendPetToWorldAsync()
    {
        var pet = _selectedCarriedPet;
        if (pet is null) return;
        if (SelectedSiblingWorld is not { } target)
        {
            PetMoveStatus = "No world save was found next to this player.";
            return;
        }
        if (IsDirty)
        {
            PetMoveStatus = "Save or revert your other player changes first.";
            return;
        }
        try
        {
            var world = Core.WorldSaves.WorldSaveReader.ReadFromFile(target.Path);
            // Place the pet at the chosen bed; fall back to any pet bed, else the world origin.
            var chosen = SelectedBed;
            var fallbackBed = world.Deployables.FirstOrDefault(d => d.IsPetBed);
            double x = chosen?.X ?? fallbackBed?.X ?? 0;
            double y = chosen?.Y ?? fallbackBed?.Y ?? 0;
            double z = chosen?.Z ?? fallbackBed?.Z ?? 0;
            var result = await Task.Run(() =>
            {
                var r = Core.WorldSaves.PetTransfer.PlayerToWorld(_data, pet.Slot, pet.Index, world, x, y, z);
                if (r.Ok)
                {
                    // Write the gaining file (world) first, then the player that lost the pet.
                    Core.WorldSaves.WorldSaveWriter.WriteToFile(world, target.Path);
                    PlayerSaveWriter.WriteToFile(_data, _path);
                }
                return r;
            });
            if (!result.Ok) { PetMoveStatus = result.Message; return; }

            _carriedPets.Remove(pet);
            if (ReferenceEquals(_selectedCarriedPet, pet)) SelectedCarriedPet = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarriedPets)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCarriedPets)));
            var where = chosen?.Label ?? (fallbackBed is not null ? "a pet bed" : "the world origin");
            PetMoveStatus = $"{result.Message} (placed at {where} in {target.Name})";
            // The world now has one more pet; refresh the bed list's source world parse is unneeded.
            Refresh();
        }
        catch (Exception ex)
        {
            PetMoveStatus = $"Move failed: {ex.Message}";
        }
    }

    public bool IsDirty =>
        ArePetsDirty() ||
        Drifted(_hunger, _baseline.Hunger) ||
        Drifted(_thirst, _baseline.Thirst) ||
        Drifted(_sanity, _baseline.Sanity) ||
        Drifted(_fatigue, _baseline.Fatigue) ||
        Drifted(_continence, _baseline.Continence) ||
        _money != _baseline.Money ||
        _phd != _phdBaseline ||
        AreTraitsDirty() ||
        IsHealthDirty() ||
        RecipeBrowser.IsDirty ||
        Codex.IsDirty ||
        IsDiscoveryDirty() ||
        Skills.Any(s => s.IsDirty) ||
        TransmogToggles.Any(t => t.IsDirty) ||
        IsRespawnDirty() ||
        AllSlots().Any(s => s.IsDirty);

    /// <summary>
    /// Names every dirty contributor - logged by the leave-gate so a phantom
    /// "unsaved changes" dialog (a binding write-back masquerading as an edit)
    /// identifies its source instead of needing a repro hunt.
    /// </summary>
    public string DescribeDirty()
    {
        var parts = new List<string>();
        if (Drifted(_hunger, _baseline.Hunger)) parts.Add($"hunger {_baseline.Hunger:F2}->{_hunger:F2}");
        if (Drifted(_thirst, _baseline.Thirst)) parts.Add($"thirst {_baseline.Thirst:F2}->{_thirst:F2}");
        if (Drifted(_sanity, _baseline.Sanity)) parts.Add($"sanity {_baseline.Sanity:F2}->{_sanity:F2}");
        if (Drifted(_fatigue, _baseline.Fatigue)) parts.Add($"fatigue {_baseline.Fatigue:F2}->{_fatigue:F2}");
        if (Drifted(_continence, _baseline.Continence)) parts.Add($"continence {_baseline.Continence:F2}->{_continence:F2}");
        if (_money != _baseline.Money) parts.Add($"money {_baseline.Money}->{_money}");
        if (_phd != _phdBaseline) parts.Add("background");
        if (AreTraitsDirty()) parts.Add("traits");
        if (IsHealthDirty()) parts.Add("body health");
        if (RecipeBrowser.IsDirty) parts.Add("recipes");
        if (Codex.IsDirty) parts.Add("codex");
        if (IsDiscoveryDirty()) parts.Add("discovery lists");
        parts.AddRange(Skills.Where(s => s.IsDirty)
            .Select(s => $"skill {s.DisplayName} {s.OriginalSkill.Xp:F1}->{s.Xp:F1}"));
        if (TransmogToggles.Any(t => t.IsDirty)) parts.Add("transmog visibility");
        if (IsRespawnDirty()) parts.Add($"respawn ({_respawnBaseline.X:F1},{_respawnBaseline.Y:F1},{_respawnBaseline.Z:F1})->({_respawnX:F1},{_respawnY:F1},{_respawnZ:F1})");
        parts.AddRange(AllSlots().Where(s => s.IsDirty)
            .Select(s => $"slot {s.Kind} #{s.Index} ({s.ItemId})"));
        return string.Join("; ", parts);
    }

    public InventorySlotViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot == value) return;
            _selectedSlot = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSlot)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedSlot)));
        }
    }

    public bool HasSelectedSlot => _selectedSlot is not null;
    public ICommand ClearSelectionCommand { get; }

    private SkillMilestoneViewModel? _selectedMilestone;

    /// <summary>The skill milestone whose detail card shows in the right slot panel (tap a chip).</summary>
    public SkillMilestoneViewModel? SelectedMilestone
    {
        get => _selectedMilestone;
        set
        {
            if (ReferenceEquals(_selectedMilestone, value)) return;
            _selectedMilestone = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMilestone)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedMilestone)));
        }
    }

    public bool HasSelectedMilestone => _selectedMilestone is not null;

    private IEnumerable<InventorySlotViewModel> AllSlots()
    {
        foreach (var s in Equipment) yield return s;
        foreach (var s in Hotbar) yield return s;
        foreach (var s in Main) yield return s;
        foreach (var s in Transmog) yield return s;
    }

    // ---------- ground pickups (staged with the inventory edit) ----------

    /// <summary>
    /// Dropped-item entries to delete from their world saves when THIS save is
    /// written: (world file, DroppedItemMap key). Filled by the pick-up flow; the
    /// matching item already sits staged in an inventory slot.
    /// </summary>
    internal List<(string WorldFile, string DroppedId)> PendingGroundPickups { get; } = new();

    /// <summary>Raised after pickups were written so the shell can refresh world caches.</summary>
    internal event Action<IReadOnlyList<string>>? GroundPickupsCommitted;

    /// <summary>
    /// Items to drop onto the world ground when THIS save is written: (world file, the item
    /// slot, world X/Y/Z). Filled by the inventory DROP flow; the matching slot is already
    /// cleared. Committed by <see cref="CommitGroundDropsAsync"/> together with the player save.
    /// </summary>
    internal List<(string WorldFile, Core.PlayerSaves.InventoryItemSlot Slot, double X, double Y, double Z)> PendingGroundDrops { get; } = new();

    /// <summary>Raised after drops were written so the shell can refresh the nearby-items list.</summary>
    internal event Action<IReadOnlyList<string>>? GroundDropsCommitted;

    private async Task CommitGroundDropsAsync()
    {
        if (PendingGroundDrops.Count == 0) return;

        var byFile = PendingGroundDrops
            .GroupBy(d => d.WorldFile, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var touched = new List<string>();
        await Task.Run(() =>
        {
            foreach (var group in byFile)
            {
                try
                {
                    var data = Core.WorldSaves.WorldSaveReader.ReadFromFile(group.Key);
                    var added = group.Count(d =>
                        Core.WorldSaves.WorldSaveWriter.AddDroppedItem(data, d.Slot, d.X, d.Y, d.Z) is not null);
                    if (added > 0)
                    {
                        Core.WorldSaves.WorldSaveWriter.WriteToFile(data, group.Key);
                        touched.Add(group.Key);
                        Core.Diagnostics.EditorLog.Info(
                            "Drop", $"{Path.GetFileName(group.Key)}: wrote {added} dropped item(s) to the ground.");
                    }
                    else
                    {
                        Core.Diagnostics.EditorLog.Warn(
                            "Drop", $"{Path.GetFileName(group.Key)}: no existing ground item to clone - drop(s) skipped.");
                    }
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.EditorLog.Error(
                        "Drop", $"Could not add dropped items to {group.Key}", ex);
                }
            }
        });
        PendingGroundDrops.Clear();
        if (touched.Count > 0)
        {
            GroundDropsCommitted?.Invoke(touched);
        }
    }

    private async Task CommitGroundPickupsAsync()
    {
        if (PendingGroundPickups.Count == 0) return;

        var byFile = PendingGroundPickups
            .GroupBy(p => p.WorldFile, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var touched = new List<string>();
        await Task.Run(() =>
        {
            foreach (var group in byFile)
            {
                try
                {
                    var data = Core.WorldSaves.WorldSaveReader.ReadFromFile(group.Key);
                    var removed = group.Count(p =>
                        Core.WorldSaves.WorldSaveWriter.RemoveDroppedItem(data, p.DroppedId));
                    if (removed > 0)
                    {
                        Core.WorldSaves.WorldSaveWriter.WriteToFile(data, group.Key);
                        touched.Add(group.Key);
                        Core.Diagnostics.EditorLog.Info(
                            "Pickup", $"{Path.GetFileName(group.Key)}: removed {removed} picked-up ground item(s).");
                    }
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.EditorLog.Error(
                        "Pickup", $"Could not remove picked-up items from {group.Key}", ex);
                }
            }
        });
        PendingGroundPickups.Clear();
        if (touched.Count > 0)
        {
            GroundPickupsCommitted?.Invoke(touched);
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }

    public async Task SaveAsync()
    {
        if (!IsDirty || IsSaving) return;

        IsSaving = true;
        SaveStatus = "Saving…";
        try
        {
            var updatedStats = new CharacterStats(_hunger, _thirst, _sanity, _fatigue, _continence, _money);
            var equip = Equipment.Select(s => s.ToCurrentSlot()).ToList();
            var hotbar = Hotbar.Select(s => s.ToCurrentSlot()).ToList();
            var main = Main.Select(s => s.ToCurrentSlot()).ToList();
            var updatedInv = new PlayerInventory(equip, hotbar, main);
            var updatedSkills = Skills.Select(s => s.ToCurrentSkill()).ToList();
            var updatedTraits = Traits.Select(t => t.Id).ToList();
            var updatedPhd = _phd;
            var updatedHealth = CurrentHealth();
            var updatedRecipes = RecipeBrowser.CurrentList();
            var updatedEmails = Codex.CurrentEmailsRead();
            var updatedJournals = Codex.CurrentJournals();
            var updatedCompendium = Codex.CurrentCompendiumLists();
            var updatedKills = Codex.CurrentKillCounts();
            var updatedFish = Codex.CurrentFishCaught();
            var updatedPickedUp = _itemsPickedUp.ToList();
            var updatedCrafted = _craftedItems.ToList();
            var updatedMaps = _mapsUnlocked.ToList();

            // An item the player is holding must have been seen, so mark every item now in the
            // inventory/hotbar/equipment as discovered. This is what makes a freshly added item
            // (dragged in from the palette, or sent from a container) show up as discovered in-game
            // instead of as an unknown "???" the field guide never registered.
            MarkHeldItemsDiscovered(updatedPickedUp, equip, hotbar, main);
            var updatedTransmog = Transmog.Select(s => s.ToCurrentSlot()).ToList();
            var updatedTransmogVisibility = TransmogToggles.Select(t => t.IsVisible).ToList();
            // Carried-pet edits/deletions are applied AFTER ApplyInventory so the pet-specific
            // fields (health/XP/mutation via the pet writers) win over the generic slot pass.
            var petEdits = _carriedPets.Where(p => !p.IsDeleted && p.IsDirty).Select(p => p.ToCurrent()).ToList();
            var petDeletes = _carriedPets.Where(p => p.IsDeleted).Select(p => (p.Slot, p.Index)).ToList();

            await Task.Run(() =>
            {
                PlayerSaveWriter.ApplyStats(_data, updatedStats);
                PlayerSaveWriter.ApplyInventory(_data, updatedInv);
                foreach (var p in petEdits) PlayerSaveWriter.ApplyCarriedPet(_data, p);
                foreach (var (kind, idx) in petDeletes) PlayerSaveWriter.RemoveCarriedPet(_data, kind, idx);
                PlayerSaveWriter.ApplySkills(_data, updatedSkills);
                PlayerSaveWriter.ApplyTraits(_data, updatedTraits);
                if (!string.IsNullOrEmpty(updatedPhd))
                {
                    PlayerSaveWriter.ApplyPhd(_data, updatedPhd);
                }
                PlayerSaveWriter.ApplyLimbHealth(_data, updatedHealth);
                PlayerSaveWriter.ApplyRecipes(_data, updatedRecipes);
                PlayerSaveWriter.ApplyEmailsRead(_data, updatedEmails);
                PlayerSaveWriter.ApplyJournals(_data, updatedJournals);
                PlayerSaveWriter.ApplyCompendium(_data, updatedCompendium.Email, updatedCompendium.Narrative, updatedCompendium.Exploration);
                PlayerSaveWriter.ApplyKillCounts(_data, updatedKills);
                PlayerSaveWriter.ApplyFishCaught(_data, updatedFish);
                PlayerSaveWriter.ApplyItemsPickedUp(_data, updatedPickedUp);
                PlayerSaveWriter.ApplyCraftedItems(_data, updatedCrafted);
                PlayerSaveWriter.ApplyMapsUnlocked(_data, updatedMaps);
                PlayerSaveWriter.ApplyTransmogSlots(_data, updatedTransmog);
                PlayerSaveWriter.ApplyTransmogVisibility(_data, updatedTransmogVisibility);
                if (IsRespawnDirty())
                {
                    PlayerSaveWriter.ApplyRespawn(_data, _respawnX, _respawnY, _respawnZ, _selectedLevel?.OptionGuid);
                    if (IsTerminalDirty())
                    {
                        PlayerSaveWriter.ApplyRespawnTerminal(_data, _selectedTerminal!.OptionGuid);
                    }
                }
                PlayerSaveWriter.WriteToFile(_data, _path);
            });

            // Ground pickups commit only once the player's inventory is safely on
            // disk - removing the world entry first could lose the item if the player
            // save failed or was discarded.
            await CommitGroundPickupsAsync();
            await CommitGroundDropsAsync();

            _baseline = updatedStats;
            // Slot VMs need their OriginalSlot baselines refreshed so IsDirty flips back.
            foreach (var s in AllSlots())
            {
                s.AcceptCurrentAsBaseline();
            }
            if (petDeletes.Count > 0)
            {
                if (_selectedCarriedPet is not null && _selectedCarriedPet.IsDeleted) SelectedCarriedPet = null;
                _carriedPets.RemoveAll(p => p.IsDeleted);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CarriedPets)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCarriedPets)));
            }
            foreach (var p in _carriedPets) p.AcceptBaseline();
            foreach (var s in Skills)
            {
                s.AcceptCurrentAsBaseline();
            }
            foreach (var t in TransmogToggles)
            {
                t.AcceptCurrentAsBaseline();
            }
            _respawnBaseline = (_respawnX, _respawnY, _respawnZ, _selectedLevel?.OptionGuid ?? _respawnBaseline.OptionGuid);
            _terminalBaseline = _selectedTerminal?.OptionGuid ?? _terminalBaseline;
            _traitBaseline.Clear();
            _traitBaseline.AddRange(updatedTraits);
            _phdBaseline = _phd;
            _healthBaseline = updatedHealth;
            RecipeBrowser.AcceptCurrentAsBaseline();
            Codex.AcceptCurrentAsBaseline();
            (_itemsPickedUpBaseline, _craftedItemsBaseline, _mapsBaseline) =
                (_itemsPickedUp.Count, _craftedItems.Count, _mapsUnlocked.Count);
            var persona = ResolvePersonaName();
            Core.Diagnostics.EditorLog.Info("PlayerSave",
                $"Saved player {SteamId64}{(persona is null ? string.Empty : $" ({persona})")} - {Path.GetFileName(_path)}");
            SaveStatus = $"Saved at {DateTime.Now:HH:mm:ss}";
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            Refresh();
        }
    }

    /// <summary>Raised after a successful save (the .sav is on disk). The host uses it to pack a
    /// Game Pass working copy back into its container.</summary>
    public event Action? Saved;

    public void Revert()
    {
        foreach (var p in _carriedPets) p.Revert();
        Hunger = _baseline.Hunger;
        Thirst = _baseline.Thirst;
        Sanity = _baseline.Sanity;
        Fatigue = _baseline.Fatigue;
        Continence = _baseline.Continence;
        Money = _baseline.Money;
        // Staged ground pickups un-stage with the slots they were placed into; the
        // items are still on the ground (world saves only change on SAVE).
        if (PendingGroundPickups.Count > 0)
        {
            PendingGroundPickups.Clear();
            GroundPickupsCommitted?.Invoke(Array.Empty<string>());
        }
        // Staged drops un-stage with the slot they were taken from; nothing hit the world yet.
        if (PendingGroundDrops.Count > 0)
        {
            PendingGroundDrops.Clear();
            GroundDropsCommitted?.Invoke(Array.Empty<string>());
        }
        foreach (var s in AllSlots()) s.Revert();
        foreach (var s in Skills) s.Revert();
        Traits.Clear();
        foreach (var t in _traitBaseline) Traits.Add(new TraitItemViewModel(t));
        _phd = _phdBaseline;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBackground)));
        (Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg) =
            (_healthBaseline.Head, _healthBaseline.Torso, _healthBaseline.LeftArm,
             _healthBaseline.RightArm, _healthBaseline.LeftLeg, _healthBaseline.RightLeg);
        RecipeBrowser.Revert();
        Codex.Revert();
        if (IsDiscoveryDirty())
        {
            _itemsPickedUp.Clear(); _itemsPickedUp.AddRange(_data.ItemsPickedUp);
            _craftedItems.Clear(); _craftedItems.AddRange(_data.CraftedItems);
            _mapsUnlocked.Clear(); _mapsUnlocked.AddRange(_data.MapsUnlocked);
            foreach (var n in new[] { nameof(ItemsPickedUpText), nameof(CraftedItemsText), nameof(MapsText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
        SaveStatus = null;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RevertCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        var old = field;
        field = value;
        // Guarded: the interpolated message would otherwise allocate on every slider
        // tick even with diagnostics off.
        if (Core.Diagnostics.EditorLog.Enabled)
        {
            Core.Diagnostics.EditorLog.Info("Edit", $"Player {propertyName}: '{old}' → '{value}'");
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        Refresh();
    }
}

/// <summary>One row of the add-trait browser: full description + point cost up front.</summary>
public sealed record TraitBrowserRow(string Id, string DisplayName, string? Description, int PointCost)
{
    // 0 is real data (world-acquired traits like Forbidden Diet) - show it explicitly.
    public string CostText => $"{PointCost:+#;-#;0} PTS";
}

/// <summary>One world region option for the respawn level picker.</summary>
public sealed record WorldLevelOption(string OptionGuid, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One punch-card respawn terminal option.</summary>
public sealed record RespawnTerminalOption(string OptionGuid, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One pet bed in a target world a carried pet can be sent to.</summary>
public sealed record PetBedOption(double X, double Y, double Z, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One TransmogVisibility_ flag ("show this armor piece" eye-toggle).</summary>
public sealed class TransmogToggleViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private bool _isVisible;
    private bool _baseline;

    public TransmogToggleViewModel(int index, bool isVisible, Action onChanged)
    {
        Index = index;
        _isVisible = _baseline = isVisible;
        _onChanged = onChanged;
    }

    public int Index { get; }

    /// <summary>Visibility flags are indexed like the equipment slots - label by role.</summary>
    public string Label => PlayerEditorViewModel.EquipmentRoleMap.TryGetValue(Index, out var role)
        ? role
        : $"S{Index:D2}";

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            Core.Diagnostics.EditorLog.Info("Edit", $"Transmog visibility {Label}: {value}");
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsVisible)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDirty)));
            _onChanged();
        }
    }

    public bool IsDirty => _isVisible != _baseline;

    public void AcceptCurrentAsBaseline()
    {
        _baseline = _isVisible;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDirty)));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
