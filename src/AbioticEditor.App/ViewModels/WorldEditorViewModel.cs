using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Editor for one loaded world save. Exposes the list of containers and a save command
/// that persists their current slot state back to disk via
/// <see cref="WorldSaveWriter"/>.
/// </summary>
public sealed class WorldEditorViewModel : INotifyPropertyChanged
{
    private readonly WorldSaveData _data;
    private readonly string _path;
    private WorldContainerViewModel? _selectedContainer;
    private bool _isSaving;
    private string? _saveStatus;
    private WorldTab _activeTab = WorldTab.Containers;
    private string _doorFilter = string.Empty;
    private string _filter = string.Empty;
    private bool _hideEmpty = true;
    private readonly HashSet<string> _flagBaseline;
    private string _flagFilter = string.Empty;
    private string _newFlagText = string.Empty;
    private readonly Dictionary<string, WorldDoorViewModel> _doorBaselines = new();
    private IReadOnlyList<WorldFeatureTabViewModel> _featureTabs = Array.Empty<WorldFeatureTabViewModel>();
    private WorldFeatureTabViewModel? _selectedFeatureTab;
    private readonly List<WorldTabButton> _tabs = new();

    // Invoked when a power socket's plugged-in device is not in this save: the host (MainViewModel)
    // resolves the folder-wide device index and, if the device is a container in a sibling world
    // save, switches to that save and opens it. Null when no host navigation is wired.
    private readonly Func<string, Task>? _crossWorldNavigate;
    private readonly Func<IReadOnlyList<string>,
        Task<IReadOnlyDictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>>>? _resolveDevices;

    public WorldEditorViewModel(
        WorldSaveData data, string path,
        Func<string, Task>? crossWorldNavigate = null,
        Func<IReadOnlyList<string>,
            Task<IReadOnlyDictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>>>? resolveDevices = null)
    {
        _data = data;
        _path = path;
        _crossWorldNavigate = crossWorldNavigate;
        _resolveDevices = resolveDevices;

        // The metadata save is story-only (no containers/flags/doors) - open it on the
        // STORY tab instead of an empty containers list.
        if (IsMetadataSave)
        {
            _activeTab = WorldTab.Meta;
        }

        // Version sanity: warn when the save is newer than this editor knows, or its
        // save class wasn't recognized at all.
        CompatibilityWarning = Core.Compatibility.SaveCompatibility.WarningFor(data.Raw);
        if (CompatibilityWarning is not null)
        {
            Core.Diagnostics.EditorLog.Warn(
                "Compatibility", $"{System.IO.Path.GetFileName(path)}: {CompatibilityWarning}");
        }

        AllContainers = data.Containers.Select(c => new WorldContainerViewModel(c)).ToList();
        VisibleContainers = AllContainers.ToList();

        // Watch every slot for edits so the save command becomes enabled.
        foreach (var c in AllContainers)
        {
            foreach (var s in c.Slots) s.PropertyChanged += OnSlotChanged;
        }

        // Flags: snapshot baseline for dirty tracking, populate observable.
        _flagBaseline = new HashSet<string>(data.Flags, StringComparer.Ordinal);
        Flags = new ObservableCollection<string>(data.Flags);
        _activeFlagSet = new HashSet<string>(data.Flags, StringComparer.OrdinalIgnoreCase);
        Flags.CollectionChanged += (_, __) =>
        {
            // Keep the O(1) lookup set in sync and drop the cached item VMs; the list
            // itself only rebuilds once per batch (see RunFlagBatch).
            _activeFlagSet.Clear();
            foreach (var f in Flags) _activeFlagSet.Add(f);
            _flagItemCache = null;
            if (_suppressFlagListRefresh) return;
            ApplyFlagFilter();
            Refresh();
            // A single flag toggle can meet (or un-meet) a trader's unlock/spoiler gate.
            RefreshTraderCards();
        };
        // Build both the flat list AND the story-ordered groups (the groups are what
        // the UI binds - constructing only the flat list would leave the tab empty).
        ApplyFlagFilter();

        // Doors: build VMs and remember baseline.
        Doors = data.Doors.Select(d => new WorldDoorViewModel(d)).ToList();
        foreach (var d in Doors)
        {
            _doorBaselines[d.Id] = new WorldDoorViewModel(d.OriginalDoor);
            d.PropertyChanged += (_, __) => Refresh();
        }

        // Metadata-save extras (story chapter + playtime + global recipes).
        _storyRowBaseline = _storyRow = data.StoryProgressionRow;
        _minutesBaseline = _minutes = data.MinutesPassed ?? 0;

        // Formerly-UNKWN extras: the Facility world clock, the region discovery day
        // and the metadata save's Leyak containment links (log review 2026-06-12).
        var clock = WorldSaveReader.ReadWorldClock(data.Raw);
        _hasWorldClock = clock is not null;
        _clockSecondsBaseline = _clockSeconds = clock?.Seconds ?? 0;
        _clockDayBaseline = _clockDay = clock?.Day ?? 0;
        var discovered = WorldSaveReader.ReadDayDiscovered(data.Raw);
        _hasDayDiscovered = discovered is not null;
        _dayDiscoveredBaseline = _dayDiscovered = discovered ?? 0;
        LeyakContainments = new ObservableCollection<LeyakContainmentViewModel>(
            WorldSaveReader.ReadLeyakContainments(data.Raw)
                .Select(k => new LeyakContainmentViewModel(k.Key, k.Value, this)));
        foreach (var prefix in new[] { WuItems, WuEmails, WuJournals, WuCompEmail, WuCompNarrative, WuCompExploration })
        {
            _worldUnlockCounts[prefix] = WorldSaveReader.ReadGlobalUnlockArray(data.Raw, prefix).Count;
        }
        LastPlayedText = WorldSaveReader.ReadLastPlayedText(data.Raw);
        GlobalRecipeBrowser = new RecipeListViewModel(data.GlobalRecipes, Refresh);
        // Selecting a recipe opens its detail in the right sidebar; the shell listens
        // for HasSelectedWorldRecipe like the door/flag/trader detail panels.
        GlobalRecipeBrowser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RecipeListViewModel.SelectedRecipe)
                or nameof(RecipeListViewModel.HasSelectedRecipe))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedWorldRecipe)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedWorldRecipe)));
            }
        };

        // Dropped items (ground litter). Slots are watched for edits like containers.
        _droppedItems = data.DroppedItems.Select(d => new DroppedItemViewModel(this, d)).ToList();
        foreach (var d in _droppedItems) d.Slot.PropertyChanged += OnSlotChanged;

        Npcs = data.Npcs.Select(n => new WorldNpcViewModel(n, Refresh)).ToList();

        // Pets get their own tab and a Fish-style detail editor. The variant catalog is
        // built once (merged from paks when present, curated otherwise) and shared by every
        // pet's upgrade picker.
        var petVariants = PetCatalog.BuildVariants(Services.GameDataServices.Provider);
        _pets = new ObservableCollection<WorldPetViewModel>(
            data.Pets.Select(p => new WorldPetViewModel(p, Refresh, petVariants)));
        // Pets store only coordinates, so the world-save file is their best area indicator.
        foreach (var pet in _pets) pet.WorldSaveFileName = _path;

        // Vehicles (region saves). On-board storage is editable via the CONTAINERS tab
        // (vehicle inventories load as Vehicle-source containers); OpenVehicleInventory jumps there.
        _vehicles = data.Vehicles.Select(v => new WorldVehicleViewModel(v, Refresh, OpenVehicleInventory)).ToList();
        VehicleGroups = _vehicles
            .GroupBy(v => string.IsNullOrEmpty(v.Region) ? "Unknown world" : v.Region)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VehicleWorldGroup(g.Key, g))
            .ToList();

        // World-state feature maps (power sockets, resource nodes, NPC spawns, triggers,
        // elevators, buttons, portals, trams, ...). Each applicable map becomes its own
        // Fish-style master-detail tab, discovered generically from this save's tree.
        // "vehicles" has a dedicated full-featured Vehicles tab, so exclude it from the generic
        // feature-tab discovery to avoid showing two Vehicles tabs.
        _featureTabs = WorldMapFeatures.ApplicableTo(data.Raw)
            .Where(f => !string.Equals(f.Id, "vehicles", StringComparison.OrdinalIgnoreCase))
            .Select(f => new WorldFeatureTabViewModel(
                f, data.Raw, Refresh, SelectFeatureTab, _path, OpenPoweredDevice, _resolveDevices))
            .ToList();

        if (IsMetadataSave)
        {
            // Metadata saves have no containers/doors - open on the story tab.
            _activeTab = WorldTab.Meta;
        }

        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => IsDirty && !IsSaving);
        RevertCommand = new RelayCommand(Revert, () => IsDirty && !IsSaving);
        AddFlagCommand = new RelayCommand(AddFlag, () => !string.IsNullOrWhiteSpace(_newFlagText));
        InitTabCommands();
        BuildTabs();
    }

    // ---------- Metadata save: story progression + playtime ----------

    private string? _storyRow;
    private string? _storyRowBaseline;
    private int _minutes;
    private int _minutesBaseline;

    /// <summary>True when the loaded file is <c>WorldSave_MetaData.sav</c>.</summary>
    public bool IsMetadataSave => _data.StoryProgressionRow is not null;

    public static IReadOnlyList<string> StoryRows => StoryProgressionCatalog.Rows;

    private IReadOnlyList<StoryChapterItemViewModel>? _chapterList;

    /// <summary>The ordered chapter checklist (lazy - only metadata saves show it).</summary>
    public IReadOnlyList<StoryChapterItemViewModel> ChapterList
        => _chapterList ??= StoryProgressionCatalog.Chapters
            .Select((c, i) => new StoryChapterItemViewModel(this, c, i))
            .ToList();

    // Story trigger flags live in WorldSave_Facility.sav; on the metadata save the
    // chapter list would otherwise read an empty flag list. Loaded lazily off-thread.
    private HashSet<string>? _facilityFlags;
    private bool _facilityFlagsRequested;

    /// <summary>
    /// Case-insensitive world-flag lookup (chapter status, trader gating...). On metadata
    /// saves, falls back to the sibling Facility save's flags.
    /// </summary>
    internal bool HasWorldFlag(string flag)
    {
        if (_activeFlagSet.Contains(flag)) return true;
        if (!IsMetadataSave) return false;
        EnsureFacilityFlags();
        return _facilityFlags?.Contains(flag) == true;
    }

    private void EnsureFacilityFlags()
    {
        if (_facilityFlagsRequested) return;
        _facilityFlagsRequested = true;

        var folder = Path.GetDirectoryName(_path);
        if (folder is null) return;
        var facility = Path.Combine(folder, "WorldSave_Facility.sav");
        if (!File.Exists(facility)) return;

        _ = Task.Run(() =>
        {
            try
            {
                var flags = WorldSaveReader.ReadFromFile(facility).Flags;
                var set = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _facilityFlags = set;
                    // Prerequisite counts on inactive flags may change now that the
                    // sibling save's flags are known. ApplyFlagFilter also refreshes
                    // the chapter states.
                    _flagItemCache = null;
                    ApplyFlagFilter();
                    // Trader availability on the metadata save reads these same flags.
                    RefreshTraderCards();
                });
            }
            catch
            {
                // Without the facility save the chapter list shows own-save flags only.
            }
        });
    }

    private StoryChapterItemViewModel? _selectedChapter;

    /// <summary>Tap a chapter row to open its quest detail in the right sidebar.</summary>
    public StoryChapterItemViewModel? SelectedChapter
    {
        get => _selectedChapter;
        set
        {
            if (Set(ref _selectedChapter, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedChapter)));
            }
        }
    }

    public bool HasSelectedChapter => _selectedChapter is not null;

    /// <summary>
    /// Adds every chapter trigger flag from the start of the story THROUGH the given
    /// chapter - the linear way to unlock a quest whose dependencies are missing.
    /// On a metadata save the flags belong to the sibling Facility save, so they are
    /// written there immediately (with backup); on region saves they stage as usual.
    /// Returns the number of flags added.
    /// </summary>
    public int UnlockChaptersThrough(StoryChapterItemViewModel chapter)
    {
        if (IsMetadataSave)
        {
            var (added, message) = StoryFlagSync.SyncFacilityFlags(_path, chapter.Chapter.Row);
            SaveStatus = message;
            if (added > 0 && _facilityFlags is not null)
            {
                for (var i = 0; i <= chapter.Index; i++)
                {
                    if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } f) _facilityFlags.Add(f);
                }
            }
            NotifyChapterStates();
            return added;
        }

        var staged = 0;
        RunFlagBatch(() =>
        {
            for (var i = 0; i <= chapter.Index; i++)
            {
                var flag = StoryProgressionCatalog.Chapters[i].TriggerFlag;
                if (flag is null || HasWorldFlag(flag)) continue;
                Flags.Add(flag);
                staged++;
            }
        });
        return staged;
    }

    internal void NotifyChapterStates()
    {
        if (_chapterList is null) return;
        foreach (var c in _chapterList) c.NotifyCurrentChanged();
    }

    public string? SelectedStoryRow
    {
        get => _storyRow;
        set
        {
            if (Set(ref _storyRow, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StoryChapterIndexText)));
                if (_chapterList is not null)
                {
                    foreach (var c in _chapterList) c.NotifyCurrentChanged();
                }
                Refresh();
            }
        }
    }

    public string StoryChapterIndexText
    {
        get
        {
            var i = StoryProgressionCatalog.IndexOf(_storyRow);
            return i < 0 ? "(unknown chapter)" : $"chapter {i + 1} of {StoryProgressionCatalog.Rows.Count}";
        }
    }

    public int MinutesPassed
    {
        get => _minutes;
        set
        {
            if (Set(ref _minutes, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlaytimeText)));
                Refresh();
            }
        }
    }

    public string PlaytimeText => $"{_minutes / 60}h {_minutes % 60:D2}m played";

    private bool IsMetaDirty()
        => IsMetadataSave
           && (!string.Equals(_storyRow, _storyRowBaseline, StringComparison.Ordinal)
               || _minutes != _minutesBaseline
               || GlobalRecipeBrowser.IsDirty);

    // ---------- World clock / discovery day / Leyak containment ----------

    private readonly bool _hasWorldClock;
    private double _clockSeconds;
    private double _clockSecondsBaseline;
    private int _clockDay;
    private int _clockDayBaseline;
    private readonly bool _hasDayDiscovered;
    private int _dayDiscovered;
    private int _dayDiscoveredBaseline;
    private readonly List<string> _leyakReleases = new();

    /// <summary>True on the save that carries the TimeOfDay struct (the Facility file).</summary>
    public bool HasWorldClock => _hasWorldClock;

    /// <summary>The world's day counter (TimeOfDay.CurrentDay).</summary>
    public int WorldDay
    {
        get => _clockDay;
        set
        {
            if (Set(ref _clockDay, value)) Refresh();
        }
    }

    /// <summary>Seconds into the current in-game day (0..86400).</summary>
    public double WorldTimeSeconds
    {
        get => _clockSeconds;
        set
        {
            if (Set(ref _clockSeconds, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorldTimeText)));
                Refresh();
            }
        }
    }

    public string WorldTimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(Math.Clamp(_clockSeconds, 0, 86400));
            return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
        }
    }

    /// <summary>True on region saves that record their first-entered day.</summary>
    public bool HasDayDiscovered => _hasDayDiscovered;

    public int DayDiscovered
    {
        get => _dayDiscovered;
        set
        {
            if (Set(ref _dayDiscovered, value)) Refresh();
        }
    }

    /// <summary>Contained entities (metadata save): creature -> containment unit GUID.</summary>
    public ObservableCollection<LeyakContainmentViewModel> LeyakContainments { get; }

    public bool HasLeyakContainments => LeyakContainments.Count > 0;

    /// <summary>Stages releasing a creature from containment (applied on SAVE).</summary>
    internal void ReleaseLeyak(LeyakContainmentViewModel entry)
    {
        _leyakReleases.Add(entry.Creature);
        LeyakContainments.Remove(entry);
        if (SelectedContainment == entry) SelectedContainment = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLeyakContainments)));
        Refresh();
    }

    private LeyakContainmentViewModel? _selectedContainment;

    /// <summary>Tap a containment row to expand its detail (creature image + where the
    /// unit physically sits in the Facility).</summary>
    public LeyakContainmentViewModel? SelectedContainment
    {
        get => _selectedContainment;
        set
        {
            var previous = _selectedContainment;
            if (Set(ref _selectedContainment, value))
            {
                // Drive each row's inline detail: collapse the old one, expand the new one
                // so the image + info appear directly under the row the user tapped.
                if (previous is not null) previous.IsExpanded = false;
                if (value is not null) { value.IsExpanded = true; value.EnsureDetail(); }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedContainment)));
            }
        }
    }

    public bool HasSelectedContainment => _selectedContainment is not null;

    // The containment unit GUIDs point into the Facility save's DeployedObjectMap;
    // the metadata save itself has no deployables. Read once, off-thread, on demand.
    private Task<IReadOnlyList<WorldDeployable>>? _facilityDeployablesTask;

    /// <summary>
    /// The save file that physically holds the containment units this save's links point at:
    /// the sibling <c>WorldSave_Facility.sav</c> for a metadata save, else this save itself.
    /// Null when the Facility save isn't beside the metadata save on disk.
    /// </summary>
    internal string? ContainmentHostPath =>
        IsMetadataSave ? StoryFlagSync.SiblingFacilityPath(_path) : _path;

    internal Task<IReadOnlyList<WorldDeployable>> GetFacilityDeployablesAsync()
        => _facilityDeployablesTask ??= Task.Run<IReadOnlyList<WorldDeployable>>(() =>
        {
            try
            {
                if (!IsMetadataSave) return _data.Deployables;
                var facility = StoryFlagSync.SiblingFacilityPath(_path);
                if (facility is null) return [];
                return WorldSaveReader.ReadFromFile(facility).Deployables;
            }
            catch
            {
                return [];
            }
        });

    private bool AreExtrasDirty()
        => (_hasWorldClock && (Math.Abs(_clockSeconds - _clockSecondsBaseline) > 0.5 || _clockDay != _clockDayBaseline))
           || (_hasDayDiscovered && _dayDiscovered != _dayDiscoveredBaseline)
           || _leyakReleases.Count > 0
           || _stagedWorldUnlocks.Count > 0;

    // ---------- World-wide unlocks (GlobalUnlocks struct, metadata save) ----------
    // The game shares pickups/emails/journal/compendium discovery world-wide, parallel
    // to each player's own lists. Bulk unlocks stage a merged (additive) array per
    // prefix and write on SAVE.

    private const string WuItems = "GlobalItemsPickedUp_";
    private const string WuEmails = "GlobalEmailsRead_";
    private const string WuJournals = "GlobalJournalEntries_";
    private const string WuCompEmail = "GlobalCompendiumEmail_";
    private const string WuCompNarrative = "GlobalCompendiumNarrative_";
    private const string WuCompExploration = "GlobalCompendiumExploration_";

    private readonly Dictionary<string, int> _worldUnlockCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _stagedWorldUnlocks = new(StringComparer.Ordinal);

    /// <summary>The bulk world-unlock section only makes sense on the metadata save.</summary>
    public bool HasWorldUnlocks => IsMetadataSave;

    /// <summary>Formatted <c>LastPlayed</c> timestamp (metadata save; null elsewhere).</summary>
    public string? LastPlayedText { get; }

    public bool HasLastPlayed => LastPlayedText is not null;

    private int WorldUnlockCount(string prefix)
        => _stagedWorldUnlocks.TryGetValue(prefix, out var staged)
            ? staged.Count
            : _worldUnlockCounts.GetValueOrDefault(prefix);

    public string WorldItemsSeenText => $"{WorldUnlockCount(WuItems)} discovered";

    public string WorldEmailsText => $"{WorldUnlockCount(WuEmails)} read";

    public string WorldJournalsText => $"{WorldUnlockCount(WuJournals)} found";

    public string WorldCompendiumText =>
        $"{WorldUnlockCount(WuCompEmail) + WorldUnlockCount(WuCompNarrative) + WorldUnlockCount(WuCompExploration)} section unlocks";

    /// <summary>Adds every catalog item to the world-wide picked-up list (staged).</summary>
    public void UnlockAllWorldItems()
        => StageWorldUnlock(WuItems, (GameDataServices.Catalog?.Entries ?? Enumerable.Empty<Core.Items.ItemCatalogEntry>()).Select(e => e.Id));

    /// <summary>Marks every email read world-wide (staged).</summary>
    public void UnlockAllWorldEmails()
        => StageWorldUnlock(WuEmails, GameDataServices.Emails.Select(e => e.Id));

    /// <summary>Marks every journal objective found world-wide (staged).</summary>
    public void UnlockAllWorldJournals()
        => StageWorldUnlock(WuJournals, GameDataServices.Journals.Select(j => j.Id));

    /// <summary>
    /// Unlocks every compendium section world-wide (staged). Each entry lands in the
    /// array matching its section's unlock type; rows already placed elsewhere by the
    /// game keep their original placement (we never move existing entries).
    /// </summary>
    public void UnlockAllWorldCompendium()
    {
        var compendium = GameDataServices.Compendium;
        StageWorldUnlock(WuCompEmail, compendium
            .Where(c => c.SectionTypes.Any(t => t.Contains("Email", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Id));
        StageWorldUnlock(WuCompNarrative, compendium
            .Where(c => c.SectionTypes.Any(t => t.Contains("Narrative", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Id));
        StageWorldUnlock(WuCompExploration, compendium
            .Where(c => c.SectionTypes.Any(t => t.Contains("Exploration", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Id));
    }

    private void StageWorldUnlock(string prefix, IEnumerable<string> vocab)
    {
        IReadOnlyList<string> current = _stagedWorldUnlocks.TryGetValue(prefix, out var staged)
            ? staged
            : WorldSaveReader.ReadGlobalUnlockArray(_data.Raw, prefix);
        var merged = current.ToList();
        var seen = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var id in vocab)
        {
            if (seen.Add(id)) merged.Add(id);
        }
        if (merged.Count == current.Count && _stagedWorldUnlocks.ContainsKey(prefix)) return;
        _stagedWorldUnlocks[prefix] = merged;
        NotifyWorldUnlockTexts();
        Refresh();
    }

    private void NotifyWorldUnlockTexts()
    {
        foreach (var p in new[]
                 {
                     nameof(WorldItemsSeenText), nameof(WorldEmailsText),
                     nameof(WorldJournalsText), nameof(WorldCompendiumText),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    // ---------- Global recipes (metadata save) ----------

    /// <summary>World-wide recipe browser (search, per-recipe toggles, unlock-all).</summary>
    public RecipeListViewModel GlobalRecipeBrowser { get; }

    // ---------- Dropped items ----------

    private readonly List<DroppedItemViewModel> _droppedItems;
    public IReadOnlyList<DroppedItemViewModel> DroppedItems => _droppedItems;

    private string _droppedFilter = string.Empty;

    public string DroppedFilter
    {
        get => _droppedFilter;
        set
        {
            if (Set(ref _droppedFilter, value ?? string.Empty))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleDroppedItems)));
            }
        }
    }

    /// <summary>Capped at 200 rows - long-lived worlds accumulate 1000+ ground items.</summary>
    public IReadOnlyList<DroppedItemViewModel> VisibleDroppedItems
    {
        get
        {
            IEnumerable<DroppedItemViewModel> q = DroppedItems.Where(d => !d.IsDeleted);
            if (!string.IsNullOrWhiteSpace(_droppedFilter))
            {
                var f = _droppedFilter.Trim();
                q = q.Where(d => d.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
                              || (d.Slot.ItemId?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            return q.Take(200).ToList();
        }
    }

    public int DroppedCount => DroppedItems.Count(d => !d.IsDeleted);
    public int DroppedDeletedCount => DroppedItems.Count(d => d.IsDeleted);
    public string DroppedSummary => DroppedDeletedCount == 0
        ? $"{DroppedCount} item(s) on the ground"
        : $"{DroppedCount} item(s) · {DroppedDeletedCount} marked for deletion";

    public ICommand DeleteVisibleDroppedCommand => _deleteVisibleDroppedCommand ??= new RelayCommand(() =>
    {
        var targets = VisibleDroppedItems.Where(d => !d.IsDeleted).ToList();
        foreach (var d in targets) d.IsDeleted = true;
        Core.Diagnostics.EditorLog.Info("Edit", $"Dropped items marked for deletion: {targets.Count} in {FileName}");
    });
    private RelayCommand? _deleteVisibleDroppedCommand;

    public ICommand RestoreDroppedCommand => _restoreDroppedCommand ??= new RelayCommand(() =>
    {
        var restored = DroppedItems.Count(d => d.IsDeleted);
        foreach (var d in DroppedItems) d.IsDeleted = false;
        Core.Diagnostics.EditorLog.Info("Edit", $"Dropped items restored: {restored} in {FileName}");
    });
    private RelayCommand? _restoreDroppedCommand;

    internal void OnDroppedItemChanged()
    {
        foreach (var n in new[] { nameof(VisibleDroppedItems), nameof(DroppedCount), nameof(DroppedDeletedCount), nameof(DroppedSummary) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        Refresh();
    }

    private bool AreDroppedDirty()
        => DroppedItems.Any(d => d.IsDeleted || d.Slot.IsDirty);

    // ---------- Narrative NPCs ----------

    public IReadOnlyList<WorldNpcViewModel> Npcs { get; }
    public bool HasNpcs => Npcs.Count > 0;

    /// <summary>True when this save carries quest-progression flags (only the main Facility save does).</summary>
    public bool HasQuestFlags => Flags.Count > 0;
    private bool AreNpcsDirty() => Npcs.Any(n => n.IsDirty);

    // ---------- Pets (PetNPC) ----------

    // ObservableCollection (not a plain List) so the pets CollectionView refreshes when a pet
    // is removed by a cross-save transfer or a staged deletion is committed.
    private ObservableCollection<WorldPetViewModel> _pets = new();
    private WorldPetViewModel? _selectedPet;

    public IReadOnlyList<WorldPetViewModel> Pets => _pets;
    public bool HasPets => _pets.Count > 0;
    private bool ArePetsDirty() => _pets.Any(p => p.IsDirty);

    /// <summary>The pet shown in the detail pane (master-detail, like the Fish codex).</summary>
    public WorldPetViewModel? SelectedPet
    {
        get => _selectedPet;
        set
        {
            if (Set(ref _selectedPet, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedPet)));
                value?.EnsureImage();
            }
        }
    }

    public bool HasSelectedPet => _selectedPet is not null;

    // ---------- Send a pet to a player (cross-save) ----------

    private IReadOnlyList<SaveTarget>? _siblingPlayers;
    private SaveTarget? _selectedSiblingPlayer;
    private bool _sendToCompanion = true;
    private string? _petMoveStatus;

    /// <summary>Player saves found next to this world (in a sibling PlayerData/ dir).</summary>
    public IReadOnlyList<SaveTarget> SiblingPlayers => _siblingPlayers ??=
        PetSaveLocator.SiblingPlayerSaves(_path).Select(p => new SaveTarget(p, System.IO.Path.GetFileName(p))).ToList();

    public bool HasSiblingPlayers => SiblingPlayers.Count > 0;

    public SaveTarget? SelectedSiblingPlayer
    {
        get => _selectedSiblingPlayer ??= (SiblingPlayers.Count > 0 ? SiblingPlayers[0] : null);
        set => Set(ref _selectedSiblingPlayer, value);
    }

    /// <summary>True = place in the Companion slot; false = first free hotbar slot.</summary>
    public bool SendToCompanion { get => _sendToCompanion; set => Set(ref _sendToCompanion, value); }

    /// <summary>Last cross-save move result, shown under the button.</summary>
    public string? PetMoveStatus { get => _petMoveStatus; private set => Set(ref _petMoveStatus, value); }

    public ICommand SendPetToPlayerCommand { get; private set; } = null!;

    private async Task SendPetToPlayerAsync()
    {
        var pet = _selectedPet;
        if (pet is null) return;
        if (SelectedSiblingPlayer is not { } target)
        {
            PetMoveStatus = "No player save was found next to this world.";
            return;
        }
        if (IsDirty)
        {
            PetMoveStatus = "Save or revert your other world changes first.";
            return;
        }
        var petLabel = string.IsNullOrWhiteSpace(pet.DisplayName) ? pet.Id : pet.DisplayName;
        PetMoveStatus = $"Sending {petLabel} to {target.Name}...";
        try
        {
            var player = Core.PlayerSaves.PlayerSaveReader.ReadFromFile(target.Path);
            var kind = SendToCompanion ? Core.PlayerSaves.PetSlotKind.Equipment : Core.PlayerSaves.PetSlotKind.Hotbar;
            var result = await Task.Run(() =>
            {
                var r = PetTransfer.WorldToPlayer(_data, pet.Id, player, kind);
                if (r.Ok)
                {
                    // Write the gaining file (player) first, then the world that lost the pet.
                    Core.PlayerSaves.PlayerSaveWriter.WriteToFile(player, target.Path);
                    WorldSaveWriter.WriteToFile(_data, _path);
                }
                return r;
            });
            if (!result.Ok) { PetMoveStatus = result.Message; return; }

            // Cross-save edit: the pet left this world and joined the player save. Log both ends.
            Core.Diagnostics.EditorLog.Info("CrossSave",
                $"Pet '{petLabel}' moved from {System.IO.Path.GetFileName(_path)} to player {target.Name} "
                + $"({System.IO.Path.GetFileName(target.Path)}) as {(SendToCompanion ? "companion" : "hotbar")} item");

            // Removing from the ObservableCollection refreshes the list immediately.
            _pets.Remove(pet);
            if (ReferenceEquals(_selectedPet, pet)) SelectedPet = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPets)));
            PetMoveStatus = $"Sent {petLabel} to {target.Name}. It now lives in that player's save.";
            Refresh();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("CrossSave", $"Pet move failed for '{petLabel}'", ex);
            PetMoveStatus = $"Move failed: {ex.Message}";
        }
    }

    // ---------- Send a container item to a player (cross-save) ----------

    private string? _containerItemMoveStatus;

    /// <summary>Result of the last "send item to a player" action, shown under the container grid.</summary>
    public string? ContainerItemMoveStatus
    {
        get => _containerItemMoveStatus;
        private set => Set(ref _containerItemMoveStatus, value);
    }

    public bool HasContainerItemMoveStatus => !string.IsNullOrEmpty(_containerItemMoveStatus);

    /// <summary>
    /// Moves the item in <paramref name="slot"/> (a world container slot) into a sibling player save's
    /// inventory: it lands in the player's first free backpack slot, else the hotbar. The move is
    /// atomic and immediate, mirroring the pet transfer: it refuses while other world edits are
    /// pending (so the only change is this move), writes the gaining player save first and the world
    /// second (each keeps a <c>.bak</c>), then clears the source slot. When several player saves sit
    /// next to the world the user is asked which one.
    /// </summary>
    public async Task SendContainerItemToPlayerAsync(InventorySlotViewModel? slot)
    {
        if (slot is null || slot.IsEmpty)
        {
            ContainerItemMoveStatus = "Select a slot that has an item in it first.";
            NotifyContainerMoveStatus();
            return;
        }
        if (!HasSiblingPlayers)
        {
            ContainerItemMoveStatus = "No player save was found next to this world.";
            NotifyContainerMoveStatus();
            return;
        }
        if (IsDirty)
        {
            ContainerItemMoveStatus = "Save or revert your other world changes first, then send the item.";
            NotifyContainerMoveStatus();
            return;
        }

        var target = await ResolveTargetPlayerAsync();
        if (target is null) return; // user cancelled the picker

        var itemLabel = string.IsNullOrWhiteSpace(slot.DisplayName) ? (slot.ItemId ?? "item") : slot.DisplayName;
        var item = slot.ToCurrentSlot();
        ContainerItemMoveStatus = $"Sending {itemLabel} to {target.Name}...";
        NotifyContainerMoveStatus();
        try
        {
            var player = await Task.Run(() => Core.PlayerSaves.PlayerSaveReader.ReadFromFile(target.Path));
            var result = Core.PlayerSaves.InventoryGift.GiveToPlayer(player, item);
            if (!result.Ok)
            {
                ContainerItemMoveStatus = result.Message;
                NotifyContainerMoveStatus();
                return;
            }

            // The add succeeded on the player model; now make the move real. Clear the source slot,
            // then write the player (gaining) first and the world (losing) second.
            SlotSwap.ClearToEmpty(slot);
            var snapshot = AllContainers.Select(c => c.ToCurrentContainer()).ToList();
            await Task.Run(() =>
            {
                Core.PlayerSaves.PlayerSaveWriter.WriteToFile(player, target.Path);
                WorldSaveWriter.ApplyContainers(_data, snapshot);
                WorldSaveWriter.WriteToFile(_data, _path);
            });

            // The cleared slot is now the on-disk state, so re-baseline it (no lingering "dirty").
            slot.AcceptCurrentAsBaseline();
            RefreshContainerCounts();
            // Repack the Game Pass container if this is a Game Pass working copy (no-op otherwise).
            Saved?.Invoke();

            Core.Diagnostics.EditorLog.Info("CrossSave",
                $"Item '{itemLabel}' moved from {System.IO.Path.GetFileName(_path)} to player {target.Name} "
                + $"({System.IO.Path.GetFileName(target.Path)}) -> {result.Where}");
            ContainerItemMoveStatus = $"Sent {itemLabel} to {target.Name}'s {result.Where}.";
            NotifyContainerMoveStatus();
            Refresh();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("CrossSave", $"Item move failed for '{itemLabel}'", ex);
            ContainerItemMoveStatus = $"Move failed: {ex.Message}";
            NotifyContainerMoveStatus();
        }
    }

    /// <summary>Picks the destination player: the only sibling, or one the user chooses from a prompt.</summary>
    private async Task<SaveTarget?> ResolveTargetPlayerAsync()
    {
        var players = SiblingPlayers;
        if (players.Count == 1) return players[0];

        // Offer up to four player saves as buttons (plus Cancel); beyond that fall back to the
        // first, which the picker would make unwieldy anyway.
        if (players.Count > 4) return SelectedSiblingPlayer ?? players[0];

        var actions = players
            .Select(p => (p.Name, DialogTone.Primary))
            .Append(("Cancel", DialogTone.Neutral))
            .ToArray();
        var choice = await DialogViewModel.Current.ShowAsync(
            "Send item to which player?",
            "Choose the player save that should receive this item.",
            actions);
        return choice >= 0 && choice < players.Count ? players[choice] : null;
    }

    /// <summary>Re-reads occupied counts for every container after a slot changed under our feet.</summary>
    private void RefreshContainerCounts()
    {
        // Rebuild the visible list so the count text and any "hide empty" filtering reflect the move.
        ApplyFilter();
    }

    private void NotifyContainerMoveStatus()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContainerItemMoveStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasContainerItemMoveStatus)));
    }

    // ---------- Vehicles (VehicleMap) ----------

    private readonly List<WorldVehicleViewModel> _vehicles;
    private WorldVehicleViewModel? _selectedVehicle;

    public IReadOnlyList<WorldVehicleViewModel> Vehicles => _vehicles;
    public bool HasVehicles => _vehicles.Count > 0;
    private bool AreVehiclesDirty() => _vehicles.Any(v => v.IsDirty);

    /// <summary>
    /// The same vehicles, grouped by the sub-world they sit in (a region save can span many
    /// streamed levels and portal pocket-worlds). Drives the grouped list on the VEHICLES tab.
    /// </summary>
    public IReadOnlyList<VehicleWorldGroup> VehicleGroups { get; }

    /// <summary>The vehicle shown in the detail pane (master-detail). Triggers its async loads.</summary>
    public WorldVehicleViewModel? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            if (Set(ref _selectedVehicle, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedVehicle)));
                value?.OnSelected();
            }
        }
    }

    public bool HasSelectedVehicle => _selectedVehicle is not null;

    /// <summary>Jumps to a vehicle's on-board storage in the CONTAINERS tab (filtered to it).</summary>
    private void OpenVehicleInventory(string vehicleId)
    {
        // A vehicle's on-board storage is usually empty; HideEmpty (on by default) would drop
        // the matched container before the keyword filter even runs, so the jump "finds
        // nothing". Turn it off so the targeted container always shows.
        HideEmpty = false;
        Filter = vehicleId;
        ActiveTab = WorldTab.Containers;
    }

    /// <summary>
    /// Jumps to the CONTAINERS tab and surfaces the container with <paramref name="deviceId"/>,
    /// used by the power-socket tab's "open the plugged-in device" link. The device id is the same
    /// GUID a deployed container is keyed by, so filtering by it isolates the right container; we
    /// also select it directly when it is loaded.
    /// </summary>
    private void OpenPoweredDevice(string deviceId)
    {
        if (OpenContainerById(deviceId))
        {
            return;
        }
        // The device is not in this save: hand off to the host to find it across the world's
        // other region saves and switch there (true cross-world navigation).
        if (_crossWorldNavigate is not null)
        {
            _ = _crossWorldNavigate(deviceId);
        }
    }

    /// <summary>
    /// Selects the container with <paramref name="id"/> and shows it on the CONTAINERS tab.
    /// Returns false when no such container is loaded in this save (e.g. it lives in another save).
    /// Used both by the in-save power-socket link and by the host after a cross-world switch.
    /// </summary>
    public bool OpenContainerById(string id)
    {
        var match = AllContainers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }
        Core.Diagnostics.EditorLog.Info("World", $"Open container by id: {match.DisplayName ?? match.Id} (id {match.Id})");
        HideEmpty = false;
        Filter = id;
        SelectedContainer = match;
        ActiveTab = WorldTab.Containers;
        return true;
    }

    private IReadOnlyList<TraderCardViewModel>? _traderCards;

    /// <summary>
    /// The trader roster cards (game stock tables + wiki lore overlay), availability
    /// checked via <see cref="HasWorldFlag"/> so the metadata save reads the sibling
    /// Facility flags. Every DT_NPC_Traders row renders - rows without a lore entry
    /// (future traders the editor doesn't know yet) fall back to their row id; only
    /// known non-traders (Fili, an Anteverse NPC) are hidden.
    /// </summary>
    public IReadOnlyList<TraderCardViewModel> TraderCards
        => _traderCards ??= Services.GameDataServices.Traders
            .Where(t => !TraderLore.NonTraders.Contains(t.Id))
            .OrderBy(t => TraderLore.ById.ContainsKey(t.Id) ? 0 : 1)
            .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TraderCardViewModel(t, HasWorldFlag))
            .ToList();

    /// <summary>
    /// Re-evaluates every trader's availability/concealment against the CURRENT flag set. Trader
    /// gating reads live flags through <see cref="HasWorldFlag"/>, so any flag change (a single
    /// edit, a batch, or the facility flags arriving) must call this or a trader that the player
    /// has now "met" keeps showing its stale classified/locked status. No-op until the cards are
    /// built (lazy), so callers never force the roster into existence.
    /// </summary>
    private void RefreshTraderCards()
    {
        if (_traderCards is null) return;
        foreach (var t in _traderCards) t.RefreshAvailability();
    }

    /// <summary>True when there is at least one trader card to show (always true: a built-in
    /// snapshot backs the roster when the game isn't installed).</summary>
    public bool HasTraderCards => TraderCards.Count > 0;

    /// <summary>
    /// True when the roster is coming from the built-in <see cref="TraderCatalog.Fallback"/>
    /// snapshot rather than the installed game. The trades and their unlock flags are accurate
    /// and editable, but item names and icons need the game - so the tab shows an info note
    /// pointing at Settings &gt; Game Data. Fixed for the session (game data loads once), so it
    /// needs no change notification.
    /// </summary>
    public bool IsTraderDataFromSnapshot
        => TraderCards.Count > 0 && !Services.GameDataServices.IsGameDataLoaded;

    private TraderCardViewModel? _selectedTrader;

    /// <summary>Tap a roster card to expand the full stock + unlock states.</summary>
    public TraderCardViewModel? SelectedTrader
    {
        get => _selectedTrader;
        set
        {
            if (Set(ref _selectedTrader, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedTrader)));
            }
        }
    }

    public bool HasSelectedTrader => _selectedTrader is not null;

    /// <summary>
    /// Writes a chosen set of trader-gating/stock flags. On the metadata save (where
    /// the TRADERS tab lives) the flags belong to the sibling
    /// <c>WorldSave_Facility.sav</c> - written immediately with a .bak, exactly like
    /// the chapter SET sync. On a directly-loaded world save they stage into this
    /// file's own flag list until SAVE. Caller confirms with the user first, since
    /// these are story flags and can advance other content tied to them.
    /// </summary>
    public async Task<(int Added, string Message)> UnlockTraderFlagsAsync(
        TraderCardViewModel trader, IReadOnlyCollection<string> flags)
    {
        if (flags.Count == 0) return (0, $"{trader.Name}: nothing to unlock.");

        int added;
        string message;
        if (IsMetadataSave)
        {
            var path = _path;
            (added, message) = await Task.Run(() => StoryFlagSync.AddFacilityFlags(path, flags));
            if (added > 0)
            {
                // Re-read the facility flags; the async completion refreshes the cards.
                RefreshFacilityFlagState();
            }
        }
        else
        {
            added = 0;
            RunFlagBatch(() =>
            {
                foreach (var f in flags)
                {
                    if (!_activeFlagSet.Contains(f))
                    {
                        Flags.Add(f);
                        added++;
                    }
                }
            });
            if (added > 0)
            {
                foreach (var t in TraderCards) t.RefreshAvailability();
            }
            message = added == 0
                ? "All of those flags are already set in this save."
                : $"Staged {added} world flag(s) - press SAVE to write.";
        }
        Core.Diagnostics.EditorLog.Info("Edit",
            $"Trader unlock: {trader.Name} - {added} flag(s) [{string.Join(", ", flags)}]");
        return (added, message);
    }

    /// <summary>Full unlock: every flag gating the trader and its entire stock.</summary>
    public Task<(int Added, string Message)> UnlockTraderAsync(TraderCardViewModel trader)
        => UnlockTraderFlagsAsync(trader, trader.MissingFlags);

    // ---------- Bases ----------

    private IReadOnlyList<WorldBaseViewModel>? _bases;
    private WorldBaseViewModel? _selectedBase;

    public IReadOnlyList<WorldBaseViewModel> Bases
        => _bases ??= BaseDetector.Detect(_data.Deployables)
            .Select(b => new WorldBaseViewModel(b, _data.Raw, Refresh)).ToList();

    /// <summary>This world's deployables (teleporter-sync bench lookup).</summary>
    internal IReadOnlyList<WorldDeployable> Deployables => _data.Deployables;

    public bool HasBases => _data.Deployables.Count > 0;

    public WorldBaseViewModel? SelectedBase
    {
        get => _selectedBase;
        set
        {
            if (Set(ref _selectedBase, value))
            {
                // Switching bases also leaves any container that was open in the map area.
                SelectedBaseContainer = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedBase)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaseMap)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBaseContainers)));
            }
        }
    }

    public bool HasSelectedBase => _selectedBase is not null;

    private WorldContainerViewModel? _selectedBaseContainer;

    /// <summary>
    /// The base container opened inline in the map area of the BASES tab (null shows
    /// the top-down base map instead). Slots edit in place via the slot sidebar.
    /// </summary>
    public WorldContainerViewModel? SelectedBaseContainer
    {
        get => _selectedBaseContainer;
        set
        {
            if (Set(ref _selectedBaseContainer, value))
            {
                // Container slot icons load lazily; the CONTAINERS tab kicks this on
                // selection and this path must too, or the grid shows empty tiles.
                value?.EnsureIcons();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedBaseContainer)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowBaseMap)));
            }
        }
    }

    public bool HasSelectedBaseContainer => _selectedBaseContainer is not null;

    /// <summary>The map renders only while no base container is opened over it.</summary>
    public bool ShowBaseMap => _selectedBaseContainer is null;

    /// <summary>Drawable for the top-down deployable map (new instance -> redraw).</summary>
    public BaseMapDrawable BaseMap => new(_data.Deployables, _selectedBase?.Source);

    /// <summary>Editable container VMs belonging to the selected base.</summary>
    public IReadOnlyList<WorldContainerViewModel> SelectedBaseContainers
    {
        get
        {
            if (_selectedBase is null) return Array.Empty<WorldContainerViewModel>();
            var ids = new HashSet<string>(_selectedBase.ContainerIds, StringComparer.Ordinal);
            return AllContainers.Where(c => ids.Contains(c.Id)).ToList();
        }
    }

    public ICommand OpenContainerCommand => _openContainerCommand ??= new RelayCommand<WorldContainerViewModel>(c =>
    {
        if (c is null) return;
        Core.Diagnostics.EditorLog.Info("World", $"Open container: {c.DisplayName ?? c.Id} (id {c.Id})");
        HideEmpty = false;
        SelectedContainer = c;
        ActiveTab = WorldTab.Containers;
    });
    private RelayCommand<WorldContainerViewModel>? _openContainerCommand;

    // ---------- Cross-file story-flag sync (metadata saves) ----------

    /// <summary>
    /// When true, <see cref="SetChapterAsync"/> also moves every player's respawn/load-in
    /// point to the target chapter's punch-card terminal (<see cref="PlayerRespawnRevert"/>).
    /// Off by default - relocating a player is more consequential than clearing flags/codex
    /// entries, so it stays opt-in.
    /// </summary>
    public bool MovePlayersOnChapterSet { get; set; }

    /// <summary>
    /// The single SET action: moves the chapter pointer, brings <c>WorldSave_Facility.sav</c>'s
    /// trigger AND granular quest flags in line both directions (<see cref="StoryFlagSync"/>),
    /// and clears codex/email/journal rows the story no longer claims to have reached, both
    /// world-wide and per player (<see cref="CodexRevert"/>) - otherwise skipping or rewinding
    /// leaves the checklist, and the codex, lying. Optionally also relocates every player's
    /// respawn point (<see cref="MovePlayersOnChapterSet"/>).
    /// </summary>
    public async Task SetChapterAsync(string row)
    {
        SelectedStoryRow = row;
        if (!IsMetadataSave) return;
        try
        {
            IsSaving = true;
            SaveStatus = "Setting chapter + syncing Facility trigger flags…";
            var (added, _) = await Task.Run(() => StoryFlagSync.SyncFacilityFlags(_path, row));
            var (cleared, _) = await Task.Run(() => StoryFlagSync.ClearForwardFlags(_path, row));
            RefreshFacilityFlagState();

            var statusExtra = string.Empty;
            var facilityPath = StoryFlagSync.SiblingFacilityPath(_path);
            if (facilityPath is not null)
            {
                var reachedFlags = await Task.Run(() => new HashSet<string>(
                    WorldSaveReader.ReadFromFile(facilityPath).Flags, StringComparer.OrdinalIgnoreCase));

                var globalCleared = 0;
                foreach (var (prefix, kept) in CodexRevert.ClearForwardGlobalUnlocks(_data, reachedFlags))
                {
                    var before = _stagedWorldUnlocks.TryGetValue(prefix, out var staged)
                        ? staged.Count
                        : WorldSaveReader.ReadGlobalUnlockArray(_data.Raw, prefix).Count;
                    _stagedWorldUnlocks[prefix] = kept;
                    globalCleared += before - kept.Count;
                }
                if (globalCleared > 0)
                {
                    NotifyWorldUnlockTexts();
                    statusExtra += $", {globalCleared} world codex unlock(s) cleared";
                }

                var (playersChanged, rowsRemoved, _) = await Task.Run(() => CodexRevert.ClearForwardPlayerUnlocks(_path, reachedFlags));
                if (rowsRemoved > 0)
                {
                    statusExtra += $", {rowsRemoved} player codex row(s) cleared ({playersChanged} save(s))";
                }

                if (MovePlayersOnChapterSet)
                {
                    var (moved, moveMessage) = await Task.Run(() => PlayerRespawnRevert.MoveToChapterTerminal(_path, row));
                    statusExtra += moved > 0 ? $", {moveMessage}" : $" ({moveMessage})";
                }
            }

            SaveStatus = $"Chapter set · {added} trigger flag(s) added, {cleared} cleared in WorldSave_Facility.sav{statusExtra} (backups kept). Press SAVE to write the pointer.";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Chapter flag sync failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            Refresh();
        }
    }

    /// <summary>After a cross-file flag write, re-read the facility flags so the chapter list updates.</summary>
    private void RefreshFacilityFlagState()
    {
        _facilityFlags = null;
        _facilityFlagsRequested = false;
        EnsureFacilityFlags();
        NotifyChapterStates();
    }

    /// <summary>The metadata save's selected world recipe (sidebar detail), or null.</summary>
    public RecipeRowViewModel? SelectedWorldRecipe => GlobalRecipeBrowser.SelectedRecipe;

    public bool HasSelectedWorldRecipe => GlobalRecipeBrowser.HasSelectedRecipe;

    public ObservableCollection<string> Flags { get; }
    public IReadOnlyList<FlagItemViewModel> VisibleFlags { get; private set; } = Array.Empty<FlagItemViewModel>();
    public IReadOnlyList<WorldDoorViewModel> Doors { get; }

    public string FlagFilter
    {
        get => _flagFilter;
        set
        {
            if (Set(ref _flagFilter, value ?? string.Empty)) ApplyFlagFilter();
        }
    }

    public string NewFlagText
    {
        get => _newFlagText;
        set
        {
            if (Set(ref _newFlagText, value ?? string.Empty))
            {
                (AddFlagCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand AddFlagCommand { get; }

    public ICommand RemoveFlagCommand => _removeFlagCommand ??= new RelayCommand<string>(RemoveFlag);
    private RelayCommand<string>? _removeFlagCommand;

    // ---------- linear story timeline ----------

    private IReadOnlyList<StoryFlagItemViewModel>? _storyTimeline;

    /// <summary>
    /// The 37 chapters in order, each toggling its trigger flag in THIS save. Lazy;
    /// rebuilt notifications come from the Flags collection events.
    /// </summary>
    public IReadOnlyList<StoryFlagItemViewModel> StoryTimeline
    {
        get
        {
            if (_storyTimeline is null)
            {
                _storyTimeline = StoryProgressionCatalog.Chapters
                    .Where(c => c.TriggerFlag is not null)
                    .Select((c, i) => new StoryFlagItemViewModel(this, c, i + 1))
                    .ToList();
                Flags.CollectionChanged += (_, __) =>
                {
                    // Batched mutations notify once at the end of RunFlagBatch.
                    if (_suppressFlagListRefresh) return;
                    foreach (var s in _storyTimeline!) s.NotifyChanged();
                };
            }
            return _storyTimeline;
        }
    }

    private bool _showStoryTimeline = true;

    public bool ShowStoryTimeline
    {
        get => _showStoryTimeline;
        set => Set(ref _showStoryTimeline, value);
    }

    public void RemoveFlag(string? flag)
    {
        if (flag is null) return;
        // The Flags.CollectionChanged handler refreshes the visible list + dirty state.
        if (Flags.Remove(flag))
        {
            Core.Diagnostics.EditorLog.Info("Edit", $"World flag REMOVED: {flag}");
        }
    }

    private void AddFlag()
    {
        var f = _newFlagText.Trim();
        if (string.IsNullOrEmpty(f) || Flags.Contains(f, StringComparer.Ordinal)) return;
        Flags.Add(f);
        Core.Diagnostics.EditorLog.Info("Edit", $"World flag ADDED: {f}");
        NewFlagText = string.Empty;
    }

    // Item VMs are cached between filter passes: typing in the filter box only
    // re-filters; the VMs (and their prerequisite counts) rebuild only when the flag
    // set itself changes (or the inactive-catalog toggle flips).
    private List<FlagItemViewModel>? _flagItemCache;
    private readonly HashSet<string> _activeFlagSet;
    private bool _suppressFlagListRefresh;

    /// <summary>
    /// Runs a bulk flag mutation with per-item list rebuilds suppressed, then refreshes
    /// the flag list, dirty state, and story timeline once.
    /// </summary>
    private void RunFlagBatch(Action mutate)
    {
        _suppressFlagListRefresh = true;
        try
        {
            mutate();
        }
        finally
        {
            _suppressFlagListRefresh = false;
        }
        ApplyFlagFilter();
        Refresh();
        // A batch of flag edits (e.g. "set all prerequisites") can change which traders are met.
        RefreshTraderCards();
        if (_storyTimeline is not null)
        {
            foreach (var s in _storyTimeline) s.NotifyChanged();
        }
    }

    private List<FlagItemViewModel> UnfilteredFlagItems()
    {
        if (_flagItemCache is not null) return _flagItemCache;

        // Only the flags actually stored in THIS save - the quest flags this world has
        // reached. (The full story state lives in WorldSave_Facility.sav; region files
        // carry only their own.) Catalog/"missing" flags are no longer listed here.
        var items = new List<FlagItemViewModel>(Flags.Count);
        foreach (var f in Flags) items.Add(new FlagItemViewModel(f, isActive: true));
        return _flagItemCache = items;
    }

    private void ApplyFlagFilter()
    {
        IEnumerable<FlagItemViewModel> q = UnfilteredFlagItems();
        if (!string.IsNullOrWhiteSpace(_flagFilter))
        {
            var f = _flagFilter.Trim();
            q = q.Where(x => x.RawName.Contains(f, StringComparison.OrdinalIgnoreCase)
                          || x.FriendlyName.Contains(f, StringComparison.OrdinalIgnoreCase)
                          || x.Area.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        VisibleFlags = q.OrderByDescending(f => f.IsActive)
                        .ThenBy(f => f.Category)
                        .ThenBy(f => f.FriendlyName, StringComparer.Ordinal)
                        .ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleFlags)));

        // Story-ordered grouping: flags read like the story timeline - Office first,
        // Finale last, anomaly/meta flags at the end.
        VisibleFlagGroups = VisibleFlags
            .GroupBy(f =>
            {
                var chapter = FlagGate.RegionChapterFor(f.RawName);
                var index = chapter is null ? int.MaxValue : StoryProgressionCatalog.IndexOf(chapter.Row);
                return (Index: index, Title: chapter is null ? "OTHER · ANOMALIES & META" : RegionTitleFor(chapter));
            })
            .OrderBy(g => g.Key.Index)
            .Select(g => new FlagGroup(
                g.Key.Title,
                g.OrderByDescending(f => f.IsStoryTrigger)
                 .ThenByDescending(f => f.IsActive)
                 .ThenBy(f => f.FriendlyName, StringComparer.Ordinal)))
            .ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleFlagGroups)));

        // Any flag mutation can change chapter completion/dependency states.
        NotifyChapterStates();
    }

    /// <summary>Flags grouped by the story region they belong to, in story order.</summary>
    public IReadOnlyList<FlagGroup> VisibleFlagGroups { get; private set; } = Array.Empty<FlagGroup>();

    private static string RegionTitleFor(StoryChapter chapter)
    {
        var r = chapter.Row;
        if (r.StartsWith("Office", StringComparison.OrdinalIgnoreCase)) return "01 · OFFICE SECTOR";
        if (r is "Flathill" or "PostFlathill") return "02 · FLATHILL (PORTAL WORLD)";
        if (r.StartsWith("MF", StringComparison.OrdinalIgnoreCase)) return "03 · MANUFACTURING WEST & MINES";
        if (r == "Pens") return "04 · THE PENS";
        if (r is "Labs" or "Containment" or "Helmholtz" or "Tarasque" or "Mycofields" or "PostLabs") return "05 · CASCADE LABORATORIES";
        if (r.StartsWith("Sec", StringComparison.OrdinalIgnoreCase) || r == "EndSecurity") return "06 · SECURITY SECTOR";
        if (r is "ElectricalStation" or "Voussoir" or "EndDam") return "07 · HYDROPLANT & VOUSSOIR";
        if (r is "PowerServices" or "AnteverseC") return "08 · POWER SERVICES";
        if (r.StartsWith("Reactors", StringComparison.OrdinalIgnoreCase) || r is "Shadowgate" or "InqEnd") return "09 · THE REACTORS";
        if (r.StartsWith("Residence", StringComparison.OrdinalIgnoreCase)
            || r is "Fracture" or "Botanical" or "DarkLens" or "SouthIsland") return "10 · RESIDENCE SECTOR";
        if (r == "EndGame") return "11 · FINALE";
        return chapter.Title.ToUpperInvariant();
    }

    /// <summary>
    /// True for WorldSave_Facility.sav - the persistent level's save, where the game
    /// keeps the story/progression flags. Region saves carry only their own handful.
    /// </summary>
    public bool IsFacilitySave =>
        Path.GetFileNameWithoutExtension(_path).Equals("WorldSave_Facility", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The catalog flags that BELONG in this file: everything for the facility save
    /// (story home), only the matching region's flags for a region save. Showing all
    /// 100+ story flags as addable on every region file read as nonsense.
    /// </summary>
    private IReadOnlyList<string> CatalogFlagsForThisFile()
    {
        if (IsFacilitySave || IsMetadataSave) return QuestFlagCatalog.KnownFlags;

        var fileChapter = RegionChapterForFile();
        if (fileChapter is null) return Array.Empty<string>();
        var fileRegion = RegionTitleFor(fileChapter);

        return QuestFlagCatalog.KnownFlags
            .Where(f => FlagGate.RegionChapterFor(f) is { } ch && RegionTitleFor(ch) == fileRegion)
            .ToList();
    }

    /// <summary>Catalog flags relevant to this file (full catalog on the facility save).</summary>
    public int KnownFlagCount => CatalogFlagsForThisFile().Count;

    public ICommand ToggleFlagCommand => _toggleFlagCommand ??= new RelayCommand<FlagItemViewModel>(ToggleFlag);
    private RelayCommand<FlagItemViewModel>? _toggleFlagCommand;

    private void ToggleFlag(FlagItemViewModel? item)
    {
        if (item is null) return;
        if (item.IsActive)
        {
            // Cascade: anything the curated dependency graph says required this flag can no longer
            // legitimately be set either (e.g. clearing a chapter trigger without also clearing its
            // granular quest steps left the game still reading that content as done).
            var toRemove = FlagGate.DependentsOf(new[] { item.RawName }, Flags);
            foreach (var flag in toRemove)
            {
                Flags.Remove(flag);
            }
            Core.Diagnostics.EditorLog.Info("Edit", $"World flag REMOVED: {item.RawName}"
                + (toRemove.Count > 1 ? $" (+{toRemove.Count - 1} dependent flag(s))" : string.Empty));
        }
        else if (!Flags.Contains(item.RawName, StringComparer.Ordinal))
        {
            // Prerequisite gate: enabling a flag whose region/story predecessors aren't
            // set would create out-of-order progression the game never produces.
            var missing = MissingPrerequisitesFor(item.RawName);
            if (missing.Count > 0)
            {
                var message = $"Blocked: \"{item.FriendlyName}\" needs {missing.Count} earlier flag(s) first " +
                    $"({string.Join(", ", missing.Take(3))}{(missing.Count > 3 ? ", …" : "")}). " +
                    "Select the flag for details, or use UNLOCK STORY THROUGH HERE on its chapter.";
                SaveStatus = message;
                // The flags tab doesn't show SaveStatus - surface on the main status bar
                // too, otherwise the blocked toggle looks like a dead button.
                Services.ProgressContext.Notify?.Invoke(message);
                Core.Diagnostics.EditorLog.Info("Edit", $"World flag BLOCKED (prereqs): {item.RawName} needs [{string.Join(", ", missing)}]");
                return;
            }
            Flags.Add(item.RawName);
            Core.Diagnostics.EditorLog.Info("Edit", $"World flag ADDED: {item.RawName}");
        }
        // The Flags.CollectionChanged handler refreshes the visible list + dirty state.
        RefreshSelectedFlagState();
    }

    /// <summary>Prerequisite flags of <paramref name="flag"/> that are not yet set.</summary>
    internal IReadOnlyList<string> MissingPrerequisitesFor(string flag)
        => FlagGate.PrerequisitesFor(flag).Where(p => !HasWorldFlag(p)).ToList();

    // ---------- flag detail (fish-style master/detail in the sidebar) ----------

    private FlagItemViewModel? _selectedFlag;
    private string? _flagCardPath;
    private string? _flagCardRequestedFor;

    /// <summary>Tap a flag row to open its detail in the right sidebar.</summary>
    public FlagItemViewModel? SelectedFlag
    {
        get => _selectedFlag;
        set
        {
            // A sealed (gated, future) flag can't open its detail; tapping it prompts a
            // clearance override instead and the selection snaps back.
            if (value is { IsConcealed: true })
            {
                _ = RevealFlagAsync(value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFlag)));
                return;
            }
            if (Set(ref _selectedFlag, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedFlag)));
                RefreshSelectedFlagState();
                EnsureFlagCard();
            }
        }
    }

    /// <summary>Prompts to reveal a sealed flag; on confirm rebuilds the list so it un-masks.</summary>
    private async Task RevealFlagAsync(FlagItemViewModel flag)
    {
        if (await Services.SpoilerPrompt.RevealAsync("This quest flag", flag.SpoilerKey))
        {
            Core.Diagnostics.EditorLog.Info("Reveal", $"Concealed quest flag revealed: {flag.RawName}");
            ApplyFlagFilter();
        }
    }

    public bool HasSelectedFlag => _selectedFlag is not null;

    /// <summary>Prerequisites of the selected flag with their current ✓/✗ state.</summary>
    public string SelectedFlagPrereqText
    {
        get
        {
            if (_selectedFlag is null) return string.Empty;
            var prereqs = FlagGate.PrerequisitesFor(_selectedFlag.RawName);
            if (prereqs.Count == 0) return "No prerequisites - can be enabled at any time.";
            return string.Join("\n", prereqs.Select(p =>
            {
                var chapter = StoryProgressionCatalog.ChapterForFlag(p);
                var label = chapter is null ? p : $"{chapter.Title} ({p})";
                return $"{(HasWorldFlag(p) ? "✓" : "✗")} {label}";
            }));
        }
    }

    public bool SelectedFlagCanEnable => _selectedFlag is not null
        && (_selectedFlag.IsActive || MissingPrerequisitesFor(_selectedFlag.RawName).Count == 0);

    /// <summary>Region context for the selected flag (chapter that opens its area).</summary>
    public string SelectedFlagRegionText
    {
        get
        {
            if (_selectedFlag is null) return string.Empty;
            var chapter = FlagGate.RegionChapterFor(_selectedFlag.RawName);
            return chapter is null
                ? $"{_selectedFlag.Area} (no fixed story gate - portal anomaly or meta flag)"
                : $"Takes place around: {chapter.Title}";
        }
    }

    /// <summary>The region's chapter card art, extracted lazily.</summary>
    public string? SelectedFlagCardPath
    {
        get => _flagCardPath;
        private set
        {
            if (_flagCardPath == value) return;
            _flagCardPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFlagCardPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedFlagCard)));
        }
    }

    public bool HasSelectedFlagCard => _flagCardPath is not null;

    private void EnsureFlagCard()
    {
        var flag = _selectedFlag?.RawName;
        var art = flag is null ? null : FlagGate.RegionChapterFor(flag)?.CardArt;
        if (art == _flagCardRequestedFor) return;
        _flagCardRequestedFor = art;
        SelectedFlagCardPath = null;
        if (art is null || Services.GameDataServices.Provider is not { } provider) return;

        _ = Task.Run(() =>
        {
            try
            {
                var path = provider.ExtractTextureByGameRef(art);
                if (path is not null && art == _flagCardRequestedFor)
                {
                    MainThread.BeginInvokeOnMainThread(() => SelectedFlagCardPath = path);
                }
            }
            catch
            {
                // Card art is cosmetic.
            }
        });
    }

    private void RefreshSelectedFlagState()
    {
        foreach (var p in new[]
        {
            nameof(SelectedFlagPrereqText), nameof(SelectedFlagCanEnable), nameof(SelectedFlagRegionText),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>Sets every missing prerequisite of the selected flag (additive only).</summary>
    public int EnablePrerequisitesForSelectedFlag()
    {
        if (_selectedFlag is null) return 0;
        var missing = MissingPrerequisitesFor(_selectedFlag.RawName);
        if (missing.Count > 0)
        {
            RunFlagBatch(() =>
            {
                foreach (var f in missing)
                {
                    if (!_activeFlagSet.Contains(f)) Flags.Add(f);
                }
            });
            RefreshSelectedFlagState();
        }
        return missing.Count;
    }

    private bool AreFlagsDirty()
    {
        if (Flags.Count != _flagBaseline.Count) return true;
        foreach (var f in Flags) if (!_flagBaseline.Contains(f)) return true;
        return false;
    }

    private bool AreDoorsDirty() => Doors.Any(d => d.IsDirty);

    // ---------- doors-tab region context (the game ships no per-door artwork) ----------

    // Ordered most-specific-first: the FIRST matching token wins. The bare "facility"
    // entry must stay LAST - the persistent Facility level hosts the Office hub, so it
    // falls back to the Office card rather than showing nothing.
    private static readonly (string Token, string Row)[] FileRegionMap =
    {
        ("office1", "Office"), ("office2", "Office2"), ("office3", "Office3"), ("office4", "Office3"),
        ("labs_adjustment", "Labs"), ("labs_control", "Tarasque"), ("containment", "Containment"),
        ("helmholtz", "Helmholtz"), ("labs", "Labs"),
        ("security", "PostLabs"), ("canaan", "SecSurfaceElevator"),
        ("dam_hydroplant", "ElectricalStation"), ("hydroplant", "ElectricalStation"),
        ("dam_central", "ElectricalStation"), ("dam_lower", "EndDam"), ("dam_waterfall", "EndDam"),
        ("dam", "ElectricalStation"), ("reservoir", "ElectricalStation"), ("voussoir", "Voussoir"),
        ("mfmines", "MFMines"), ("mfmaggot", "MFMines"), ("mines", "MFMines"),
        ("mfwest", "MF"), ("mfhq", "MF"), ("foundry", "MF"), ("manufacturing", "MF"), ("mf", "MF"),
        ("pens", "Pens"), ("parking", "Office"), ("tram", "Office3"),
        ("df_labs", "Reactors1Labs"), ("dflabs", "Reactors1Labs"),
        ("radwaste", "ReactorsAll"), ("df_war", "ReactorsAll"), ("df_overgrowth", "ReactorsAll"),
        ("df_central", "ReactorsAll"), ("darkfusion", "ReactorsEntry"), ("df", "ReactorsEntry"),
        ("reactor", "ReactorsEntry"), ("shadowgate", "Shadowgate"),
        ("botanical", "Botanical"), ("fracture", "Fracture"), ("southisland", "SouthIsland"),
        ("residence", "Residence"),
        ("powerservices", "PowerServices"), ("plant", "PowerServices"),
        ("flathill", "Flathill"), ("fog", "Flathill"),
        ("facility", "Office"),
    };

    private string? _doorsCardPath;
    private bool _doorsCardRequested;

    /// <summary>The loaded region's chapter, inferred from the save file name.</summary>
    private StoryChapter? RegionChapterForFile()
    {
        var name = Path.GetFileNameWithoutExtension(_path).ToLowerInvariant();
        foreach (var (token, row) in FileRegionMap)
        {
            if (name.Contains(token, StringComparison.Ordinal))
            {
                return StoryProgressionCatalog.Find(row);
            }
        }
        return null;
    }

    public string DoorsRegionTitle => RegionChapterForFile() is { } ch
        ? $"DOORS - {ch.Title}"
        : "DOORS";

    // ---------- door detail (click a door -> sidebar card) ----------

    private WorldDoorViewModel? _selectedDoor;
    private string? _doorCardPath;
    private string? _doorCardRequestedFor;

    /// <summary>Tap a door row to open its detail in the right sidebar.</summary>
    public WorldDoorViewModel? SelectedDoor
    {
        get => _selectedDoor;
        set
        {
            if (Set(ref _selectedDoor, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedDoor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDoorRegionText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDoorWikiUrl)));
                EnsureSelectedDoorCard();
                EnsureSelectedDoorLocation();
            }
        }
    }

    public bool HasSelectedDoor => _selectedDoor is not null;

    /// <summary>
    /// Card art for the door's own SUB-LEVEL when its name maps to a story region,
    /// falling back to the loaded file's region card.
    /// </summary>
    public string? SelectedDoorCardPath
    {
        get => _doorCardPath;
        private set
        {
            if (_doorCardPath == value) return;
            _doorCardPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDoorCardPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedDoorCard)));
        }
    }

    public bool HasSelectedDoorCard => _doorCardPath is not null;

    /// <summary>"Region: Cascade Laboratories" - where the door's sub-level sits in the story.</summary>
    public string SelectedDoorRegionText
    {
        get
        {
            var chapter = RegionChapterForToken(_selectedDoor?.MapName) ?? RegionChapterForFile();
            return chapter is null ? string.Empty : $"Region: {chapter.Title}";
        }
    }

    private static StoryChapter? RegionChapterForToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var lower = token.ToLowerInvariant();
        foreach (var (t, row) in FileRegionMap)
        {
            if (lower.Contains(t, StringComparison.Ordinal))
            {
                return StoryProgressionCatalog.Find(row);
            }
        }
        return null;
    }

    private void EnsureSelectedDoorCard()
    {
        var chapter = RegionChapterForToken(_selectedDoor?.MapName) ?? RegionChapterForFile();
        var art = chapter?.CardArt;
        if (art == _doorCardRequestedFor) return;
        _doorCardRequestedFor = art;
        SelectedDoorCardPath = null;
        if (art is null || Services.GameDataServices.Provider is not { } provider) return;

        _ = Task.Run(() =>
        {
            try
            {
                var path = provider.ExtractTextureByGameRef(art);
                if (path is not null && art == _doorCardRequestedFor)
                {
                    MainThread.BeginInvokeOnMainThread(() => SelectedDoorCardPath = path);
                }
            }
            catch
            {
                // Card art is cosmetic.
            }
        });
    }

    // ---------- exact door position (read from the cooked sub-level on demand) ----------

    private string? _doorPositionText;
    private bool _isLocatingDoor;
    private IDrawable? _doorMap;
    private string? _doorLocationRequestedFor;

    /// <summary>"X -12,345 · Y 6,789 · Z 1,200" once the cooked level yields the actor.</summary>
    public string? SelectedDoorPositionText
    {
        get => _doorPositionText;
        private set
        {
            if (_doorPositionText == value) return;
            _doorPositionText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDoorPositionText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedDoorPosition)));
        }
    }

    public bool HasSelectedDoorPosition => _doorPositionText is not null;

    /// <summary>True while the sub-level package is being read (first time per level).</summary>
    public bool IsLocatingDoor
    {
        get => _isLocatingDoor;
        private set => Set(ref _isLocatingDoor, value);
    }

    /// <summary>
    /// The door pinned on the in-game sector map (pamphlet drawing) when one depicts
    /// its sub-level, otherwise a scatter of the level's doors; null until resolved.
    /// </summary>
    public IDrawable? SelectedDoorMap
    {
        get => _doorMap;
        private set
        {
            _doorMap = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDoorMap)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedDoorMap)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoorMapCaption)));
        }
    }

    /// <summary>Explains what the map view shows (sector map pin vs door scatter).</summary>
    public string DoorMapCaption => _doorMap is SectorMapPinDrawable
        ? "Crosshair = this door on the in-game sector map (position from the game's level files; map fit is approximate). Dots = the level's other doors."
        : "Crosshair = this door; dots = the other doors of the same sub-level (top-down, positions from the game's own level files).";

    public bool HasSelectedDoorMap => _doorMap is not null;

    /// <summary>Wiki page for the door's story region, for the LOOK UP button.</summary>
    public string? SelectedDoorWikiUrl
    {
        get
        {
            var chapter = RegionChapterForToken(_selectedDoor?.MapName) ?? RegionChapterForFile();
            return chapter is null
                ? null
                : $"https://abioticfactor.wiki.gg/wiki/{Uri.EscapeDataString(chapter.Title.Replace(' ', '_'))}";
        }
    }

    private void EnsureSelectedDoorLocation()
    {
        var door = _selectedDoor;
        SelectedDoorPositionText = null;
        SelectedDoorMap = null;
        IsLocatingDoor = false;
        if (door is null) return;

        if (Services.GameDataServices.Provider is not { } provider)
        {
            SelectedDoorPositionText = "Game install not found - exact positions unavailable.";
            return;
        }

        var requestKey = door.Id;
        _doorLocationRequestedFor = requestKey;
        IsLocatingDoor = true;

        var mapName = door.MapName;
        var actor = door.ActorName;
        // The save lists every door of this sub-level; their cooked positions become
        // the context dots around the selected one.
        var siblings = Doors
            .Where(d => string.Equals(d.MapName, mapName, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.ActorName)
            .ToList();

        _ = Task.Run(() =>
        {
            var all = DoorLocationResolver.ForMap(provider, mapName);
            var points = siblings
                .Where(all.ContainsKey)
                .Select(a => (Actor: a, Loc: all[a]))
                .ToList();
            var mine = all.TryGetValue(actor, out var found) ? found : null;

            // Prefer the in-game sector map (pamphlet drawing) as the background when
            // one depicts this sub-level: fit the level's actor cloud to the drawing's
            // content rect (SectorMapCalibration) and pin the doors on it.
            SectorMapPinDrawable? pinned = null;
            if (mine is not null
                && SectorMapCatalog.ForLevel(Services.GameDataServices.SectorMaps, mapName) is { } mapInfo)
            {
                try
                {
                    var texturePath = provider.ExtractTextureByGameRef(mapInfo.TexturePath);
                    if (texturePath is not null)
                    {
                        using var bitmap = SkiaSharp.SKBitmap.Decode(texturePath);
                        if (bitmap is not null)
                        {
                            var content = SectorMapCalibration.DetectContentRect(bitmap);
                            var variant = SectorMapCalibration.VariantFor(mapInfo.LevelFileName);
                            var project = SectorMapCalibration.BuildProjector(
                                all.Values.ToList(), content, variant);
                            var contextPins = points.Select(p => project(p.Loc)).ToList();
                            pinned = new SectorMapPinDrawable(
                                texturePath, bitmap.Width, bitmap.Height, contextPins, project(mine));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.EditorLog.Warn(
                        "DoorMap", $"Sector map pin for {mapName} failed: {ex.Message}");
                }
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_doorLocationRequestedFor != requestKey) return;
                IsLocatingDoor = false;
                if (mine is null)
                {
                    SelectedDoorPositionText = all.Count == 0
                        ? "This sub-level could not be read from the game files."
                        : "This door's actor was not found in the cooked level (renamed by a game update?).";
                    return;
                }
                SelectedDoorPositionText = $"X {mine.X:N0}   ·   Y {mine.Y:N0}   ·   Z {mine.Z:N0}";
                SelectedDoorMap = pinned ?? (IDrawable)new DoorMiniMapDrawable(points, actor);
            });
        });
    }

    /// <summary>Sector card art for the doors tab; the game has no per-door images.</summary>
    public string? DoorsRegionCardPath
    {
        get
        {
            EnsureDoorsCard();
            return _doorsCardPath;
        }
    }

    public bool HasDoorsRegionCard => _doorsCardPath is not null;

    private void EnsureDoorsCard()
    {
        if (_doorsCardRequested) return;
        _doorsCardRequested = true;
        var art = RegionChapterForFile()?.CardArt;
        if (art is null || Services.GameDataServices.Provider is not { } provider) return;

        _ = Task.Run(() =>
        {
            try
            {
                var path = provider.ExtractTextureByGameRef(art);
                if (path is null) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _doorsCardPath = path;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoorsRegionCardPath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDoorsRegionCard)));
                });
            }
            catch
            {
                // Card art is cosmetic.
            }
        });
    }

    public IReadOnlyList<WorldContainerViewModel> AllContainers { get; }
    public IReadOnlyList<WorldContainerViewModel> VisibleContainers { get; private set; }
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

    public WorldContainerViewModel? SelectedContainer
    {
        get => _selectedContainer;
        set
        {
            if (_selectedContainer == value) return;
            _selectedContainer = value;
            value?.EnsureIcons();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedContainer)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedContainer)));
        }
    }

    public bool HasSelectedContainer => _selectedContainer is not null;
    public int ContainerCount => AllContainers.Count;
    public int NonEmptyCount => AllContainers.Count(c => c.OccupiedCount > 0);

    // ---------- Tabs ----------

    public WorldTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (Set(ref _activeTab, value))
            {
                foreach (var n in new[] { nameof(IsContainersTab), nameof(IsFlagsTab), nameof(IsDoorsTab), nameof(IsDroppedTab), nameof(IsNpcsTab), nameof(IsPetsTab), nameof(IsVehiclesTab), nameof(IsBasesTab), nameof(IsMetaTab), nameof(IsTradersTab), nameof(IsContainmentTab), nameof(IsFeatureTab), nameof(IsRawTab) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
                RefreshTabs();
            }
        }
    }

    /// <summary>
    /// The full tab strip as one flat, ordered list (fixed tabs + one per applicable feature
    /// map + RAW JSON), filtered to those that apply to this save. The view binds a wrapping
    /// FlexLayout to this so every tab is visible across rows instead of overflowing one strip.
    /// </summary>
    public IReadOnlyList<WorldTabButton> Tabs => _tabs;

    private void BuildTabs()
    {
        void Add(string title, ICommand cmd, Func<bool> active) => _tabs.Add(new WorldTabButton(title, cmd, active));

        if (!IsMetadataSave)
        {
            Add("CONTAINERS", ShowContainersCommand, () => IsContainersTab);
            // Quest flags live only in the main Facility save; every other region save carries
            // none, so the tab is auto-hidden when this save has no flags rather than showing an
            // empty editor. (Story-NPC revival etc. is handled through quest flags here.)
            if (HasQuestFlags) Add("QUEST FLAGS", ShowFlagsCommand, () => IsFlagsTab);
            Add("DOORS", ShowDoorsCommand, () => IsDoorsTab);
        }
        if (HasDroppedItems) Add("DROPPED", ShowDroppedCommand, () => IsDroppedTab);
        // The NPCS tab was removed: it was an unclear, primitive list. Tamed-pet edits live in the
        // PETS tab, and story-NPC state is driven by quest flags.
        if (HasPets) Add("PETS", ShowPetsCommand, () => IsPetsTab);
        if (HasVehicles) Add("VEHICLES", ShowVehiclesCommand, () => IsVehiclesTab);
        if (HasBases) Add("BASES", ShowBasesCommand, () => IsBasesTab);
        if (IsMetadataSave)
        {
            Add("STORY", ShowMetaCommand, () => IsMetaTab);
            Add("TRADERS", ShowTradersCommand, () => IsTradersTab);
            Add("CONTAINMENT", ShowContainmentCommand, () => IsContainmentTab);
        }
        foreach (var f in _featureTabs)
        {
            var feature = f; // capture
            Add(feature.Title, feature.SelectCommand, () => IsFeatureTab && feature.IsActive);
        }
        Add("RAW JSON", ShowRawCommand, () => IsRawTab);
    }

    private void RefreshTabs()
    {
        foreach (var t in _tabs) t.Refresh();
    }

    public bool IsContainersTab => _activeTab == WorldTab.Containers;
    public bool IsFlagsTab => _activeTab == WorldTab.Flags;
    public bool IsDoorsTab => _activeTab == WorldTab.Doors;
    public bool IsDroppedTab => _activeTab == WorldTab.Dropped;
    public bool IsNpcsTab => _activeTab == WorldTab.Npcs;
    public bool IsPetsTab => _activeTab == WorldTab.Pets;
    public bool IsVehiclesTab => _activeTab == WorldTab.Vehicles;
    public bool IsBasesTab => _activeTab == WorldTab.Bases;
    public bool IsRawTab => _activeTab == WorldTab.Raw;
    public bool IsMetaTab => _activeTab == WorldTab.Meta;
    public bool IsTradersTab => _activeTab == WorldTab.Traders;
    public bool IsContainmentTab => _activeTab == WorldTab.Containment;
    public bool IsFeatureTab => _activeTab == WorldTab.Feature;

    public bool HasDroppedItems => _droppedItems.Count > 0;

    // ---------- World-state feature maps (power sockets, resource nodes, NPC spawns, triggers, ...) ----------

    /// <summary>The applicable world-state maps for this save, each its own master-detail tab.</summary>
    public IReadOnlyList<WorldFeatureTabViewModel> FeatureTabs => _featureTabs;

    public bool HasFeatureTabs => _featureTabs.Count > 0;

    /// <summary>The feature tab currently shown (drives the shared <see cref="Views.World.WorldFeatureTab"/> host).</summary>
    public WorldFeatureTabViewModel? SelectedFeatureTab
    {
        get => _selectedFeatureTab;
        private set
        {
            if (Set(ref _selectedFeatureTab, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedFeatureTab)));
            }
        }
    }

    public bool HasSelectedFeatureTab => _selectedFeatureTab is not null;

    private void SelectFeatureTab(WorldFeatureTabViewModel? tab)
    {
        if (tab is null) return;
        foreach (var t in _featureTabs)
        {
            t.IsActive = ReferenceEquals(t, tab);
        }
        SelectedFeatureTab = tab;
        ActiveTab = WorldTab.Feature;
        RefreshTabs(); // feature->feature keeps ActiveTab==Feature, so refresh highlights here too
    }

    private bool AreFeatureMapsDirty() => _featureTabs.Any(t => t.IsDirty);

    // Stored command instances: returning a fresh RelayCommand from each getter would
    // orphan CanExecuteChanged - buttons would never re-enable.
    public ICommand ShowContainersCommand { get; private set; } = null!;
    public ICommand ShowFlagsCommand { get; private set; } = null!;
    public ICommand ShowDoorsCommand { get; private set; } = null!;
    public ICommand ShowDroppedCommand { get; private set; } = null!;
    public ICommand ShowNpcsCommand { get; private set; } = null!;
    public ICommand ShowPetsCommand { get; private set; } = null!;
    public ICommand ShowVehiclesCommand { get; private set; } = null!;
    public ICommand ShowBasesCommand { get; private set; } = null!;
    public ICommand ShowMetaCommand { get; private set; } = null!;
    public ICommand ShowTradersCommand { get; private set; } = null!;
    public ICommand ShowContainmentCommand { get; private set; } = null!;
    public ICommand ShowRawCommand { get; private set; } = null!;

    private void InitTabCommands()
    {
        ShowContainersCommand = new RelayCommand(() => ActiveTab = WorldTab.Containers);
        ShowFlagsCommand = new RelayCommand(() => ActiveTab = WorldTab.Flags);
        ShowDoorsCommand = new RelayCommand(() => ActiveTab = WorldTab.Doors);
        ShowDroppedCommand = new RelayCommand(() => ActiveTab = WorldTab.Dropped);
        ShowNpcsCommand = new RelayCommand(() => ActiveTab = WorldTab.Npcs);
        ShowPetsCommand = new RelayCommand(() => ActiveTab = WorldTab.Pets);
        ShowVehiclesCommand = new RelayCommand(() => ActiveTab = WorldTab.Vehicles);
        SendPetToPlayerCommand = new RelayCommand(async () => await SendPetToPlayerAsync());
        ShowBasesCommand = new RelayCommand(() => ActiveTab = WorldTab.Bases);
        ShowMetaCommand = new RelayCommand(() => ActiveTab = WorldTab.Meta);
        ShowTradersCommand = new RelayCommand(() => ActiveTab = WorldTab.Traders);
        ShowContainmentCommand = new RelayCommand(() => ActiveTab = WorldTab.Containment);
        ShowRawCommand = new RelayCommand(() => ActiveTab = WorldTab.Raw);
        ExportJsonCommand = new RelayCommand(async () => await ExportJsonAsync(), () => !IsSaving);
        ImportJsonCommand = new RelayCommand(async () => await ImportJsonAsync(), () => !IsSaving && JsonFileExists);
    }

    // ---------- Raw JSON (export / edit externally / import) ----------

    private string? _rawStatus;

    public string? RawStatus { get => _rawStatus; private set => Set(ref _rawStatus, value); }
    public string FilePath => _path;

    public ICommand ExportJsonCommand { get; private set; } = null!;
    public ICommand ImportJsonCommand { get; private set; } = null!;

    /// <summary>The sidecar JSON file the export/import workflow uses.</summary>
    public string JsonPath => _path + ".json";

    public bool JsonFileExists => File.Exists(JsonPath);

    public async Task ExportJsonAsync()
    {
        try
        {
            IsSaving = true;
            RawStatus = "Exporting JSON… (the Facility save produces ~100 MB, this can take a moment)";
            await Task.Run(() => SaveJsonBridge.ExportJsonToFile(_data.Raw, JsonPath));
            RawStatus = $"Exported {new FileInfo(JsonPath).Length / 1024.0 / 1024.0:F1} MB → {Path.GetFileName(JsonPath)}";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JsonFileExists)));
            (ImportJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();

            await Launcher.Default.OpenAsync(new OpenFileRequest("Exported save JSON", new ReadOnlyFile(JsonPath)));
        }
        catch (Exception ex)
        {
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
            RawStatus = $"Imported at {DateTime.Now:HH:mm:ss} · reload the file from the sidebar to see the changes.";
        }
        catch (Exception ex)
        {
            RawStatus = $"Import failed (save untouched): {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    // ---------- Door filter ----------

    public string DoorFilter
    {
        get => _doorFilter;
        set
        {
            if (Set(ref _doorFilter, value ?? string.Empty))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleDoors)));
        }
    }

    public IReadOnlyList<WorldDoorViewModel> VisibleDoors
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_doorFilter)) return Doors;
            var f = _doorFilter.Trim();
            return Doors.Where(d =>
                d.FriendlyClass.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.MapName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.ActorName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.LockKind.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyState.Contains(f, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value ?? string.Empty)) ApplyFilter();
        }
    }

    public bool HideEmpty
    {
        get => _hideEmpty;
        set
        {
            if (Set(ref _hideEmpty, value)) ApplyFilter();
        }
    }

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

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }

    public bool IsDirty =>
        AllContainers.SelectMany(c => c.Slots).Any(s => s.IsDirty)
        || AreFlagsDirty()
        || AreDoorsDirty()
        || AreDroppedDirty()
        || AreNpcsDirty()
        || ArePetsDirty()
        || AreVehiclesDirty()
        || AreFeatureMapsDirty()
        || AreBasesDirty()
        || IsMetaDirty()
        || AreExtrasDirty();

    /// <summary>
    /// Names every dirty contributor (mirrors <see cref="IsDirty"/>), logged by the leave-gate so a
    /// spurious "unsaved changes" prompt on a world save is traceable from the log alone - the same
    /// diagnostic the player editor's <c>DescribeDirty</c> provides.
    /// </summary>
    public string DescribeDirty()
    {
        var parts = new List<string>();
        var dirtySlots = AllContainers.SelectMany(c => c.Slots).Count(s => s.IsDirty);
        if (dirtySlots > 0) parts.Add($"{dirtySlots} container slot(s)");
        if (AreFlagsDirty()) parts.Add("flags");
        if (AreDoorsDirty()) parts.Add("doors");
        if (AreDroppedDirty()) parts.Add("dropped items");
        if (AreNpcsDirty()) parts.Add("npcs");
        if (ArePetsDirty()) parts.Add("pets");
        if (AreVehiclesDirty()) parts.Add("vehicles");
        if (AreFeatureMapsDirty())
        {
            parts.Add("feature maps: " + string.Join(", ", _featureTabs.Where(t => t.IsDirty).Select(t => t.DisplayName)));
        }
        if (AreBasesDirty()) parts.Add("bases");
        if (IsMetaDirty()) parts.Add("metadata/story");
        if (AreExtrasDirty()) parts.Add("extras");
        return parts.Count == 0 ? "(nothing)" : string.Join("; ", parts);
    }

    // Bench-upgrade edits are staged on the (lazily built) base view-models; null until the
    // BASES tab is opened, in which case nothing can be dirty yet.
    private bool AreBasesDirty() => _bases?.Any(b => b.IsDirty) ?? false;

    private void ApplyFilter()
    {
        IEnumerable<WorldContainerViewModel> q = AllContainers;
        if (_hideEmpty) q = q.Where(c => c.OccupiedCount > 0);
        if (!string.IsNullOrWhiteSpace(_filter))
        {
            // Match the container itself OR any item stored inside it, so "find which
            // crate holds my keypad hacker" is a one-box search.
            var f = _filter.Trim();
            q = q.Where(c => (c.DisplayName ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)
                          || c.Id.Contains(f, StringComparison.OrdinalIgnoreCase)
                          || c.ContainsItem(f));
        }
        VisibleContainers = q.ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleContainers)));
    }

    public async Task SaveAsync()
    {
        if (!IsDirty || IsSaving) return;

        IsSaving = true;
        SaveStatus = "Saving…";
        try
        {
            var snapshot = AllContainers.Select(c => c.ToCurrentContainer()).ToList();
            var flagSnapshot = Flags.ToList();
            var doorSnapshot = Doors.Select(d => d.ToCurrentDoor()).ToList();
            var storySnapshot = _storyRow;
            var minutesSnapshot = _minutes;
            var globalRecipesSnapshot = GlobalRecipeBrowser.CurrentList();
            var globalRecipesDirty = GlobalRecipeBrowser.IsDirty;
            var isMeta = IsMetadataSave;
            var droppedUpdates = _droppedItems.Where(d => !d.IsDeleted && d.Slot.IsDirty).Select(d => d.ToCurrent()).ToList();
            var droppedDeletions = _droppedItems.Where(d => d.IsDeleted).Select(d => d.Id).ToList();
            var npcUpdates = Npcs.Where(n => n.IsDirty).Select(n => n.ToCurrent()).ToList();
            var petUpdates = _pets.Where(p => !p.IsDeleted && p.IsDirty).Select(p => p.ToCurrent()).ToList();
            var petDeletions = _pets.Where(p => p.IsDeleted).Select(p => p.Id).ToList();
            var vehicleUpdates = _vehicles.Where(v => v.IsDirty).Select(v => v.ToCurrent()).ToList();
            var clockDirty = _hasWorldClock
                && (Math.Abs(_clockSeconds - _clockSecondsBaseline) > 0.5 || _clockDay != _clockDayBaseline);
            var clockSeconds = _clockSeconds;
            var clockDay = _clockDay;
            var dayDiscoveredDirty = _hasDayDiscovered && _dayDiscovered != _dayDiscoveredBaseline;
            var dayDiscovered = _dayDiscovered;
            var leyakReleases = _leyakReleases.ToList();
            var worldUnlockSnapshot = _stagedWorldUnlocks.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal);
            await Task.Run(() =>
            {
                WorldSaveWriter.ApplyContainers(_data, snapshot);
                WorldSaveWriter.ApplyFlags(_data, flagSnapshot);
                WorldSaveWriter.ApplyDoors(_data, doorSnapshot);
                if (droppedUpdates.Count > 0)
                {
                    WorldSaveWriter.ApplyDroppedItems(_data, droppedUpdates);
                }
                if (droppedDeletions.Count > 0)
                {
                    WorldSaveWriter.RemoveDroppedItems(_data, droppedDeletions);
                }
                if (npcUpdates.Count > 0)
                {
                    WorldSaveWriter.ApplyNpcs(_data, npcUpdates);
                }
                if (petUpdates.Count > 0)
                {
                    WorldSaveWriter.ApplyPets(_data, petUpdates);
                }
                foreach (var petId in petDeletions)
                {
                    WorldSaveWriter.RemovePet(_data, petId);
                }
                if (vehicleUpdates.Count > 0)
                {
                    WorldSaveWriter.ApplyVehicles(_data, vehicleUpdates);
                }
                if (isMeta && !string.IsNullOrEmpty(storySnapshot))
                {
                    WorldSaveWriter.ApplyStoryProgression(_data, storySnapshot);
                    WorldSaveWriter.ApplyMinutesPassed(_data, minutesSnapshot);
                }
                if (isMeta && globalRecipesDirty)
                {
                    WorldSaveWriter.ApplyGlobalRecipes(_data, globalRecipesSnapshot);
                }
                if (clockDirty)
                {
                    WorldSaveWriter.ApplyWorldClock(_data, clockSeconds, clockDay);
                }
                if (dayDiscoveredDirty)
                {
                    WorldSaveWriter.ApplyDayDiscovered(_data, dayDiscovered);
                }
                foreach (var creature in leyakReleases)
                {
                    WorldSaveWriter.RemoveLeyakContainment(_data, creature);
                }
                foreach (var (prefix, values) in worldUnlockSnapshot)
                {
                    WorldSaveWriter.ApplyGlobalUnlockArray(_data, prefix, values);
                }
                // Feature-map field edits already patched the raw tree; staged removals are
                // applied here (just before the write) so a reverted delete never touches disk.
                foreach (var t in _featureTabs) t.ApplyPendingDeletions();
                WorldSaveWriter.WriteToFile(_data, _path);
            });
            foreach (var s in AllContainers.SelectMany(c => c.Slots)) s.AcceptCurrentAsBaseline();
            _flagBaseline.Clear();
            foreach (var f in Flags) _flagBaseline.Add(f);
            foreach (var d in Doors) d.AcceptBaseline();
            _storyRowBaseline = _storyRow;
            _minutesBaseline = _minutes;
            _clockSecondsBaseline = _clockSeconds;
            _clockDayBaseline = _clockDay;
            _dayDiscoveredBaseline = _dayDiscovered;
            _leyakReleases.Clear();
            foreach (var (prefix, values) in _stagedWorldUnlocks)
            {
                _worldUnlockCounts[prefix] = values.Count;
            }
            _stagedWorldUnlocks.Clear();
            NotifyWorldUnlockTexts();
            GlobalRecipeBrowser.AcceptCurrentAsBaseline();
            if (droppedDeletions.Count > 0)
            {
                _droppedItems.RemoveAll(d => d.IsDeleted);
            }
            foreach (var d in _droppedItems) d.Slot.AcceptCurrentAsBaseline();
            OnDroppedItemChanged();
            foreach (var n in Npcs) n.AcceptBaseline();
            if (petDeletions.Count > 0)
            {
                if (_selectedPet is not null && _selectedPet.IsDeleted) SelectedPet = null;
                foreach (var dead in _pets.Where(p => p.IsDeleted).ToList()) _pets.Remove(dead);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pets)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPets)));
            }
            foreach (var p in _pets) p.AcceptBaseline();
            foreach (var v in _vehicles) v.AcceptBaseline();
            if (_bases is not null) foreach (var b in _bases) b.AcceptBaseline();
            // Feature-map edits patch the raw tree directly, so WriteToFile already persisted
            // them; just adopt the new clean baseline for dirty tracking.
            foreach (var t in _featureTabs) t.AcceptBaseline();
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

    /// <summary>Raised after a successful save. The host uses it to pack a Game Pass working copy
    /// back into its container.</summary>
    public event Action? Saved;

    public void Revert()
    {
        foreach (var s in AllContainers.SelectMany(c => c.Slots)) s.Revert();
        // Flags: rebuild from baseline (batched - one list refresh, not one per flag).
        RunFlagBatch(() =>
        {
            Flags.Clear();
            foreach (var f in _flagBaseline) Flags.Add(f);
        });
        foreach (var d in Doors) d.Revert();
        SelectedStoryRow = _storyRowBaseline;
        MinutesPassed = _minutesBaseline;
        WorldTimeSeconds = _clockSecondsBaseline;
        WorldDay = _clockDayBaseline;
        DayDiscovered = _dayDiscoveredBaseline;
        _stagedWorldUnlocks.Clear();
        NotifyWorldUnlockTexts();
        if (_leyakReleases.Count > 0)
        {
            // The raw tree is untouched until SAVE - rebuild the list from it.
            _leyakReleases.Clear();
            SelectedContainment = null;
            LeyakContainments.Clear();
            foreach (var k in WorldSaveReader.ReadLeyakContainments(_data.Raw))
            {
                LeyakContainments.Add(new LeyakContainmentViewModel(k.Key, k.Value, this));
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLeyakContainments)));
        }
        GlobalRecipeBrowser.Revert();
        foreach (var d in _droppedItems)
        {
            d.IsDeleted = false;
            d.Slot.Revert();
        }
        OnDroppedItemChanged();
        foreach (var n in Npcs) n.Revert();
        foreach (var p in _pets) p.Revert();
        foreach (var v in _vehicles) v.Revert();
        foreach (var t in _featureTabs) t.Revert();
        if (_bases is not null) foreach (var b in _bases) b.Revert();
        SaveStatus = null;
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InventorySlotViewModel.IsDirty))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RevertCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public enum WorldTab { Containers, Flags, Doors, Dropped, Npcs, Pets, Vehicles, Bases, Meta, Traders, Containment, Feature, Raw }

/// <summary>
/// One entry of the metadata save's <c>LeyakContainmentIDs</c> map: a contained
/// entity + the GUID of the containment unit holding it. The detail view resolves
/// what the creature looks like (compendium texture) and where in the facility the
/// unit physically sits (nearest respawn-terminal sector to the unit's deployable).
/// </summary>
public sealed class LeyakContainmentViewModel : INotifyPropertyChanged
{
    private readonly WorldEditorViewModel _owner;
    private bool _detailRequested;
    private string? _imagePath;
    private string _regionText = "Locating the containment unit…";

    public LeyakContainmentViewModel(string creature, string containmentId, WorldEditorViewModel owner)
    {
        Creature = creature;
        ContainmentId = containmentId;
        _owner = owner;
        ReleaseCommand = new RelayCommand(() => _owner.ReleaseLeyak(this));
    }

    /// <summary>Creature row name as the game stores it (Leyak, Krasue, …).</summary>
    public string Creature { get; }

    /// <summary>The containment unit's DeployedObjectMap GUID (lives in the Facility save).</summary>
    public string ContainmentId { get; }

    public System.Windows.Input.ICommand ReleaseCommand { get; }

    /// <summary>A friendlier label for known creatures, falling back to the raw row.</summary>
    public string DisplayName => ContainmentCreatureCatalog.DisplayName(Creature);

    /// <summary>Short flavour blurb shown in the detail card.</summary>
    public string Lore => ContainmentCreatureCatalog.Lore(Creature);

    /// <summary>The Anteverse creatures whose names are themselves a spoiler. Blacked out in the
    /// detail card until the entry is revealed, so a player who hasn't met them in the story doesn't
    /// learn the names by browsing. Krasue's blurb also mentions Leyak, so both are always masked.</summary>
    private static readonly string[] SpoilerCreatureNames = { "Leyak", "Krasue", "Krassue" };

    /// <summary>Detail-card title with the creature name blacked out while the entry is sealed.</summary>
    public string ShownDisplayName =>
        Services.SpoilerService.RedactKeywords(DisplayName, IsConcealed, SpoilerCreatureNames);

    /// <summary>Detail-card blurb with the creature names blacked out while the entry is sealed.</summary>
    public string ShownLore =>
        Services.SpoilerService.RedactKeywords(Lore, IsConcealed, SpoilerCreatureNames);

    // ---------- spoiler concealment ----------

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => Services.SpoilerService.Key(Services.SpoilerService.Containment, Creature);

    /// <summary>
    /// A contained anomaly is a late-game spoiler in its own right (which creatures exist,
    /// what they look like, where they're held), so any entry is a candidate while
    /// protection is on and this one hasn't been revealed.
    /// </summary>
    public bool IsConcealed => Services.SpoilerService.ShouldConceal(SpoilerKey, true);

    /// <summary>Masked creature name for the row while sealed.</summary>
    public string ShownName => Services.SpoilerService.Mask(DisplayName, IsConcealed, "▓ CLASSIFIED ANOMALY");

    /// <summary>Row sub-line: the tap hint flips to a clearance prompt while sealed.</summary>
    public string ShownTapHint => IsConcealed
        ? "Above clearance - tap to reveal"
        : "Tap for appearance + location";

    /// <summary>Prompts to override clearance; on confirm reveals this entry permanently.</summary>
    public async Task RevealAsync()
    {
        if (!IsConcealed) return;
        if (await Services.SpoilerPrompt.RevealAsync("This containment record", SpoilerKey))
        {
            Notify(nameof(IsConcealed));
            Notify(nameof(ShownName));
            Notify(nameof(ShownDisplayName));
            Notify(nameof(ShownLore));
            Notify(nameof(ShownTapHint));
        }
    }

    /// <summary>Extracted compendium portrait for the creature, or null while loading.</summary>
    public string? ImagePath
    {
        get => _imagePath;
        private set { if (_imagePath != value) { _imagePath = value; Notify(nameof(ImagePath)); Notify(nameof(HasImage)); } }
    }

    public bool HasImage => _imagePath is not null;

    private bool _imageIsWiki;

    /// <summary>True when the shown portrait is bundled fan-wiki art (needs attribution).</summary>
    public bool ImageIsWiki
    {
        get => _imageIsWiki;
        private set { if (_imageIsWiki != value) { _imageIsWiki = value; Notify(nameof(ImageIsWiki)); } }
    }

    /// <summary>The facility sector the unit sits in, e.g. "Power Services", or a status line.</summary>
    public string RegionText
    {
        get => _regionText;
        private set { if (_regionText != value) { _regionText = value; Notify(nameof(RegionText)); } }
    }

    private string? _coordsText;

    /// <summary>Raw world position of the unit (numeric, shown in the LCD readout), or null.</summary>
    public string? CoordsText
    {
        get => _coordsText;
        private set { if (_coordsText != value) { _coordsText = value; Notify(nameof(CoordsText)); Notify(nameof(HasCoords)); } }
    }

    public bool HasCoords => _coordsText is not null;

    private string? _containingSaveText;

    /// <summary>
    /// Which save file actually holds the unit (the Facility region save), and whether that
    /// save is currently open in the sidebar. Null until the location resolves.
    /// </summary>
    public string? ContainingSaveText
    {
        get => _containingSaveText;
        private set { if (_containingSaveText != value) { _containingSaveText = value; Notify(nameof(ContainingSaveText)); Notify(nameof(HasContainingSave)); } }
    }

    public bool HasContainingSave => _containingSaveText is not null;

    private bool _isExpanded;

    /// <summary>
    /// True while this row's inline detail (image + location) is shown. The owner keeps it
    /// exclusive so only the tapped row is expanded at a time.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; Notify(nameof(IsExpanded)); } }
    }

    /// <summary>Loads the creature image + containment region once, off-thread.</summary>
    public void EnsureDetail()
    {
        if (_detailRequested) return;
        _detailRequested = true;
        LoadImage();
        LoadRegion();
    }

    private void LoadImage()
    {
        // Prefer the bundled fan-wiki render for known creatures: it is consistent, always
        // present (no game install needed), and for the Krasue it is the only real portrait
        // there is. Anything we don't bundle falls back to the creature's own in-pak art.
        if (BundledImage(Creature) is { } bundled)
        {
            ImagePath = bundled;
            ImageIsWiki = true;   // CC BY-NC-SA: surface requires the attribution line.
            return;
        }

        var provider = Services.GameDataServices.Provider;
        if (provider is null) return;
        var refs = ContainmentCreatureCatalog.TextureRefs(Creature);
        _ = Task.Run(() =>
        {
            // The creature's OWN compendium art only - never another creature's as a stand-in.
            foreach (var c in refs)
            {
                try
                {
                    var path = provider.ExtractTextureByGameRef(c);
                    if (path is not null)
                    {
                        MainThread.BeginInvokeOnMainThread(() => ImagePath = path);
                        return;
                    }
                }
                catch
                {
                    // Portraits are cosmetic.
                }
            }
        });
    }

    /// <summary>
    /// App-bundled fan-wiki portrait for a known creature (<c>Resources/Images/*.png</c>),
    /// or null for one we don't bundle. The Krasue has no in-pak compendium art at all; the
    /// Leyak uses its wiki render too so the two read consistently. Wiki source:
    /// abioticfactor.wiki.gg.
    /// </summary>
    private static string? BundledImage(string creature)
    {
        if (creature.StartsWith("Krasue", StringComparison.OrdinalIgnoreCase)) return "krasue.png";
        if (creature.StartsWith("Leyak", StringComparison.OrdinalIgnoreCase)) return "leyak.png";
        return null;
    }

    private async void LoadRegion()
    {
        try
        {
            var deployables = await _owner.GetFacilityDeployablesAsync();
            var unit = deployables.FirstOrDefault(d =>
                string.Equals(d.Id, ContainmentId, StringComparison.OrdinalIgnoreCase));
            if (unit is null)
            {
                RegionText = "Containment unit not found in WorldSave_Facility.sav (it may have been picked up).";
                return;
            }
            var terminal = Core.PlayerSaves.RespawnTerminalCatalog.NearestTo(unit.X, unit.Y, unit.Z);
            RegionText = terminal.LocationName;
            CoordsText = $"X {unit.X:0}   Y {unit.Y:0}   Z {unit.Z:0}";
            ContainingSaveText = BuildContainingSaveText();
        }
        catch
        {
            RegionText = "Could not resolve the containment unit's location.";
        }
    }

    /// <summary>
    /// Names the Facility region save that holds the unit and flags whether it is open in
    /// the sidebar, so the user knows which file to edit (or load) to reach the unit itself.
    /// </summary>
    private string BuildContainingSaveText()
    {
        var path = _owner.ContainmentHostPath;
        if (path is null) return "Held in the Facility region save (WorldSave_Facility.sav).";

        var file = System.IO.Path.GetFileName(path);
        var loaded = AbioticEditor.App.App.SharedViewModel.Saves
            .Any(s => string.Equals(s.FullPath, path, StringComparison.OrdinalIgnoreCase));
        return loaded
            ? $"Held in {file}, which is open in the sidebar."
            : $"Held in {file} (not currently open in the sidebar).";
    }

    private void Notify(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One story-ordered group of quest flags (grouped CollectionView source).</summary>
public sealed class FlagGroup : List<FlagItemViewModel>
{
    public FlagGroup(string title, IEnumerable<FlagItemViewModel> items) : base(items)
    {
        Title = title;
    }

    public string Title { get; }
}

/// <summary>
/// One button in the world editor's wrapping tab strip. <see cref="IsActive"/> is recomputed
/// (via <see cref="Refresh"/>) whenever the active tab changes, so the highlight tracks
/// selection without each tab needing its own bound bool on the editor VM.
/// </summary>
public sealed class WorldTabButton : INotifyPropertyChanged
{
    private readonly Func<bool> _isActive;

    public WorldTabButton(string title, ICommand selectCommand, Func<bool> isActive)
    {
        Title = title;
        SelectCommand = selectCommand;
        _isActive = isActive;
    }

    public string Title { get; }
    public ICommand SelectCommand { get; }
    public bool IsActive => _isActive();

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Vehicles that share one world / sub-level (grouped CollectionView source).</summary>
public sealed class VehicleWorldGroup : List<WorldVehicleViewModel>
{
    public VehicleWorldGroup(string world, IEnumerable<WorldVehicleViewModel> items) : base(items)
    {
        World = world;
    }

    public string World { get; }

    /// <summary>Header count suffix, e.g. "3 vehicles".</summary>
    public string CountText => Count == 1 ? "1 vehicle" : $"{Count} vehicles";
}
