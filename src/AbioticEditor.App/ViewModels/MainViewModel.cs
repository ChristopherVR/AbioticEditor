using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

using AbioticEditor.Core.Steam;

namespace AbioticEditor.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string PlayerSaveClass = "/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C";
    private const string WorldSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C";
    private const string WorldMetaSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C";

    private string? _folderPath;
    private SaveFileSummary? _selectedSave;
    private bool _isScanning;
    private string? _statusMessage;
    private string? _logoPath;
    private PlayerEditorViewModel? _playerEditor;
    private WorldEditorViewModel? _worldEditor;

    public MainViewModel()
    {
        // Restore the diagnostic-logging switch before anything else runs so early
        // operations (folder restore, asset mount) land in the log when enabled.
        _diagnosticLoggingEnabled = Preferences.Default.Get(DiagnosticLoggingPreferenceKey, false);
        EditorLog.Enabled = _diagnosticLoggingEnabled;
        if (_diagnosticLoggingEnabled)
        {
            EditorLog.Info("App", "Editor started (diagnostic logging restored from preferences).");
        }

        // Progress-gate messages (blocked unlocks) land on the status line.
        Services.ProgressContext.Notify = msg => MainThread.BeginInvokeOnMainThread(() => StatusMessage = msg);
    }

    /// <summary>
    /// One-time startup guard: the host page reruns its constructor on a theme rebuild,
    /// but folder restore / world discovery should only happen on the very first load
    /// (re-running would reset the user's current selection).
    /// </summary>
    public bool HasStartedUp { get; set; }

    public ObservableCollection<SaveFileSummary> Saves { get; } = new();

    /// <summary>
    /// The sidebar's grouped view of <see cref="Saves"/>: world story (metadata) first,
    /// then players, then the world region files - so the three very different kinds of
    /// save stop reading as one undifferentiated list.
    /// </summary>
    public ObservableCollection<SaveGroup> SaveGroups { get; } = new();

    private void RebuildSaveGroups()
    {
        SaveGroups.Clear();
        void AddGroup(string title, Func<SaveFileSummary, bool> match, bool isPlayers = false)
        {
            var items = Saves.Where(match).Where(MatchesFilter).ToList();
            // Once a folder is loaded the PLAYERS group always shows (even when filtered to
            // empty, or when the world has no players yet) so its "+" stays reachable; other
            // groups - and PLAYERS before any folder is open - collapse when nothing matches.
            if (items.Count > 0 || (isPlayers && FolderPath is not null))
            {
                SaveGroups.Add(new SaveGroup(title, items) { IsPlayers = isPlayers });
            }
        }

        AddGroup("WORLD STORY · METADATA", s => s.KindLabel == "META");
        AddGroup("PLAYERS", s => s.KindLabel == "PLAYER", isPlayers: true);
        AddGroup("WORLD REGIONS", s => s.KindLabel == "WORLD");
        AddGroup("OTHER / ERRORS", s => s.KindLabel is not ("META" or "PLAYER" or "WORLD"));
    }

    private string _sidebarFilter = string.Empty;

    /// <summary>
    /// Free-text filter applied across the whole sidebar (save rows + config files). Matches
    /// the display name, owner name, kind chip and file name, case-insensitively.
    /// </summary>
    public string SidebarFilter
    {
        get => _sidebarFilter;
        set
        {
            if (!Set(ref _sidebarFilter, value ?? string.Empty)) return;
            RebuildSaveGroups();
            RebuildConfigFilter();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSidebarFilter)));
        }
    }

    /// <summary>True when a non-empty filter is active (drives the "clear" affordance).</summary>
    public bool HasSidebarFilter => !string.IsNullOrWhiteSpace(_sidebarFilter);

    private RelayCommand? _clearSidebarFilterCommand;

    /// <summary>Clears the sidebar filter (the "×" inside the search box).</summary>
    public System.Windows.Input.ICommand ClearSidebarFilterCommand =>
        _clearSidebarFilterCommand ??= new RelayCommand(() => SidebarFilter = string.Empty);

    private bool MatchesFilter(SaveFileSummary s)
    {
        if (string.IsNullOrWhiteSpace(_sidebarFilter)) return true;
        var f = _sidebarFilter.Trim();
        return Has(s.DisplayName) || Has(s.PlayerName) || Has(s.KindLabel) || Has(Path.GetFileName(s.FullPath));

        bool Has(string? value) => value?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private bool MatchesFilter(ConfigFileOption c)
    {
        if (string.IsNullOrWhiteSpace(_sidebarFilter)) return true;
        var f = _sidebarFilter.Trim();
        return c.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || c.KindChip.Contains(f, StringComparison.OrdinalIgnoreCase)
            || c.Detail.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- add player ("+" next to the PLAYERS group) ----------

    private RelayCommand? _addPlayerCommand;

    /// <summary>
    /// Bound to the "+" beside the PLAYERS header. Asks whether to start a fresh blank
    /// player or copy the currently selected one, prompts for a SteamID64, writes a new
    /// <c>Player_&lt;id&gt;.sav</c> into the world's PlayerData folder and selects it.
    /// </summary>
    public System.Windows.Input.ICommand AddPlayerCommand =>
        _addPlayerCommand ??= new RelayCommand(async () => await AddPlayerAsync());

    private static byte[]? _blankPlayerTemplate;

    private async Task AddPlayerAsync()
    {
        var playerDir = ResolvePlayerDataDir();
        if (playerDir is null)
        {
            await DialogViewModel.Current.AlertAsync("Add player",
                "Open a world's save folder first so the editor knows where to create the new player file.");
            return;
        }

        var selectedPlayer = _selectedSave is { KindLabel: "PLAYER" } sp ? sp : null;
        var anyPlayers = Saves.Any(s => s.KindLabel == "PLAYER");

        // Ask: new blank vs copy the selected player.
        int choice;
        if (selectedPlayer is not null)
        {
            choice = await DialogViewModel.Current.ShowAsync(
                "Add a player",
                $"Build a new player save in this world.\n\nStart fresh, or copy “{selectedPlayer.DisplayName}” " +
                "(keeps their inventory, skills and progress)?",
                ("Cancel", DialogTone.Neutral),
                ("New blank player", DialogTone.Primary),
                ("Copy selected", DialogTone.Primary));
        }
        else
        {
            choice = await DialogViewModel.Current.ShowAsync(
                "Add a player",
                anyPlayers
                    ? "Build a new blank player save in this world.\n\nTip: to copy an existing player instead, " +
                      "select them in the list first, then press +."
                    : "Build a new blank player save in this world.",
                ("Cancel", DialogTone.Neutral),
                ("New blank player", DialogTone.Primary));
        }
        if (choice <= 0) return; // Cancel (0) or scrim-dismissed (-1).
        var copySelected = choice == 2;

        var steamId = await PromptForNewSteamIdAsync(playerDir);
        if (steamId is null) return;

        try
        {
            string newPath;
            if (copySelected && selectedPlayer is not null)
            {
                var sourcePath = selectedPlayer.FullPath;
                newPath = await Task.Run(() =>
                    Core.PlayerSaves.PlayerSaveIdentity.CloneToNewId(sourcePath, steamId.Value));
            }
            else
            {
                var template = await LoadBlankPlayerTemplateAsync();
                Directory.CreateDirectory(playerDir);
                newPath = await Task.Run(() =>
                    Core.PlayerSaves.PlayerSaveFactory.CreateFromTemplate(template, playerDir, steamId.Value));
            }

            EditorLog.Info("App", $"Created player {Path.GetFileName(newPath)} ({(copySelected ? "copied" : "blank")}).");

            if (FolderPath is { } folder)
            {
                await LoadFolderAsync(folder);
                SelectedSave = Saves.FirstOrDefault(s =>
                    string.Equals(s.FullPath, newPath, StringComparison.OrdinalIgnoreCase));
            }
            StatusMessage = copySelected
                ? $"Copied player to {Path.GetFileName(newPath)} - set their SteamID to {steamId}."
                : $"Created blank player {Path.GetFileName(newPath)} (SteamID {steamId}).";
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", "Add player failed", ex);
            await DialogViewModel.Current.AlertAsync("Couldn't add the player", ex.Message);
        }
    }

    /// <summary>
    /// Finds the PlayerData folder for the loaded world: the directory of any existing
    /// player save, else a <c>PlayerData</c> subfolder of (or the loaded folder itself when
    /// it already is) PlayerData. Null when no folder is loaded.
    /// </summary>
    private string? ResolvePlayerDataDir()
    {
        var anyPlayer = Saves.FirstOrDefault(s => s.KindLabel == "PLAYER");
        if (anyPlayer is not null)
        {
            return Path.GetDirectoryName(anyPlayer.FullPath);
        }

        if (FolderPath is not { } folder) return null;
        if (string.Equals(Path.GetFileName(folder), "PlayerData", StringComparison.OrdinalIgnoreCase))
        {
            return folder;
        }
        return Path.Combine(folder, "PlayerData");
    }

    /// <summary>
    /// Prompts for a 17-digit SteamID64, re-prompting until it's valid and not already used
    /// in <paramref name="playerDir"/>. Returns null when the user cancels.
    /// </summary>
    private static async Task<ulong?> PromptForNewSteamIdAsync(string playerDir)
    {
        var initial = "76561198";
        while (true)
        {
            var text = await DialogViewModel.Current.PromptAsync(
                "New player SteamID",
                "Enter the 17-digit SteamID64 for the new player (for example 76561198000000000).",
                placeholder: "76561198000000000",
                initialValue: initial,
                accept: "Create",
                cancel: "Cancel");
            if (text is null) return null;

            var trimmed = text.Trim();
            initial = trimmed;
            if (!ulong.TryParse(trimmed, out var id) || trimmed.Length != 17)
            {
                await DialogViewModel.Current.AlertAsync("Invalid SteamID",
                    "That isn't a valid 17-digit SteamID64. It should look like 76561198000000000.");
                continue;
            }
            if (File.Exists(Path.Combine(playerDir, $"Player_{id}.sav")))
            {
                await DialogViewModel.Current.AlertAsync("Already exists",
                    $"Player_{id}.sav already exists in this world. Choose a different SteamID.");
                continue;
            }
            return id;
        }
    }

    private static async Task<byte[]> LoadBlankPlayerTemplateAsync()
    {
        if (_blankPlayerTemplate is not null) return _blankPlayerTemplate;
        using var stream = await FileSystem.OpenAppPackageFileAsync("blank-player-template.sav");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return _blankPlayerTemplate = ms.ToArray();
    }

    // ---------- diagnostic logging (opt-in, persisted) ----------

    private const string DiagnosticLoggingPreferenceKey = "DiagnosticLogging";
    private bool _diagnosticLoggingEnabled;
    private RelayCommand? _openLogFolderCommand;

    /// <summary>
    /// Master switch for <see cref="EditorLog"/>. Persisted via Preferences (key
    /// <c>DiagnosticLogging</c>) and restored at startup. Default off.
    /// </summary>
    public bool DiagnosticLoggingEnabled
    {
        get => _diagnosticLoggingEnabled;
        set
        {
            if (!Set(ref _diagnosticLoggingEnabled, value)) return;
            Preferences.Default.Set(DiagnosticLoggingPreferenceKey, value);
            EditorLog.Enabled = value;
            if (value)
            {
                EditorLog.Info("App", "Diagnostic logging enabled by user.");
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoggingStatusText)));
        }
    }

    /// <summary>"Logging to &lt;path&gt;" while enabled, "Diagnostic logging off" otherwise.</summary>
    public string LoggingStatusText => _diagnosticLoggingEnabled
        ? $"Logging to {EditorLog.CurrentLogFilePath}"
        : "Diagnostic logging off";

    /// <summary>
    /// Opens the log folder in the OS file manager (creating it if no log was written
    /// yet). Mobile platforms have no user-facing file manager to launch, so they get
    /// the path on the status line instead.
    /// </summary>
    public System.Windows.Input.ICommand OpenLogFolderCommand => _openLogFolderCommand ??= new RelayCommand(() =>
    {
        try
        {
            Directory.CreateDirectory(EditorLog.LogDirectory);
#if WINDOWS
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{EditorLog.LogDirectory}\"",
                UseShellExecute = true,
            });
#elif MACCATALYST
            System.Diagnostics.Process.Start("/usr/bin/open", $"\"{EditorLog.LogDirectory}\"");
#else
            StatusMessage = $"Log folder: {EditorLog.LogDirectory}";
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the log folder: {ex.Message}";
        }
    });

    private RelayCommand? _importMappingsCommand;

    /// <summary>
    /// Picker filter for usmap import. .usmap has no registered MIME type / UTType, so
    /// the non-Windows platforms fall back to "any file" and rely on validation after
    /// the pick.
    /// </summary>
    private static readonly FilePickerFileType UsmapFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".usmap"],
        [DevicePlatform.Android] = ["application/octet-stream", "*/*"],
        [DevicePlatform.iOS] = ["public.data", "public.item"],
        [DevicePlatform.MacCatalyst] = ["usmap", "public.data"],
    });

    /// <summary>
    /// Lets the user install a newer <c>Mappings.usmap</c> (dumped from a future game
    /// build with FModel/Dumper-7). The file is validated and copied to the override
    /// location (<see cref="Core.Assets.GameAssetProvider.UserMappingsPath"/>), which
    /// wins over the bundled usmap on next launch.
    /// </summary>
    public System.Windows.Input.ICommand ImportMappingsCommand => _importMappingsCommand ??= new RelayCommand(async () =>
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a Mappings.usmap dumped from the game",
                FileTypes = UsmapFileType,
            });
            if (pick is null) return; // user cancelled

            var installed = Core.Assets.GameAssetProvider.InstallUserMappings(pick.FullPath);
            EditorLog.Info("App", $"User imported mappings: {pick.FullPath} -> {installed}");
            StatusMessage = "New mappings installed - restart the editor to load game data with them.";
            await DialogViewModel.Current.AlertAsync(
                "Mappings installed",
                $"Installed to:\n{installed}\n\nThe new mappings take effect after you restart the editor. " +
                "Delete that file to fall back to the bundled mappings.");
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", "Usmap import failed", ex);
            StatusMessage = $"Mappings import failed: {ex.Message}";
        }
    });

    public string? FolderPath
    {
        get => _folderPath;
        private set => Set(ref _folderPath, value);
    }

    // Save switching: load IMMEDIATELY when idle (a single deliberate click feels instant), but
    // serialize + coalesce while a load is running. Clicking fast through the list updates the
    // pending target instead of spawning a parse per click, so loads never run concurrently
    // (world saves are ~200 MB) and only the row you settle on is fully loaded. No fixed delay -
    // a debounce here added latency to every single click, which felt worse.
    private SaveFileSummary? _loadedSave;
    private SaveFileSummary? _pendingSelection;
    private bool _switchRunning;

    public SaveFileSummary? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (value == _selectedSave) return;
            _selectedSave = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSave)));
            _ = RequestSwitchAsync(value);
        }
    }

    private async Task RequestSwitchAsync(SaveFileSummary? next)
    {
        _pendingSelection = next;
        if (_switchRunning)
        {
            return; // a load is in flight; it will pick up the latest pending target when it finishes
        }

        _switchRunning = true;
        try
        {
            while (true)
            {
                var target = _pendingSelection;
                _pendingSelection = null;

                if (ReferenceEquals(target, _loadedSave))
                {
                    break; // already showing this save (e.g. clicked back onto it)
                }

                if (!await ConfirmLeaveCurrentEditorAsync())
                {
                    // User chose to stay: restore the selection to the actually-loaded save.
                    _selectedSave = _loadedSave;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSave)));
                    break;
                }

                await LoadEditorForAsync(target); // sets _loadedSave on success

                if (_pendingSelection is null)
                {
                    break; // no newer selection arrived during the load
                }
            }
        }
        finally
        {
            _switchRunning = false;
        }
    }

    /// <summary>
    /// Gate for any navigation that would drop the current editor. Saves are written
    /// per file, so leaving a dirty editor needs an explicit decision: save it, discard
    /// the staged edits, or stay. Returns false when the user chose to stay.
    /// </summary>
    public async Task<bool> ConfirmLeaveCurrentEditorAsync()
    {
        var dirtyName =
            PlayerEditor?.IsDirty == true ? Path.GetFileName(PlayerEditor.FilePath)
            : WorldEditor?.IsDirty == true ? Path.GetFileName(WorldEditor.FilePath)
            : IniEditor?.IsDirty == true ? IniEditor.FileName
            : null;
        if (dirtyName is null) return true;

        // A phantom dialog (binding write-back posing as an edit) names its source
        // here - check the log when the gate appears without any deliberate change.
        if (PlayerEditor?.IsDirty == true)
        {
            EditorLog.Info("App", $"Leave-gate for {dirtyName}: {PlayerEditor.DescribeDirty()}");
        }

        // In-app dialog: [Cancel, Discard changes, Save and continue].
        var choice = await DialogViewModel.Current.ShowAsync(
            $"{dirtyName} has unsaved changes",
            "You have staged edits that haven't been written to the save yet. " +
            "Save them, discard them, or stay on this editor.",
            ("Cancel", DialogTone.Neutral),
            ("Discard changes", DialogTone.Danger),
            ("Save and continue", DialogTone.Primary));

        switch (choice)
        {
            case 2: // Save and continue
                if (PlayerEditor?.IsDirty == true) await PlayerEditor.SaveAsync();
                else if (WorldEditor?.IsDirty == true) await WorldEditor.SaveAsync();
                else IniEditor?.Save();
                return true;
            case 1: // Discard changes
                EditorLog.Info("App", $"User discarded staged changes in {dirtyName}.");
                return true;
            default: // Cancel / dismissed
                return false;
        }
    }

    /// <summary>
    /// Returns to the landing page (open-folder actions + worlds detected on this
    /// machine) by tearing down whichever editor is open, after the leave-gate. The
    /// scanned folder and its save list stay loaded in the sidebar.
    /// </summary>
    public async Task GoHomeAsync()
    {
        if (!await ConfirmLeaveCurrentEditorAsync()) return;
        // Direct teardown - routing through the selection setters would re-run the
        // leave-gate (same pattern as SwitchConfigFileAsync).
        _selectedSave = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSave)));
        _selectedConfigFile = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfigFile)));
        PlayerEditor = null;
        WorldEditor = null;
        IniEditor = null;
        ActiveSlot = null;
        _ = DiscoverWorldsAsync();
    }

    public PlayerEditorViewModel? PlayerEditor
    {
        get => _playerEditor;
        private set
        {
            var previous = _playerEditor;
            if (Set(ref _playerEditor, value))
            {
                if (previous is not null) previous.PropertyChanged -= OnEditorContextChanged;
                if (value is not null)
                {
                    value.PropertyChanged += OnEditorContextChanged;
                    // The region resolves off-thread; ground items need it. Pickup
                    // commits / reverts re-scan so the list reflects the world files.
                    value.GroundContextReady += () =>
                        MainThread.BeginInvokeOnMainThread(() => _ = UpdateNearbyGroundItemsAsync());
                    value.GroundPickupsCommitted += touchedFiles =>
                        MainThread.BeginInvokeOnMainThread(() => _ = UpdateNearbyGroundItemsAsync());
                    value.GroundDropsCommitted += touchedFiles =>
                        MainThread.BeginInvokeOnMainThread(() => _ = UpdateNearbyGroundItemsAsync());
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPlayerEditor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoEditor)));
                RaiseSidebarContextChanged();
                _ = UpdateHomeBedAsync();
                _ = UpdateNearbyGroundItemsAsync();
            }
        }
    }

    // ---------- context-sensitive right sidebar ----------

    /// <summary>
    /// The item catalog only shows where dragging items somewhere makes sense: the
    /// player INVENTORY and VITALS tabs (transmog slots live on VITALS) and the world
    /// CONTAINERS/DROPPED tabs.
    /// </summary>
    public bool ShowItemPalette => HasItemPalette && (
        (PlayerEditor?.IsInventoryTab ?? false)
        || (PlayerEditor?.IsTransmogTab ?? false)
        || (WorldEditor is { } we && (we.IsContainersTab || we.IsDroppedTab))
        // On the GATEPal/codex tab the palette only surfaces once an item (e.g. a fish's
        // bait) has been opened into it, so the sidebar shows that item's encyclopedia.
        || ((PlayerEditor?.IsCodexTab ?? false) && _itemPalette?.SelectedItem is not null));

    /// <summary>Trader detail takes over the sidebar while a trader card is selected.</summary>
    public bool ShowTraderDetail => WorldEditor?.HasSelectedTrader == true;

    /// <summary>Quest/chapter detail takes over the sidebar while a chapter is selected.</summary>
    public bool ShowChapterDetail => WorldEditor?.HasSelectedChapter == true;

    /// <summary>Flag detail takes over the sidebar while a quest flag is selected.</summary>
    public bool ShowFlagDetail => WorldEditor?.HasSelectedFlag == true;

    /// <summary>Door detail takes over the sidebar while a door is selected.</summary>
    public bool ShowDoorDetail => WorldEditor?.HasSelectedDoor == true;

    /// <summary>Skill milestone detail takes over the sidebar while a milestone is selected.</summary>
    public bool ShowMilestoneDetail => PlayerEditor?.HasSelectedMilestone == true;

    private void OnEditorContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the sidebar to a single context: a detail selection becoming active clears the
        // others, and switching world tabs clears whatever was open on the previous tab.
        switch (e.PropertyName)
        {
            case "HasSelectedDoor" when WorldEditor?.HasSelectedDoor == true:
                EnsureSingleSidebarContext(SidebarContext.Door); break;
            case "HasSelectedTrader" when WorldEditor?.HasSelectedTrader == true:
                EnsureSingleSidebarContext(SidebarContext.Trader); break;
            case "HasSelectedChapter" when WorldEditor?.HasSelectedChapter == true:
                EnsureSingleSidebarContext(SidebarContext.Chapter); break;
            case "HasSelectedFlag" when WorldEditor?.HasSelectedFlag == true:
                EnsureSingleSidebarContext(SidebarContext.Flag); break;
            case "HasSelectedWorldRecipe" when WorldEditor?.HasSelectedWorldRecipe == true:
                EnsureSingleSidebarContext(SidebarContext.WorldRecipe); break;
            case "HasSelectedMilestone" when PlayerEditor?.HasSelectedMilestone == true:
                EnsureSingleSidebarContext(SidebarContext.Milestone); break;
            case "ActiveTab" when ReferenceEquals(sender, WorldEditor):
                EnsureSingleSidebarContext(SidebarContext.None); break;
        }

        if (e.PropertyName is "IsInventoryTab" or "IsTransmogTab" or "IsContainersTab" or "IsDroppedTab"
            or "IsCodexTab" or "HasSelectedTrader" or "HasSelectedChapter" or "HasSelectedFlag" or "HasSelectedDoor"
            or "HasSelectedWorldRecipe" or "HasSelectedMilestone" or "ActiveTab")
        {
            RaiseSidebarContextChanged();
        }
    }

    /// <summary>Which one panel the right sidebar is showing (the others are cleared).</summary>
    private enum SidebarContext { None, Slot, Door, Trader, Chapter, Flag, WorldRecipe, Milestone }

    private bool _coalescingSidebar;

    /// <summary>
    /// Enforces that only one sidebar detail context is live at a time. Selecting a slot, door,
    /// flag, trader, chapter, recipe or milestone clears every other selection so their panels
    /// can't stack. Re-entrant clears are suppressed (clearing a selection raises its own
    /// PropertyChanged, which would otherwise loop back in here).
    /// </summary>
    private void EnsureSingleSidebarContext(SidebarContext keep)
    {
        if (_coalescingSidebar) return;
        _coalescingSidebar = true;
        try
        {
            if (keep != SidebarContext.Slot) ActiveSlot = null;
            if (WorldEditor is { } we)
            {
                if (keep != SidebarContext.Door) we.SelectedDoor = null;
                if (keep != SidebarContext.Trader) we.SelectedTrader = null;
                if (keep != SidebarContext.Chapter) we.SelectedChapter = null;
                if (keep != SidebarContext.Flag) we.SelectedFlag = null;
                if (keep != SidebarContext.WorldRecipe) we.GlobalRecipeBrowser.SelectedRecipe = null;
            }
            if (keep != SidebarContext.Milestone && PlayerEditor is { } pe) pe.SelectedMilestone = null;
        }
        finally
        {
            _coalescingSidebar = false;
        }
    }

    private void RaiseSidebarContextChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowItemPalette)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTraderDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowChapterDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowFlagDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDoorDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWorldRecipeDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMilestoneDetail)));
    }

    /// <summary>Recipe detail takes over the sidebar while a metadata world recipe is selected.</summary>
    public bool ShowWorldRecipeDetail => WorldEditor?.HasSelectedWorldRecipe == true;

    public bool HasPlayerEditor => _playerEditor is not null;

    private string? _homeBedText;

    /// <summary>
    /// The player's claimed bed resolved from the world's deployables (bed claims encode
    /// <c>steamid}|!|{name</c> in CustomTextDisplay). Null until resolved / when none.
    /// </summary>
    public string? HomeBedText
    {
        get => _homeBedText;
        private set
        {
            if (Set(ref _homeBedText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHomeBed)));
            }
        }
    }

    public bool HasHomeBed => _homeBedText is not null;

    public ObservableCollection<BedOption> BedOptions { get; } = new();

    private BedOption? _selectedBedOption;

    public BedOption? SelectedBedOption
    {
        get => _selectedBedOption;
        set => Set(ref _selectedBedOption, value);
    }

    public System.Windows.Input.ICommand SetSpawnToBedCommand => _setSpawnToBedCommand ??= new RelayCommand(
        async () => await SetSpawnToBedAsync());
    private RelayCommand? _setSpawnToBedCommand;

    private async Task SetSpawnToBedAsync()
    {
        if (PlayerEditor is not { } pe || _selectedBedOption is not { } bed) return;
        var playerId = (ulong)Math.Max(0, pe.SteamId64);

        // Respawning at a bed requires owning its claim; a bed claimed by someone else
        // must be reassigned - which leaves that player without a claimed bed. That is
        // exactly the kind of edit nobody should make by accident.
        var claimedByOther = bed.OwnerSteamId is { } owner && owner != playerId;
        if (claimedByOther)
        {
            var ownerLabel = bed.OwnerName is { Length: > 0 } n ? n : bed.OwnerSteamId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var confirmed = await DialogViewModel.Current.ConfirmAsync(
                "Bed belongs to another player",
                $"This bed is claimed by {ownerLabel}. Setting your spawn here reassigns the claim to this " +
                $"player and leaves {ownerLabel} with NO claimed bed (they respawn at the fallback spawn).\n\n" +
                "The world save is rewritten immediately (.bak kept). Continue?",
                "REASSIGN BED", "Cancel", DialogTone.Danger);
            if (!confirmed) return;
        }

        pe.SetRespawnLocation(bed.X, bed.Y, bed.Z + 90);

        // Reassign / take the claim in the world save unless it is already ours.
        if (playerId > 0 && bed.OwnerSteamId != playerId)
        {
            var claimName = ResolvePlayerName(playerId);
            var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(pe.FilePath));
            var facility = worldDir is null ? null : Path.Combine(worldDir, "WorldSave_Facility.sav");
            if (facility is not null && File.Exists(facility))
            {
                var ok = await Task.Run(() =>
                {
                    var data = WorldSaveReader.ReadFromFile(facility);
                    if (!WorldSaveWriter.ApplyDeployableCustomText(
                            data, bed.Id, $"{playerId}{Core.WorldSaves.WorldDeployable.ClaimSeparator}{claimName}"))
                    {
                        return false;
                    }
                    WorldSaveWriter.WriteToFile(data, facility);
                    return true;
                });

                if (ok)
                {
                    // The cached deployables now lie about the claim; reload them.
                    _benchCache.Remove(facility);
                    await UpdateHomeBedAsync();
                    StatusMessage = claimedByOther
                        ? $"Bed reassigned to {claimName} and spawn moved there (world save rewritten, .bak kept). Press SAVE to write the player's spawn point."
                        : $"Bed claimed for {claimName} and spawn moved there (world save rewritten, .bak kept). Press SAVE to write the player's spawn point.";
                    return;
                }
                StatusMessage = "Spawn moved, but the bed claim could not be rewritten (deployable not found in the Facility save).";
                return;
            }
        }
        StatusMessage = $"Spawn moved to {bed.Label} - press SAVE to write it. " +
            "Check the level picker matches the bed's region.";
    }

    /// <summary>Display name to embed in a bed claim for this player.</summary>
    private string ResolvePlayerName(ulong steamId)
    {
        var fromSidebar = Saves.FirstOrDefault(s =>
            s.PlayerName is not null && SteamPersonaIndex.SteamIdFromPlayerPath(s.FullPath) == steamId)?.PlayerName;
        if (fromSidebar is not null) return fromSidebar;
        return SteamPersonaIndex.LoadMachineAccounts().TryGetValue(steamId, out var persona)
            ? persona
            : "Player";
    }

    // ---------- ground items near the player (INVENTORY tab pick-up) ----------

    /// <summary>Dropped items within reach of the player's last position.</summary>
    public ObservableCollection<GroundItemOption> NearbyGroundItems { get; } = new();

    public bool HasNearbyGroundItems => NearbyGroundItems.Count > 0;

    /// <summary>"Within reach": generous interactable range around the saved position (UE cm).</summary>
    private const double GroundPickupRange = 1500;

    private async Task UpdateNearbyGroundItemsAsync()
    {
        NearbyGroundItems.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));
        if (PlayerEditor is not { } pe) return;

        var (px, py, pz) = pe.LastSafePosition;
        if (px == 0 && py == 0 && pz == 0) return;

        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(pe.FilePath));
        if (worldDir is null) return;

        // The player's region save plus the persistent Facility both can hold litter
        // around the position.
        var candidates = new List<string>();
        var facility = Path.Combine(worldDir, "WorldSave_Facility.sav");
        if (File.Exists(facility)) candidates.Add(facility);
        var regionFile = pe.RespawnLevelFileName is { } level
            ? Path.Combine(worldDir, $"WorldSave_{level}.sav")
            : null;
        if (regionFile is not null && File.Exists(regionFile) && !candidates.Contains(regionFile))
        {
            candidates.Add(regionFile);
        }

        var found = await Task.Run(() =>
        {
            var list = new List<GroundItemOption>();
            foreach (var file in candidates)
            {
                try
                {
                    foreach (var item in WorldSaveReader.ReadFromFile(file).DroppedItems)
                    {
                        if (item.Slot.IsEmpty) continue;
                        var distance = item.DistanceTo(px, py, pz);
                        if (distance > GroundPickupRange) continue;
                        list.Add(new GroundItemOption(file, item, distance));
                    }
                }
                catch (Exception ex)
                {
                    EditorLog.Warn("Pickup", $"Ground scan of {Path.GetFileName(file)} failed: {ex.Message}");
                }
            }
            return list.OrderBy(g => g.Distance).ToList();
        });

        if (PlayerEditor != pe) return; // switched away during the scan
        foreach (var g in found)
        {
            NearbyGroundItems.Add(g);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));
    }

    /// <summary>
    /// Moves a nearby ground item into the player's first empty backpack slot. The
    /// inventory change stages like any other edit; the world-save removal commits
    /// together with the player SAVE, so a discard leaves the item on the ground.
    /// </summary>
    public void PickUpGroundItem(GroundItemOption option)
    {
        if (PlayerEditor is not { } pe) return;

        var target = pe.Main.FirstOrDefault(s => s.IsEmpty);
        if (target is null)
        {
            StatusMessage = "No empty backpack slot - drop or move something first.";
            return;
        }

        var s = option.Item.Slot;
        target.ItemId = s.ItemId;
        target.Count = s.Count;
        target.Durability = s.Durability;
        target.MaxDurability = s.MaxDurability;
        target.AmmoInMagazine = s.AmmoInMagazine;
        target.LiquidLevel = s.LiquidLevel;
        target.LiquidType = s.LiquidType;
        target.PlayerMadeString = s.PlayerMadeString;
        target.EnsureIcon();

        if (option.IsStaged)
        {
            // Picking up something you just dropped (still only staged): cancel the pending drop
            // rather than staging a removal of a ground entry that was never written.
            var index = pe.PendingGroundDrops.FindIndex(d => ReferenceEquals(d.Slot, s));
            if (index >= 0) pe.PendingGroundDrops.RemoveAt(index);
            NearbyGroundItems.Remove(option);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));
            StatusMessage = $"{option.DisplayName} returned to slot {target.Index + 1} (drop cancelled).";
            return;
        }

        pe.PendingGroundPickups.Add((option.WorldFile, option.Item.Id));
        NearbyGroundItems.Remove(option);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));
        EditorLog.Info("Pickup", $"Staged pick-up of {s.ItemId} ({option.Item.Id}) into slot {target.Index}.");
        StatusMessage = $"{option.DisplayName} staged into slot {target.Index + 1}. " +
            "SAVE writes the inventory and removes it from the ground (.bak kept).";
    }

    private async Task UpdateHomeBedAsync()
    {
        var steamId = PlayerEditor?.SteamId64 ?? 0;
        if (steamId <= 0)
        {
            HomeBedText = null;
            BedOptions.Clear();
            return;
        }

        var deployables = await LoadWorldDeployablesAsync();
        if (deployables is null)
        {
            HomeBedText = "No world save found next to this file - bed lookup unavailable.";
            return;
        }

        var bases = Core.WorldSaves.BaseDetector.Detect(deployables);

        // Player beds, claimed-by-this-player first, as spawn targets. Pet beds are
        // claimable in the same map but a player can't respawn on one.
        BedOptions.Clear();
        BedOption? mine = null;
        foreach (var b in deployables.Where(d => d.IsBed && !d.IsPetBed)
                     .OrderByDescending(d => d.OwnerSteamId == (ulong)steamId))
        {
            var home = bases.FirstOrDefault(bs => bs.Deployables.Contains(b));
            var option = new BedOption(b.Id,
                $"{b.DisplayName} - {home?.Name ?? "outside any base"} ({b.X:F0}, {b.Y:F0})",
                b.X, b.Y, b.Z, b.OwnerSteamId, b.OwnerName);
            BedOptions.Add(option);
            if (b.OwnerSteamId == (ulong)steamId) mine ??= option;
        }
        SelectedBedOption = mine ?? BedOptions.FirstOrDefault();

        var bed = deployables.FirstOrDefault(d =>
            d.IsBed && d.OwnerSteamId is { } owner && owner == (ulong)steamId);
        if (bed is null)
        {
            HomeBedText = "No claimed bed found in this world (respawns at the fallback spawn).";
            return;
        }

        var bedBase = bases.FirstOrDefault(b => b.Deployables.Contains(bed));
        HomeBedText = $"{bed.FriendlyClass} - {bedBase?.Name ?? "outside any base"} ({bed.X:F0}, {bed.Y:F0})"
            + (bed.OwnerName is { } n ? $" · claimed by {n}" : string.Empty);
    }

    public WorldEditorViewModel? WorldEditor
    {
        get => _worldEditor;
        private set
        {
            var previous = _worldEditor;
            if (Set(ref _worldEditor, value))
            {
                if (previous is not null) previous.PropertyChanged -= OnEditorContextChanged;
                if (value is not null) value.PropertyChanged += OnEditorContextChanged;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWorldEditor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoEditor)));
                RaiseSidebarContextChanged();
            }
        }
    }

    public bool HasWorldEditor => _worldEditor is not null;
    public bool HasNoEditor => _playerEditor is null && _worldEditor is null && _iniEditor is null;

    // ---------- ini files (server Admin.ini, SandboxSettings.ini, client config) ----------

    private IniEditorViewModel? _iniEditor;
    private ConfigFileOption? _selectedConfigFile;

    /// <summary>Ini files discovered near the loaded save folder; empty when none.</summary>
    public ObservableCollection<ConfigFileOption> ConfigFiles { get; } = new();

    /// <summary>The subset of <see cref="ConfigFiles"/> passing the current sidebar filter.</summary>
    public ObservableCollection<ConfigFileOption> VisibleConfigFiles { get; } = new();

    public bool HasConfigFiles => VisibleConfigFiles.Count > 0;

    private void RebuildConfigFilter()
    {
        VisibleConfigFiles.Clear();
        foreach (var c in ConfigFiles.Where(MatchesFilter))
        {
            VisibleConfigFiles.Add(c);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasConfigFiles)));
    }

    public IniEditorViewModel? IniEditor
    {
        get => _iniEditor;
        private set
        {
            if (Set(ref _iniEditor, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIniEditor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoEditor)));
                RaiseSidebarContextChanged();
            }
        }
    }

    public bool HasIniEditor => _iniEditor is not null;

    /// <summary>Selecting a config file opens the ini editor and deselects any save.</summary>
    public ConfigFileOption? SelectedConfigFile
    {
        get => _selectedConfigFile;
        set
        {
            if (value == _selectedConfigFile) return;
            var previous = _selectedConfigFile;
            _selectedConfigFile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfigFile)));
            if (value is null) return;
            _ = SwitchConfigFileAsync(previous, value);
        }
    }

    private async Task SwitchConfigFileAsync(ConfigFileOption? previous, ConfigFileOption next)
    {
        if (!await ConfirmLeaveCurrentEditorAsync())
        {
            _selectedConfigFile = previous;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedConfigFile)));
            return;
        }

        // Direct teardown: routing through the SelectedSave setter would run the
        // leave-confirmation a second time.
        _selectedSave = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSave)));
        PlayerEditor = null;
        WorldEditor = null;
        ActiveSlot = null;

        // Let the editor that was just torn down finish detaching before the ini view
        // binds: a still-focused Entry whose platform handler is already disconnected
        // makes MAUI's visual-state teardown throw ("PlatformView cannot be null")
        // synchronously inside this swap. One dispatcher hop drains that cascade.
        await Task.Yield();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                IniEditor = new IniEditorViewModel(next.File.FullPath, next.File.Kind);
                StatusMessage = $"Editing {next.Name} - every save keeps a .bak of the previous file.";
                break;
            }
            catch (InvalidOperationException ex) when (attempt == 0 && ex.Message.Contains("PlatformView"))
            {
                // The focus teardown raced us anyway; retry once after it settles.
                EditorLog.Warn("Ini", $"Focus-teardown race opening {next.Name}; retrying once.");
                await Task.Delay(120);
            }
            catch (Exception ex)
            {
                EditorLog.Error("Ini", $"Opening {next.File.FullPath} failed", ex);
                StatusMessage = $"Could not open {next.Name}: {ex.Message}";
                break;
            }
        }
    }

    // ---------- machine-wide world discovery (startup) ----------

    /// <summary>Worlds found on this machine (client saves + dedicated-server installs).</summary>
    public ObservableCollection<DiscoveredWorldOption> DiscoveredWorlds { get; } = new();

    public bool HasDiscoveredWorlds => DiscoveredWorlds.Count > 0;

    /// <summary>
    /// Scans the machine off-thread and fills <see cref="DiscoveredWorlds"/>. Safe to
    /// call repeatedly; the list is rebuilt each time.
    /// </summary>
    public async Task DiscoverWorldsAsync()
    {
        try
        {
            var found = await Task.Run(SaveDiscovery.DiscoverAll);
            DiscoveredWorlds.Clear();
            foreach (var world in found)
            {
                DiscoveredWorlds.Add(new DiscoveredWorldOption(world));
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiscoveredWorlds)));
            if (found.Count > 0)
            {
                EditorLog.Info("Discovery", $"Found {found.Count} world(s) on this machine.");
            }
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Discovery", $"World discovery failed: {ex.Message}");
        }
    }

    /// <summary>Loads a folder after the leave-confirmation gate (drag-drop and discovery rows).</summary>
    public async Task LoadFolderGuardedAsync(string folder)
    {
        if (!await ConfirmLeaveCurrentEditorAsync()) return;
        await LoadFolderAsync(folder);
    }

    private void DiscoverConfigFiles(string folder)
    {
        ConfigFiles.Clear();
        try
        {
            foreach (var ini in Core.Ini.AbioticIniCatalog.Discover(folder))
            {
                ConfigFiles.Add(new ConfigFileOption(ini));
            }
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Ini", $"Config discovery under {folder} failed: {ex.Message}");
        }
        RebuildConfigFilter();
    }


    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    /// <summary>
    /// Absolute path to the extracted AF logo PNG, or null if the game isn't installed
    /// or no usmap mappings are available. The header binds an <c>&lt;Image&gt;</c> to
    /// this; when null, the orange placeholder tile shows instead.
    /// </summary>
    public string? LogoPath
    {
        get => _logoPath;
        private set => Set(ref _logoPath, value);
    }

    public bool HasLogo => _logoPath is not null;

    private ItemPaletteViewModel? _itemPalette;

    /// <summary>
    /// The searchable item palette. Null until game data is loaded; null forever when no
    /// local install / mappings are available (the palette needs ItemTable_Global).
    /// </summary>
    public ItemPaletteViewModel? ItemPalette
    {
        get => _itemPalette;
        private set
        {
            if (Set(ref _itemPalette, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItemPalette)));
            }
        }
    }

    public bool HasItemPalette => _itemPalette is not null;

    /// <summary>
    /// Opens the palette's encyclopedia card for an item id (description, stats,
    /// crafted-by / used-in / sold-by). Used by surfaces that show items outside the
    /// palette, like a tapped dropped item. No-op when game data isn't loaded.
    /// </summary>
    public void ShowItemEncyclopedia(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId) || _itemPalette is null) return;
        _itemPalette.SelectedItem = _itemPalette.FindById(itemId);
        // On the codex tab the palette is hidden until an item is selected; reveal it now.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowItemPalette)));
    }

    private InventorySlotViewModel? _activeSlot;

    /// <summary>
    /// The slot currently shown in the right-hand slot editor - player or world container
    /// slots alike. Null hides the editor section entirely.
    /// </summary>
    public InventorySlotViewModel? ActiveSlot
    {
        get => _activeSlot;
        set
        {
            var previous = _activeSlot;
            if (Set(ref _activeSlot, value))
            {
                // The highlight in the slot grids tracks whichever slot is open here.
                if (previous is not null) previous.IsSelected = false;
                if (value is not null) value.IsSelected = true;
                DismantlePreviewText = null;
                if (_itemPalette is not null)
                {
                    _itemPalette.RoleFilter = value?.Role;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveSlot)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActiveSlotTeleporter)));
                // Opening a slot owns the sidebar; clear any door/flag/trader/... detail so two
                // panels never stack (e.g. a door selected on the DOORS tab then a dropped item).
                if (value is not null) EnsureSingleSidebarContext(SidebarContext.Slot);
                if (IsActiveSlotTeleporter)
                {
                    _ = EnsureBenchOptionsAsync();
                }
                UpdateTeleporterSyncText();
            }
        }
    }

    public bool HasActiveSlot => _activeSlot is not null;

    // ---------- dismantle preview (two-step confirm) ----------

    private string? _dismantlePreviewText;

    /// <summary>
    /// What pressing CONFIRM will do: yielded ingredient stacks + where they land.
    /// Null hides the preview panel.
    /// </summary>
    public string? DismantlePreviewText
    {
        get => _dismantlePreviewText;
        set
        {
            if (Set(ref _dismantlePreviewText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDismantlePreview)));
            }
        }
    }

    public bool HasDismantlePreview => _dismantlePreviewText is not null;

    // ---------- slot actions (shared by the inventory views + slot sidebar) ----------

    /// <summary>Opens a slot in the slot editor (player or world container alike).</summary>
    public void SelectSlot(InventorySlotViewModel slot)
    {
        ActiveSlot = slot;
        if (PlayerEditor is { } editor)
        {
            editor.SelectedSlot = slot;
        }
        StatusMessage = $"Selected {slot.DisplayName} (slot {slot.Index})";
    }

    /// <summary>Sorts the backpack: occupied slots first, grouped by category then name.</summary>
    public void SortBackpack()
    {
        if (PlayerEditor is not { } pe) return;

        var items = pe.Main
            .Where(s => !s.IsEmpty)
            .Select(s => s.ToCurrentSlot())
            .OrderBy(s => GameDataServices.Catalog?.Find(s.ItemId) is { } en
                ? ItemPaletteViewModel.CategoryOf(en) : "@zzz")
            .ThenBy(s => GameDataServices.Catalog?.Find(s.ItemId)?.DisplayName ?? s.ItemId,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < pe.Main.Count; i++)
        {
            var slot = pe.Main[i];
            if (i < items.Count)
            {
                var d = items[i];
                slot.ItemId = d.ItemId;
                slot.Count = d.Count;
                slot.Durability = d.Durability;
                slot.MaxDurability = d.MaxDurability;
                slot.AmmoInMagazine = d.AmmoInMagazine;
                slot.LiquidLevel = d.LiquidLevel;
                slot.LiquidType = d.LiquidType;
                slot.DynamicState = d.DynamicState;
                slot.PlayerMadeString = d.PlayerMadeString;
                slot.AssetId = d.AssetId;
            }
            else
            {
                SlotSwap.ClearToEmpty(slot);
            }
        }
        StatusMessage = $"Sorted {items.Count} stack(s) - press SAVE to write.";
    }

    /// <summary>
    /// Drops the active slot's item onto the world ground near the player: it is staged and,
    /// on SAVE, written into the appropriate world save's <c>DroppedItemMap</c> (by cloning an
    /// existing ground item there - see <see cref="WorldSaveWriter.AddDroppedItem"/>), after
    /// which it appears in NEARBY GROUND ITEMS. When the target region has no existing ground
    /// item to clone, the editor cannot synthesize one, so the item is simply removed and the
    /// status line says so.
    /// </summary>
    public async Task DropActiveItemAsync()
    {
        var slot = ActiveSlot;
        if (slot is null || slot.IsEmpty)
        {
            StatusMessage = "Select an occupied slot first, then DROP places it on the ground.";
            return;
        }
        if (PlayerEditor is not { } pe)
        {
            return;
        }

        var name = slot.DisplayName;
        var dropped = slot.ToCurrentSlot();
        var (px, py, pz) = pe.LastSafePosition;
        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(pe.FilePath));

        // Clear the slot immediately (responsive); the world write is staged until SAVE.
        SlotSwap.ClearToEmpty(slot);

        string? targetFile = null;
        if (worldDir is not null && !(px == 0 && py == 0 && pz == 0))
        {
            StatusMessage = $"Dropping {name}…";
            var candidates = new List<string>();
            if (pe.RespawnLevelFileName is { } level)
            {
                var regionFile = Path.Combine(worldDir, $"WorldSave_{level}.sav");
                if (File.Exists(regionFile)) candidates.Add(regionFile);
            }
            var facility = Path.Combine(worldDir, "WorldSave_Facility.sav");
            if (File.Exists(facility) && !candidates.Contains(facility)) candidates.Add(facility);

            // Pick the first candidate that already has a ground item to clone from (parsed
            // off the UI thread; the region save is tried before the larger Facility save).
            targetFile = await Task.Run(() =>
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (WorldSaveReader.ReadFromFile(candidate).DroppedItems.Count > 0)
                        {
                            return candidate;
                        }
                    }
                    catch (Exception ex)
                    {
                        EditorLog.Warn("Drop", $"Ground template scan of {Path.GetFileName(candidate)} failed: {ex.Message}");
                    }
                }
                return (string?)null;
            });
        }

        if (PlayerEditor != pe)
        {
            return; // switched saves during the scan
        }

        if (targetFile is not null)
        {
            pe.PendingGroundDrops.Add((targetFile, dropped, px, py, pz));

            // Surface it in NEARBY GROUND ITEMS straight away (staged; the same `dropped` slot
            // instance links this entry to its pending drop so pick-up can cancel it).
            var stagedItem = new Core.WorldSaves.WorldDroppedItem("staged", dropped, true, px, py, pz);
            NearbyGroundItems.Insert(0, new GroundItemOption(targetFile, stagedItem, 0) { IsStaged = true });
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));

            StatusMessage = $"Dropped {name} on the ground near you (shown in NEARBY GROUND ITEMS). "
                + $"SAVE writes it into {Path.GetFileName(targetFile)} (.bak kept); REVERT undoes it.";
        }
        else
        {
            StatusMessage = $"Removed {name} (couldn't drop it into the world - this region has no "
                + "existing ground item to clone the format from). Press SAVE to write.";
        }
    }

    /// <summary>
    /// Step 1 of dismantling: build a human-readable preview of what the active slot
    /// would break down into (its crafting-recipe ingredients) and where the stacks
    /// would land. The actual mutation happens in <see cref="ConfirmDismantle"/>.
    /// </summary>
    public void BeginDismantlePreview()
    {
        var slot = ActiveSlot;
        if (slot is null || slot.IsEmpty || slot.ItemId is null) return;

        var recipe = GameDataServices.RecipesCrafting(slot.ItemId).FirstOrDefault();
        if (recipe is null || recipe.IngredientList.Count == 0)
        {
            StatusMessage = $"{slot.DisplayName} has no crafting recipe - nothing to dismantle it into.";
            return;
        }

        var siblings = FindSlotCollection(slot);
        var empties = siblings.Where(s => s != slot && s.IsEmpty && !s.HasRole).ToList();
        var slotReusable = !slot.HasRole;
        var needed = recipe.IngredientList.Count - (slotReusable ? 1 : 0);
        if (empties.Count < needed)
        {
            StatusMessage = $"Dismantling needs {needed} empty slot(s) - only {empties.Count} available.";
            return;
        }

        var catalog = GameDataServices.Catalog;
        var yield = string.Join("\n", recipe.IngredientList.Select(i =>
            $"  {i.Count}× {catalog?.Find(i.ItemId)?.DisplayName ?? i.ItemId}"));
        var destination = slot.HasRole
            ? "All stacks go to empty backpack slots; this equipment slot is emptied."
            : "The first stack replaces this slot; the rest fill empty slots in the same inventory.";
        DismantlePreviewText = $"{slot.DisplayName} breaks down into:\n{yield}\n{destination}";
    }

    public void CancelDismantle() => DismantlePreviewText = null;

    /// <summary>
    /// Step 2: actually replace the item with its ingredients, like the game's
    /// Repair &amp; Salvage station.
    /// </summary>
    public void ConfirmDismantle()
    {
        DismantlePreviewText = null;

        var slot = ActiveSlot;
        if (slot is null || slot.IsEmpty || slot.ItemId is null) return;

        var recipe = GameDataServices.RecipesCrafting(slot.ItemId).FirstOrDefault();
        if (recipe is null || recipe.IngredientList.Count == 0) return;

        var siblings = FindSlotCollection(slot);
        var empties = siblings.Where(s => s != slot && s.IsEmpty && !s.HasRole).ToList();

        // Equipment slots can't hold ingredients - everything goes to the backpack and
        // the equipment slot is emptied. Regular slots reuse themselves for stack #1.
        var slotReusable = !slot.HasRole;
        var needed = recipe.IngredientList.Count - (slotReusable ? 1 : 0);
        if (empties.Count < needed)
        {
            StatusMessage = $"Dismantling needs {needed} empty slot(s) - only {empties.Count} available.";
            return;
        }

        var catalog = GameDataServices.Catalog;
        var destinations = slotReusable
            ? new[] { slot }.Concat(empties).ToList()
            : empties;
        for (var i = 0; i < recipe.IngredientList.Count; i++)
        {
            var ingredient = recipe.IngredientList[i];
            var entry = catalog?.Find(ingredient.ItemId);
            var dest = destinations[i];
            if (entry is not null)
            {
                SlotSwap.FillFromCatalog(dest, entry);
            }
            else
            {
                dest.ItemId = ingredient.ItemId;
            }
            dest.Count = ingredient.Count;
        }
        if (!slotReusable)
        {
            SlotSwap.ClearToEmpty(slot);
        }
        StatusMessage = $"Dismantled into {recipe.IngredientList.Count} ingredient stack(s).";
    }

    /// <summary>The inventory list the slot belongs to (for dismantle distribution).</summary>
    private IReadOnlyList<InventorySlotViewModel> FindSlotCollection(InventorySlotViewModel slot)
    {
        if (PlayerEditor is { } pe)
        {
            if (pe.Main.Contains(slot)) return pe.Main;
            if (pe.Hotbar.Contains(slot)) return pe.Hotbar;
            if (pe.Equipment.Contains(slot)) return pe.Main; // ingredients go to the backpack
        }
        if (WorldEditor is { } we)
        {
            var container = we.AllContainers.FirstOrDefault(c => c.Slots.Contains(slot));
            if (container is not null) return container.Slots;
        }
        return Array.Empty<InventorySlotViewModel>();
    }

    // ---------- Personal Teleporter ↔ crafting bench sync ----------
    // The link is the target bench's DeployedObjectMap GUID stored in the item's
    // PlayerMadeString (comma-terminated), discovered against the fixture saves and
    // covered by TeleporterLinkTests.

    // Per-instance and cleared on every folder load: a long session hopping between
    // save folders must not pin every world's deployable list for the process lifetime.
    private readonly Dictionary<string, IReadOnlyList<Core.WorldSaves.WorldDeployable>> _benchCache =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _teleporterSyncText;
    private BenchOption? _selectedBench;

    public bool IsActiveSlotTeleporter =>
        _activeSlot?.ItemId is { } id
        && (id.Contains("personalteleporter", StringComparison.OrdinalIgnoreCase)
            || id.Contains("moisture_teleporter", StringComparison.OrdinalIgnoreCase));

    public ObservableCollection<BenchOption> BenchOptions { get; } = new();

    public BenchOption? SelectedBench
    {
        get => _selectedBench;
        set => Set(ref _selectedBench, value);
    }

    public string? TeleporterSyncText
    {
        get => _teleporterSyncText;
        private set => Set(ref _teleporterSyncText, value);
    }

    public System.Windows.Input.ICommand SyncTeleporterCommand => _syncTeleporterCommand ??= new RelayCommand(() =>
    {
        if (_activeSlot is null || _selectedBench is null) return;
        _activeSlot.PlayerMadeString = _selectedBench.Id + ",";
        UpdateTeleporterSyncText();
        StatusMessage = $"Teleporter synced to {_selectedBench.Label} - press SAVE to write it.";
    });
    private RelayCommand? _syncTeleporterCommand;

    public System.Windows.Input.ICommand UnsyncTeleporterCommand => _unsyncTeleporterCommand ??= new RelayCommand(() =>
    {
        if (_activeSlot is null) return;
        _activeSlot.PlayerMadeString = string.Empty;
        UpdateTeleporterSyncText();
        StatusMessage = "Teleporter unsynced.";
    });
    private RelayCommand? _unsyncTeleporterCommand;

    private async Task EnsureBenchOptionsAsync()
    {
        var deployables = await LoadWorldDeployablesAsync();
        BenchOptions.Clear();
        if (deployables is null)
        {
            TeleporterSyncText = "No world save found next to this file - bench list unavailable.";
            return;
        }

        // Name each bench by the base it anchors so the picker is human-readable.
        var bases = Core.WorldSaves.BaseDetector.Detect(deployables);
        foreach (var bench in deployables.Where(d => d.IsCraftingBench))
        {
            var home = bases.FirstOrDefault(b => b.Deployables.Contains(bench));
            var label = $"{bench.DisplayName} - {home?.Name ?? "unclustered"} ({bench.X:F0}, {bench.Y:F0})";
            BenchOptions.Add(new BenchOption(bench.Id, label));
        }
        UpdateTeleporterSyncText();
    }

    /// <summary>
    /// Resolves persona names for the player rows in the sidebar: bed claims from the
    /// world's Facility save (every claiming co-op player) plus the machine's Steam
    /// accounts. Runs after a scan; rows update in place when names arrive.
    /// </summary>
    private async Task EnrichPlayerNamesAsync(string folder)
    {
        try
        {
            var names = new Dictionary<ulong, string>();
            foreach (var (id, name) in await Task.Run(SteamPersonaIndex.LoadMachineAccounts))
            {
                names[id] = name;
            }

            // Bed claims carry the in-game names of everyone who claimed a bed; the
            // facility save sits in the scanned folder for both world layouts.
            var facility = Path.Combine(folder, "WorldSave_Facility.sav");
            if (File.Exists(facility))
            {
                IReadOnlyList<Core.WorldSaves.WorldDeployable>? deployables;
                if (_benchCache.TryGetValue(facility, out var cached))
                {
                    deployables = cached;
                }
                else
                {
                    deployables = await Task.Run(() =>
                    {
                        try
                        {
                            var data = WorldSaveReader.ReadFromFile(facility);
                            _worldFlagCache[facility] =
                                new HashSet<string>(data.Flags, StringComparer.OrdinalIgnoreCase);
                            return data.Deployables;
                        }
                        catch (Exception ex)
                        {
                            EditorLog.Warn("Personas", $"Facility read for names failed: {ex.Message}");
                            return (IReadOnlyList<Core.WorldSaves.WorldDeployable>?)null;
                        }
                    });
                    if (deployables is not null)
                    {
                        // Seed the teleporter/bed caches so the slot editor doesn't
                        // parse the same 15 MB save again later in the session.
                        _benchCache[facility] = deployables;
                    }
                }
                if (deployables is not null)
                {
                    // Claim names win over Steam personas: they are what teammates see.
                    foreach (var (id, name) in SteamPersonaIndex.FromDeployables(deployables))
                    {
                        names[id] = name;
                    }
                }
            }
            if (names.Count == 0) return;

            var renamed = false;
            for (var i = 0; i < Saves.Count; i++)
            {
                var s = Saves[i];
                if (s.KindLabel != "PLAYER" || s.PlayerName is not null) continue;
                // Swapping the selected row would bounce the CollectionView selection
                // (and with it the open editor); it gets its name on the next scan.
                if (ReferenceEquals(s, _selectedSave)) continue;
                if (SteamPersonaIndex.SteamIdFromPlayerPath(s.FullPath) is not { } id) continue;
                if (!names.TryGetValue(id, out var name)) continue;

                Saves[i] = s with
                {
                    PlayerName = name,
                    DisplayName = $"{name}  ·  {Path.GetFileName(s.FullPath)}",
                };
                renamed = true;
            }
            if (renamed)
            {
                RebuildSaveGroups();
            }
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Personas", $"Player-name enrichment failed: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<Core.WorldSaves.WorldDeployable>?> LoadWorldDeployablesAsync()
    {
        // World editor open -> its own deployables, no file IO.
        if (WorldEditor is { } we && we.Deployables.Count > 0)
        {
            return we.Deployables;
        }

        // Player save -> the world's Facility save lives one folder up.
        var path = PlayerEditor?.FilePath;
        if (path is null) return null;
        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(path));
        if (worldDir is null) return null;
        var facility = Path.Combine(worldDir, "WorldSave_Facility.sav");
        if (!File.Exists(facility)) return null;

        if (_benchCache.TryGetValue(facility, out var cached))
        {
            Services.ProgressContext.WorldFlags = _worldFlagCache.TryGetValue(facility, out var f) ? f : null;
            return cached;
        }

        TeleporterSyncText = "Loading world benches (parsing the Facility save)…";
        var result = await Task.Run(() =>
        {
            try
            {
                var data = WorldSaveReader.ReadFromFile(facility);
                _worldFlagCache[facility] = new HashSet<string>(data.Flags, StringComparer.OrdinalIgnoreCase);
                return data.Deployables;
            }
            catch
            {
                return (IReadOnlyList<Core.WorldSaves.WorldDeployable>?)null;
            }
        });
        if (result is not null)
        {
            _benchCache[facility] = result;
        }
        // World story progress feeds the unlock gates (codex/recipes) for this player.
        Services.ProgressContext.WorldFlags = _worldFlagCache.TryGetValue(facility, out var flags) ? flags : null;
        return result;
    }

    private readonly Dictionary<string, IReadOnlySet<string>> _worldFlagCache =
        new(StringComparer.OrdinalIgnoreCase);

    private void UpdateTeleporterSyncText()
    {
        if (!IsActiveSlotTeleporter)
        {
            TeleporterSyncText = null;
            return;
        }
        var link = _activeSlot?.PlayerMadeString?.TrimEnd(',');
        if (string.IsNullOrEmpty(link))
        {
            TeleporterSyncText = "Not synced to any bench.";
            return;
        }
        var match = BenchOptions.FirstOrDefault(b => string.Equals(b.Id, link, StringComparison.OrdinalIgnoreCase));
        TeleporterSyncText = match is not null
            ? $"Synced to: {match.Label}"
            : $"Synced to {link} (bench not found in this world - possibly removed or in another region).";
    }

    /// <summary>
    /// Kicks off background extraction of the AF logo from the user's game install.
    /// No-op if the game isn't installed or asset mappings aren't present.
    /// </summary>
    public async Task LoadLogoAsync()
    {
        await GameDataServices.EnsureLoadedAsync();

        if (GameDataServices.Catalog is { } catalog && _itemPalette is null)
        {
            // Build the palette index off the UI thread - it sorts ~1000 entries.
            var palette = await Task.Run(() => new ItemPaletteViewModel(catalog));
            ItemPalette = palette;
        }

        try
        {
            var provider = GameDataServices.Provider;
            if (provider is null || !provider.HasMappings) return;

            string?[] candidates =
            {
                "AbioticFactor/Content/Textures/GUI/Inventory/T_ABF_Logo_1024",
                "AbioticFactor/Content/Textures/GUI/Logos/ABF-Full-Color-1024w",
            };
            foreach (var c in candidates)
            {
                var path = provider.ExtractTextureAsPng(c!);
                if (path is not null)
                {
                    LogoPath = path;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLogo)));
                    return;
                }
            }
        }
        catch
        {
            // Logo is purely cosmetic - never bring down the app over it.
        }
    }

    private const string LastFolderPreferenceKey = "LastSaveFolder";

    /// <summary>
    /// Restores the previous session's folder on startup. Overridable for testing via
    /// <c>ABIOTIC_EDITOR_FOLDER</c> (folder to open) and <c>ABIOTIC_EDITOR_AUTOSELECT</c>
    /// (file-name suffix of a save to select once scanned).
    /// </summary>
    public async Task RestoreLastFolderAsync()
    {
        var folder = Environment.GetEnvironmentVariable("ABIOTIC_EDITOR_FOLDER");
        if (string.IsNullOrEmpty(folder))
        {
            folder = Preferences.Default.Get(LastFolderPreferenceKey, string.Empty);
        }
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        await LoadFolderAsync(folder);

        var autoSelect = Environment.GetEnvironmentVariable("ABIOTIC_EDITOR_AUTOSELECT");
        if (!string.IsNullOrEmpty(autoSelect))
        {
            SelectedSave = Saves.FirstOrDefault(s =>
                s.FullPath.EndsWith(autoSelect, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Re-homes the loaded player save to a new SteamID64. The id lives in the file name
    /// AND the save's top-level <c>SaveIdentifier</c> property, so both are rewritten via
    /// <see cref="Core.PlayerSaves.PlayerSaveIdentity"/> (a .bak copy of the original is
    /// kept). World-side state keyed by the old id (bed claims) is untouched. Returns an
    /// error message, or null on success.
    /// </summary>
    public async Task<string?> ChangePlayerSteamIdAsync(string newIdText)
    {
        if (PlayerEditor is not { } pe) return "No player save loaded.";
        if (!ulong.TryParse(newIdText?.Trim(), out var newId) || newIdText!.Trim().Length != 17)
        {
            return "Enter a valid 17-digit SteamID64 (e.g. 76561198000000000).";
        }

        var oldPath = pe.FilePath;
        var dir = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, $"Player_{newId}.sav");
        if (File.Exists(newPath)) return $"Player_{newId}.sav already exists in this folder.";
        var oldId = SteamPersonaIndex.SteamIdFromPlayerPath(oldPath);

        try
        {
            await Task.Run(() => Core.PlayerSaves.PlayerSaveIdentity.ChangeSteamId(oldPath, newId));
            EditorLog.Info("App", $"Re-homed player save {Path.GetFileName(oldPath)} -> {Path.GetFileName(newPath)} (SaveIdentifier rewritten)");
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", $"SteamID change failed for {oldPath}", ex);
            return $"SteamID change failed: {ex.Message}";
        }

        // World data references the player too: bed claims embed the SteamID. Player
        // saves live in PlayerData/ next to the world saves, so when those exist the
        // identity change follows into them and the player keeps their beds.
        var claims = 0;
        if (oldId is { } from)
        {
            var worldDir = Path.GetDirectoryName(dir);
            if (worldDir is not null)
            {
                claims = await Task.Run(() => WorldSteamIdPatcher.PatchFolder(worldDir, from, newId));
            }
        }

        // Rescan so the sidebar reflects the new file, then select it.
        if (FolderPath is { } folder)
        {
            await LoadFolderAsync(folder);
            SelectedSave = Saves.FirstOrDefault(s =>
                s.FullPath.EndsWith($"Player_{newId}.sav", StringComparison.OrdinalIgnoreCase));
        }
        StatusMessage = claims > 0
            ? $"Player save now belongs to {newId}; {claims} bed claim(s) in the world saves were updated too (.bak kept per file)."
            : $"Player save now belongs to {newId} (file renamed and SaveIdentifier rewritten). No bed claims by the old id were found in the world saves.";
        return null;
    }

    public async Task LoadFolderAsync(string folder)
    {
        FolderPath = folder;
        Preferences.Default.Set(LastFolderPreferenceKey, folder);
        EditorLog.ResetUnknownDedup();
        // Cross-folder state: drop cached world data and the progress gates so the new
        // folder's codex/recipe gating can't judge against the previous world's flags.
        _benchCache.Clear();
        _worldFlagCache.Clear();
        Services.ProgressContext.WorldFlags = null;
        IniEditor = null;
        SelectedConfigFile = null;
        DiscoverConfigFiles(folder);
        Saves.Clear();
        SelectedSave = null;
        IsScanning = true;
        StatusMessage = $"Scanning {folder}…";

        try
        {
            var results = await Task.Run(() => SaveFolderScanner.Scan(folder));
            foreach (var item in results)
            {
                Saves.Add(item);
            }
            RebuildSaveGroups();
            var failures = results.Count(r => r.LoadError is not null);
            StatusMessage = failures == 0
                ? $"Loaded {results.Count} save(s)."
                : $"Loaded {results.Count} save(s); {failures} failed to parse.";
            _ = EnrichPlayerNamesAsync(folder);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private int _loadSequence;
    private bool _isLoadingEditor;

    /// <summary>True while a selected save is being parsed (the Facility save takes seconds).</summary>
    public bool IsLoadingEditor
    {
        get => _isLoadingEditor;
        private set => Set(ref _isLoadingEditor, value);
    }

    /// <summary>
    /// Re-parses the currently selected save from disk and rebuilds its editor, discarding
    /// any staged edits. Used after an external write (e.g. a plugin save operation) so the
    /// open editor reflects the new file contents. No-op when no save is selected.
    /// </summary>
    public Task ReloadSelectedSaveAsync() => LoadEditorForAsync(_selectedSave);

    private async Task LoadEditorForAsync(SaveFileSummary? summary)
    {
        // Rapidly switching saves can leave an older (slower) load completing after a
        // newer one - the token makes stale results drop on the floor.
        var token = ++_loadSequence;

        PlayerEditor = null;
        WorldEditor = null;
        ActiveSlot = null;
        if (summary is not null)
        {
            // A save selection replaces any open ini editor.
            IniEditor = null;
            SelectedConfigFile = null;
        }
        if (summary is null)
        {
            _loadedSave = null;
            // No save open now - let event-handler plugins react (e.g. close a side panel).
            Core.Plugins.PluginManager.Shared.RaiseEvent(Plugins.Events.PluginEvents.SaveClosed);
            return;
        }

        IsLoadingEditor = true;
        try
        {
            await GameDataServices.EnsureLoadedAsync();

            if (summary.SaveClass == PlayerSaveClass)
            {
                var data = await Task.Run(() => PlayerSaveReader.ReadFromFile(summary.FullPath));
                if (token != _loadSequence) return;
                PlayerEditor = new PlayerEditorViewModel(data, summary.FullPath);
            }
            else if (summary.SaveClass == WorldSaveClass || summary.SaveClass == WorldMetaSaveClass)
            {
                var data = await Task.Run(() => WorldSaveReader.ReadFromFile(summary.FullPath));
                if (token != _loadSequence) return;
                WorldEditor = new WorldEditorViewModel(data, summary.FullPath);
            }

            // Notify plugins that a save is now open (after a successful parse).
            if (token == _loadSequence)
            {
                Core.Plugins.PluginManager.Shared.RaiseEvent(
                    Plugins.Events.PluginEvents.SaveOpened,
                    new Dictionary<string, object?>
                    {
                        ["savePath"] = summary.FullPath,
                        ["saveKind"] = Core.Plugins.SaveKindDetector.FromSaveClass(summary.SaveClass),
                    });
            }
        }
        catch (Exception ex)
        {
            if (token == _loadSequence)
            {
                StatusMessage = $"Could not load save: {ex.Message}";
            }
        }
        finally
        {
            // Only the winning (current) load touches shared state - a superseded load must not
            // flip the spinner, reclaim memory the live editor is using, or mark itself loaded.
            if (token == _loadSequence)
            {
                IsLoadingEditor = false;
                _loadedSave = summary;
                // The editor that was just dropped held a fully parsed save tree (the Facility
                // world is ~200 MB). The allocation rate after a switch is too low to trigger a
                // natural gen2 collection, so reclaim it - but only once per settled switch (the
                // debounce already collapsed rapid clicks into a single load).
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>One crafting bench a teleporter can sync to (XAML picker row).</summary>
public sealed record BenchOption(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One bed in the world, offered as a spawn-point target.</summary>
public sealed record BedOption(
    string Id, string Label, double X, double Y, double Z,
    ulong? OwnerSteamId = null, string? OwnerName = null)
{
    public override string ToString() => Label;
}

/// <summary>A dropped item within reach of the player (hoisted for XamlC).</summary>
public sealed record GroundItemOption(string WorldFile, Core.WorldSaves.WorldDroppedItem Item, double Distance)
{
    /// <summary>
    /// True for an item the user JUST dropped this session that is staged (not yet written to the
    /// world save). It shows in NEARBY GROUND ITEMS immediately; SAVE turns it into a real ground
    /// entry, REVERT drops it. Picking a staged one back up cancels the drop instead of removing a
    /// non-existent disk entry.
    /// </summary>
    public bool IsStaged { get; init; }

    public string DisplayName =>
        Services.GameDataServices.Catalog?.Find(Item.Slot.ItemId)?.DisplayName
        ?? Item.Slot.ItemId ?? "(unknown item)";

    /// <summary>"3.4 m away · ×2 · WorldSave_Facility".</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(3) { $"{Distance / 100:0.0} m away" };
            if (Item.Slot.Count > 1) parts.Add($"×{Item.Slot.Count}");
            parts.Add(Path.GetFileNameWithoutExtension(WorldFile));
            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>One sidebar group (grouped CollectionView source).</summary>
public sealed class SaveGroup : List<Core.Saves.SaveFileSummary>
{
    public SaveGroup(string title, IEnumerable<Core.Saves.SaveFileSummary> items) : base(items)
    {
        Title = title;
    }

    public string Title { get; }

    /// <summary>True for the PLAYERS group - the header shows an "add player" (+) button.</summary>
    public bool IsPlayers { get; init; }
}

/// <summary>Sidebar row for an ini file discovered near the save tree (hoisted for XamlC).</summary>
public sealed record ConfigFileOption(Core.Ini.AbioticIniFile File)
{
    public string Name => Path.GetFileName(File.FullPath);

    /// <summary>Disambiguates the per-world SandboxSettings.ini files.</summary>
    public string Detail => File.Kind switch
    {
        Core.Ini.AbioticIniKind.SandboxSettings =>
            Path.GetFileName(Path.GetDirectoryName(File.FullPath)) ?? string.Empty,
        _ => string.Empty,
    };

    public string KindChip => File.Kind switch
    {
        Core.Ini.AbioticIniKind.ServerAdmin => "ADMIN",
        Core.Ini.AbioticIniKind.SandboxSettings => "SANDBOX",
        Core.Ini.AbioticIniKind.ClientConfig => "CLIENT",
        _ => "INI",
    };
}

/// <summary>Row for a world found by machine-wide discovery (hoisted for XamlC).</summary>
public sealed record DiscoveredWorldOption(Core.Saves.DiscoveredWorld World)
{
    public string Name => World.WorldName;
    public string SourceLabel => World.SourceLabel;

    /// <summary>"Last played 2026-06-10 · 38 saves · account 7656...".</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(3);
            if (World.LastPlayed > DateTime.MinValue)
            {
                parts.Add($"Last played {World.LastPlayed:yyyy-MM-dd HH:mm}");
            }
            parts.Add($"{World.SaveFileCount} save file(s)");
            if (World.AccountId is { } account)
            {
                parts.Add($"account {account}");
            }
            return string.Join("  ·  ", parts);
        }
    }

    public string SourceHint => World.Source == Core.Saves.DiscoveredWorldSource.Client
        ? "Game client saves"
        : "Dedicated server install";

    public string FolderPath => World.FolderPath;
}
