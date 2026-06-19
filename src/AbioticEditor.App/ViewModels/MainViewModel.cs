using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        // operations (folder restore, asset mount) land in the log. Default OFF (opt-in): users
        // turn it on in Settings to capture a troubleshooting trace, and that choice persists.
        // Critical errors are written regardless of this switch (see EditorLog.Error).
        _diagnosticLoggingEnabled = Preferences.Default.Get(DiagnosticLoggingPreferenceKey, false);
        EditorLog.Enabled = _diagnosticLoggingEnabled;
        if (_diagnosticLoggingEnabled)
        {
            EditorLog.Info("App", $"Editor started. Log: {EditorLog.CurrentLogFilePath}");
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
            // The PLAYERS group stays visible (even when the world has no players yet) so its "+"
            // is reachable - but ONLY when the loaded folder actually has saves (a real world).
            // A folder with zero saves keeps no empty group, so the "no saves" empty view shows on
            // its own instead of an empty group header drawing on top of it.
            if (items.Count > 0 || (isPlayers && FolderPath is not null && Saves.Count > 0))
            {
                SaveGroups.Add(new SaveGroup(title, items) { IsPlayers = isPlayers });
            }
        }

        AddGroup(LocalizationResourceManager.Instance["Main_GroupWorldStory"], s => s.KindLabel == "META");
        AddGroup(LocalizationResourceManager.Instance["Main_GroupPlayers"], s => s.KindLabel == "PLAYER", isPlayers: true);
        AddGroup(LocalizationResourceManager.Instance["Main_GroupWorldRegions"], s => s.KindLabel == "WORLD");
        AddGroup(LocalizationResourceManager.Instance["Main_GroupOther"], s => s.KindLabel is not ("META" or "PLAYER" or "WORLD"));
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
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_AddPlayerTitle"],
                LocalizationResourceManager.Instance["Main_AddPlayerNeedFolder"]);
            return;
        }

        var selectedPlayer = _selectedSave is { KindLabel: "PLAYER" } sp ? sp : null;
        var anyPlayers = Saves.Any(s => s.KindLabel == "PLAYER");

        // Ask: new blank vs copy the selected player.
        int choice;
        if (selectedPlayer is not null)
        {
            choice = await DialogViewModel.Current.ShowAsync(
                LocalizationResourceManager.Instance["Main_AddPlayerHeader"],
                LocalizationResourceManager.Instance.Format("Main_AddPlayerCopyPrompt", selectedPlayer.DisplayName),
                (LocalizationResourceManager.Instance["Common_Cancel"], DialogTone.Neutral),
                (LocalizationResourceManager.Instance["Main_AddPlayerNewBlank"], DialogTone.Primary),
                (LocalizationResourceManager.Instance["Main_AddPlayerCopySelected"], DialogTone.Primary));
        }
        else
        {
            choice = await DialogViewModel.Current.ShowAsync(
                LocalizationResourceManager.Instance["Main_AddPlayerHeader"],
                anyPlayers
                    ? LocalizationResourceManager.Instance["Main_AddPlayerBlankTip"]
                    : LocalizationResourceManager.Instance["Main_AddPlayerBlank"],
                (LocalizationResourceManager.Instance["Common_Cancel"], DialogTone.Neutral),
                (LocalizationResourceManager.Instance["Main_AddPlayerNewBlank"], DialogTone.Primary));
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
                    Core.PlayerSaves.PlayerSaveIdentity.CloneToNewId(sourcePath, steamId));
            }
            else
            {
                var template = await LoadBlankPlayerTemplateAsync();
                Directory.CreateDirectory(playerDir);
                newPath = await Task.Run(() =>
                    Core.PlayerSaves.PlayerSaveFactory.CreateFromTemplate(template, playerDir, steamId));
            }

            EditorLog.Info("App", $"Created player {Path.GetFileName(newPath)} ({(copySelected ? "copied" : "blank")}).");

            if (FolderPath is { } folder)
            {
                await LoadFolderAsync(folder);
                SelectedSave = Saves.FirstOrDefault(s =>
                    string.Equals(s.FullPath, newPath, StringComparison.OrdinalIgnoreCase));
            }
            StatusMessage = copySelected
                ? LocalizationResourceManager.Instance.Format("Main_PlayerCopied", Path.GetFileName(newPath), steamId)
                : LocalizationResourceManager.Instance.Format("Main_PlayerCreatedBlank", Path.GetFileName(newPath), steamId);
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", "Add player failed", ex);
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_AddPlayerFailedTitle"], ex.Message);
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
    /// Prompts for the new player's owner id, re-prompting until it's a valid file token and
    /// not already used in <paramref name="playerDir"/>. On Steam this is a 17-digit SteamID64;
    /// for Game Pass / Epic it's the account folder id. Returns null when the user cancels.
    /// </summary>
    private static async Task<string?> PromptForNewSteamIdAsync(string playerDir)
    {
        var initial = "76561198";
        while (true)
        {
            var text = await DialogViewModel.Current.PromptAsync(
                LocalizationResourceManager.Instance["Main_NewPlayerIdTitle"],
                LocalizationResourceManager.Instance["Main_NewPlayerIdMessage"],
                placeholder: "76561198000000000",
                initialValue: initial,
                accept: LocalizationResourceManager.Instance["Main_CreateButton"],
                cancel: LocalizationResourceManager.Instance["Common_Cancel"]);
            if (text is null) return null;

            var trimmed = text.Trim();
            initial = trimmed;
            if (!Core.PlayerSaves.PlayerIdentifier.IsSafeFileToken(trimmed))
            {
                await DialogViewModel.Current.AlertAsync(
                    LocalizationResourceManager.Instance["Main_InvalidPlayerIdTitle"],
                    LocalizationResourceManager.Instance["Main_InvalidPlayerIdMessage"]);
                continue;
            }
            if (File.Exists(Path.Combine(playerDir, $"Player_{trimmed}.sav")))
            {
                await DialogViewModel.Current.AlertAsync(
                    LocalizationResourceManager.Instance["Main_AlreadyExistsTitle"],
                    LocalizationResourceManager.Instance.Format("Main_PlayerAlreadyExists", trimmed));
                continue;
            }
            return trimmed;
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

    /// <summary>"Logging to &lt;path&gt;" while enabled; off-state notes errors are still captured.</summary>
    public string LoggingStatusText => _diagnosticLoggingEnabled
        ? LocalizationResourceManager.Instance.Format("Main_LoggingTo", EditorLog.CurrentLogFilePath)
        : LocalizationResourceManager.Instance["Main_LoggingOff"];

    /// <summary>
    /// Opens the log folder in the OS file manager (creating it if no log was written
    /// yet). Mobile platforms have no user-facing file manager to launch, so they get
    /// the path on the status line instead.
    /// </summary>
    public System.Windows.Input.ICommand OpenLogFolderCommand => _openLogFolderCommand ??= new RelayCommand(() =>
    {
        try
        {
            EditorLog.Info("App", $"Open log folder: {EditorLog.LogDirectory}");
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
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_LogFolder", EditorLog.LogDirectory);
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_LogFolderOpenFailed", ex.Message);
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
                PickerTitle = LocalizationResourceManager.Instance["Main_PickMappingsTitle"],
                FileTypes = UsmapFileType,
            });
            if (pick is null) return; // user cancelled

            var installed = Core.Assets.GameAssetProvider.InstallUserMappings(pick.FullPath);
            EditorLog.Info("App", $"User imported mappings: {pick.FullPath} -> {installed}");
            StatusMessage = LocalizationResourceManager.Instance["Main_MappingsInstalledStatus"];
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_MappingsInstalledTitle"],
                LocalizationResourceManager.Instance.Format("Main_MappingsInstalledMessage", installed));
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", "Usmap import failed", ex);
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_MappingsImportFailed", ex.Message);
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
    private bool _leaveGateOpen;

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

    // Folder-wide device index (GUID -> deployable + the save file it lives in), built lazily from
    // every WorldSave_*.sav next to the current world save. Resolves a power socket whose plugged-in
    // device lives in another region save; cleared when the folder changes.
    private Dictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>? _deviceFolderIndex;

    /// <summary>
    /// True cross-world navigation for a power socket's plugged-in device: finds which region save
    /// holds the device, switches to it (respecting the leave-dirty-editor gate), and opens the
    /// device's container. Reports a clear status when the device is not a container or cannot be
    /// found in any of the world's saves.
    /// </summary>
    private async Task NavigateToWorldDeviceAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        try
        {
            StatusMessage = LocalizationResourceManager.Instance["Main_DeviceSearching"];
            var index = await EnsureDeviceFolderIndexAsync();
            if (index is null || !index.TryGetValue(deviceId, out var info))
            {
                StatusMessage = LocalizationResourceManager.Instance["Main_DeviceNotFound"];
                return;
            }
            if (!info.IsContainer)
            {
                var where = info.SourceFile is { } nf
                    ? LocalizationResourceManager.Instance.Format("Main_DeviceInFile", Path.GetFileName(nf))
                    : string.Empty;
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_DeviceNoInventory", info.FriendlyName, where);
                return;
            }

            // Already in the loaded save (shouldn't normally reach here, but open it directly).
            if (WorldEditor is { } here && info.SourceFile is { } f
                && string.Equals(here.FilePath, f, StringComparison.OrdinalIgnoreCase))
            {
                here.OpenContainerById(deviceId);
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_DeviceOpened", info.FriendlyName);
                return;
            }

            var target = info.SourceFile is { } path
                ? Saves.FirstOrDefault(s => string.Equals(s.FullPath, path, StringComparison.OrdinalIgnoreCase))
                : null;
            if (target is null)
            {
                var fileName = info.SourceFile is { } sf
                    ? Path.GetFileName(sf)
                    : LocalizationResourceManager.Instance["Main_AnotherSave"];
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_DeviceInOtherFolder", info.FriendlyName, fileName);
                return;
            }

            // Switch to the device's home save (the gate may keep the user on a dirty editor).
            _selectedSave = target;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSave)));
            await RequestSwitchAsync(target);

            if (ReferenceEquals(_loadedSave, target) && WorldEditor is { } we && we.OpenContainerById(deviceId))
            {
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_DeviceSwitchedAndOpened", Path.GetFileName(target.FullPath), info.FriendlyName);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_DeviceNavFailed", ex.Message);
        }
    }

    /// <summary>
    /// Resolves a set of plugged-in-device ids to their folder-wide descriptors (friendly name,
    /// container-ness, source save), so a power-socket tab can show real device names for devices
    /// that live in another region save. Returns only the ids it could resolve.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>>
        ResolveDevicesAsync(IReadOnlyList<string> ids)
    {
        var result = new Dictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>(
            StringComparer.OrdinalIgnoreCase);
        var index = await EnsureDeviceFolderIndexAsync();
        if (index is null)
        {
            return result;
        }
        foreach (var id in ids)
        {
            if (index.TryGetValue(id, out var info))
            {
                result[id] = info;
            }
        }
        return result;
    }

    private async Task<Dictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>?>
        EnsureDeviceFolderIndexAsync()
    {
        if (_deviceFolderIndex is not null)
        {
            return _deviceFolderIndex;
        }
        var current = _loadedSave?.FullPath ?? WorldEditor?.FilePath;
        var dir = current is not null ? Path.GetDirectoryName(current) : FolderPath;
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        _deviceFolderIndex = await Task.Run(() =>
        {
            Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
            var index = new Dictionary<string, Core.WorldSaves.PowerSocketDeviceResolver.DeviceInfo>(
                StringComparer.OrdinalIgnoreCase);
            int files = 0, failed = 0;
            // Load one save at a time and merge, so the large hub save isn't held in memory next to
            // every sibling (and a single unreadable save can't sink the whole index).
            foreach (var file in Directory.EnumerateFiles(dir, "WorldSave_*.sav"))
            {
                files++;
                try
                {
                    UeSaveGame.SaveGame save;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        save = UeSaveGame.SaveGame.LoadFrom(fs);
                    }
                    Core.WorldSaves.PowerSocketDeviceResolver.MergeSave(index, file, save);
                }
                catch (Exception ex)
                {
                    failed++;
                    EditorLog.Warn("CrossSave",
                        $"Device index: could not read {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            EditorLog.Info("CrossSave",
                $"Built device index from {files} world save(s) in {Path.GetFileName(dir)} "
                + $"({failed} unreadable): {index.Count} device(s).");
            return index;
        });
        return _deviceFolderIndex;
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

        // Reentrancy guard: GoHomeAsync, LoadFolderGuardedAsync, SwitchConfigFileAsync and
        // RequestSwitchAsync all reach this gate, and a second navigation arriving while the
        // dialog is up would stack a second dialog and could double-save. The first decision
        // wins; a concurrent navigation is treated as "stay" until it resolves.
        if (_leaveGateOpen) return false;
        _leaveGateOpen = true;
        try
        {
            // A phantom dialog (binding write-back posing as an edit) names its source
            // here - check the log when the gate appears without any deliberate change.
            if (PlayerEditor?.IsDirty == true)
            {
                EditorLog.Info("App", $"Leave-gate for {dirtyName}: {PlayerEditor.DescribeDirty()}");
            }

            // In-app dialog: [Cancel, Discard changes, Save and continue].
            var choice = await DialogViewModel.Current.ShowAsync(
                LocalizationResourceManager.Instance.Format("Main_UnsavedChangesTitle", dirtyName),
                LocalizationResourceManager.Instance["Main_UnsavedChangesMessage"],
                (LocalizationResourceManager.Instance["Common_Cancel"], DialogTone.Neutral),
                (LocalizationResourceManager.Instance["Main_DiscardChanges"], DialogTone.Danger),
                (LocalizationResourceManager.Instance["Main_SaveAndContinue"], DialogTone.Primary));

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
        finally
        {
            _leaveGateOpen = false;
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
        // Clear the loaded-save marker too: otherwise re-selecting the save we just closed hits
        // the ReferenceEquals(target, _loadedSave) short-circuit in RequestSwitchAsync and is a
        // no-op (the editor never reopens).
        _loadedSave = null;
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
        var playerId = pe.OwnerId;

        // Respawning at a bed requires owning its claim; a bed claimed by someone else
        // must be reassigned - which leaves that player without a claimed bed. That is
        // exactly the kind of edit nobody should make by accident.
        var claimedByOther = bed.OwnerId is { Length: > 0 } owner && owner != playerId;
        if (claimedByOther)
        {
            var ownerLabel = bed.OwnerName is { Length: > 0 } n ? n : bed.OwnerId!;
            var confirmed = await DialogViewModel.Current.ConfirmAsync(
                LocalizationResourceManager.Instance["Main_BedOtherPlayerTitle"],
                LocalizationResourceManager.Instance.Format("Main_BedOtherPlayerMessage", ownerLabel),
                LocalizationResourceManager.Instance["Main_ReassignBedButton"],
                LocalizationResourceManager.Instance["Common_Cancel"], DialogTone.Danger);
            if (!confirmed) return;
        }

        pe.SetRespawnLocation(bed.X, bed.Y, bed.Z + 90);
        EditorLog.Info("Spawn", $"Set spawn to bed: {bed.Label} ({bed.X:F0}, {bed.Y:F0}, {bed.Z:F0}) for player {playerId}");

        // Reassign / take the claim in the world save unless it is already ours.
        if (playerId.Length > 0 && bed.OwnerId != playerId)
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
                    EditorLog.Info("Spawn", $"Bed claim {(claimedByOther ? "reassigned" : "claimed")} for {claimName} (bed {bed.Id}) in WorldSave_Facility.sav");
                    // The cached deployables now lie about the claim; reload them.
                    _benchCache.Remove(facility);
                    await UpdateHomeBedAsync();
                    StatusMessage = claimedByOther
                        ? LocalizationResourceManager.Instance.Format("Main_BedReassigned", claimName)
                        : LocalizationResourceManager.Instance.Format("Main_BedClaimed", claimName);
                    return;
                }
                StatusMessage = LocalizationResourceManager.Instance["Main_BedClaimNotRewritten"];
                return;
            }
        }
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_SpawnMoved", bed.Label);
    }

    /// <summary>Display name to embed in a bed claim for this player.</summary>
    private string ResolvePlayerName(string ownerId)
    {
        var fromSidebar = Saves.FirstOrDefault(s =>
            s.PlayerName is not null && SteamPersonaIndex.IdFromPlayerPath(s.FullPath) == ownerId)?.PlayerName;
        if (fromSidebar is not null) return fromSidebar;
        return SteamPersonaIndex.LoadMachineAccounts().TryGetValue(ownerId, out var persona)
            ? persona
            : LocalizationResourceManager.Instance["Main_DefaultPlayerName"];
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

        var s = option.Item.Slot;

        // Pets are hotbar-only: route them to a free hotbar slot, never the backpack
        // (the game keeps pets in the hotbar / Companion slot, see EquipSlotTypes.IsHotbarOnly).
        var hotbarOnly = Core.Items.EquipSlotTypes.IsHotbarOnly(Services.GameDataServices.Catalog?.Find(s.ItemId));
        var target = hotbarOnly
            ? pe.Hotbar.FirstOrDefault(slot => slot.IsEmpty)
            : pe.Main.FirstOrDefault(slot => slot.IsEmpty);
        if (target is null)
        {
            StatusMessage = hotbarOnly
                ? LocalizationResourceManager.Instance["Main_NoEmptyHotbarSlot"]
                : LocalizationResourceManager.Instance["Main_NoEmptyBackpackSlot"];
            return;
        }

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
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_PickupDropCancelled", option.DisplayName, target.Index + 1);
            return;
        }

        pe.PendingGroundPickups.Add((option.WorldFile, option.Item.Id));
        NearbyGroundItems.Remove(option);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNearbyGroundItems)));
        EditorLog.Info("Pickup", $"Staged pick-up of {s.ItemId} ({option.Item.Id}) into slot {target.Index}.");
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_PickupStaged", option.DisplayName, target.Index + 1);
    }

    private async Task UpdateHomeBedAsync()
    {
        var ownerId = PlayerEditor?.OwnerId ?? string.Empty;
        if (ownerId.Length == 0)
        {
            HomeBedText = null;
            BedOptions.Clear();
            return;
        }

        var deployables = await LoadWorldDeployablesAsync();
        if (deployables is null)
        {
            HomeBedText = LocalizationResourceManager.Instance["Main_BedLookupUnavailable"];
            return;
        }

        var bases = Core.WorldSaves.BaseDetector.Detect(deployables);

        // Player beds, claimed-by-this-player first, as spawn targets. Pet beds are
        // claimable in the same map but a player can't respawn on one.
        BedOptions.Clear();
        BedOption? mine = null;
        foreach (var b in deployables.Where(d => d.IsBed && !d.IsPetBed)
                     .OrderByDescending(d => d.OwnerId == ownerId))
        {
            var home = bases.FirstOrDefault(bs => bs.Deployables.Contains(b));
            var option = new BedOption(b.Id,
                LocalizationResourceManager.Instance.Format("Main_BedLabel", b.DisplayName, home?.Name ?? LocalizationResourceManager.Instance["Main_OutsideAnyBase"],
                    b.X.ToString("F0", CultureInfo.CurrentCulture), b.Y.ToString("F0", CultureInfo.CurrentCulture)),
                b.X, b.Y, b.Z, b.OwnerId, b.OwnerName);
            BedOptions.Add(option);
            if (b.OwnerId == ownerId) mine ??= option;
        }
        SelectedBedOption = mine ?? BedOptions.FirstOrDefault();

        var bed = deployables.FirstOrDefault(d =>
            d.IsBed && d.OwnerId is { Length: > 0 } owner && owner == ownerId);
        if (bed is null)
        {
            HomeBedText = LocalizationResourceManager.Instance["Main_NoClaimedBed"];
            return;
        }

        var bedBase = bases.FirstOrDefault(b => b.Deployables.Contains(bed));
        HomeBedText = LocalizationResourceManager.Instance.Format("Main_BedLabel", bed.FriendlyClass, bedBase?.Name ?? LocalizationResourceManager.Instance["Main_OutsideAnyBase"],
                bed.X.ToString("F0", CultureInfo.CurrentCulture), bed.Y.ToString("F0", CultureInfo.CurrentCulture))
            + (bed.OwnerName is { } n
                ? LocalizationResourceManager.Instance.Format("Main_BedClaimedBy", n)
                : string.Empty);
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
        // Tear down the PREVIOUS ini view before building the next one. Swapping one IniEditor for
        // another in place keeps the ini view visible, so a still-focused Entry (e.g. an Engine.ini
        // HistoryBuffer field) is reused/half-rebuilt and the old file's textboxes linger. Nulling
        // it first hides the view (HasIniEditor -> false) so the focused Entry detaches cleanly, and
        // the next file builds a fresh list.
        IniEditor = null;
        // See GoHomeAsync: clear the loaded-save marker so returning to the previously open save
        // isn't swallowed by the ReferenceEquals(target, _loadedSave) short-circuit.
        _loadedSave = null;

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
                EditorLog.Info("Ini", $"Opened {next.Name} ({next.File.FullPath}); {IniEditor.Sections.Count} section(s).");
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_EditingIni", next.Name);
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
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_IniOpenFailed", next.Name, ex.Message);
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

    /// <summary>
    /// Opens a world that was just created by the New World wizard and selects its
    /// <c>WorldSave_MetaData.sav</c> so the editor lands on the world's story/metadata
    /// instead of an empty page. A Game Pass result (<paramref name="gamePass"/>) is a wgs
    /// folder routed through the extract/apply working-copy flow; a Steam result is a loose
    /// folder loaded directly. Runs the leave-gate first (a dirty editor may sit behind the
    /// modal that created the world).
    /// </summary>
    public async Task OpenCreatedWorldAsync(string path, bool gamePass)
    {
        if (!await ConfirmLeaveCurrentEditorAsync()) return;
        if (gamePass)
        {
            await OpenGamePassFolderAsync(path);
        }
        else
        {
            await LoadFolderAsync(path);
        }

        // Land on the metadata save (the only world save a brand-new world has).
        var meta = Saves.FirstOrDefault(s => s.KindLabel == "META")
            ?? Saves.FirstOrDefault(s => Path.GetFileName(s.FullPath)
                .StartsWith("WorldSave_MetaData", StringComparison.OrdinalIgnoreCase));
        if (meta is not null)
        {
            SelectedSave = meta;
        }
    }

    // ---------- Game Pass (Xbox container) saves ----------

    private sealed record GamePassSession(
        Core.GamePass.GamePassSaveSet Set, string Container, string WorldName, string WgsFolder, string WorkingDir);

    private GamePassSession? _gamePassSession;

    /// <summary>True while a Game Pass world is open as an extracted working copy.</summary>
    public bool IsGamePassSession => _gamePassSession is not null;

    /// <summary>Platform of the currently-loaded save: STEAM / GAME PASS / UNKNOWN (empty when none
    /// is loaded). Surfaced as a badge so the type is visible after a folder is opened, not only on
    /// the discovery list.</summary>
    public string CurrentSaveType
    {
        get
        {
            if (_gamePassSession is not null) return LocalizationResourceManager.Instance["Main_BadgeGamePass"];
            if (FolderPath is null) return string.Empty;
            if (FolderPath.Contains($"{Path.DirectorySeparatorChar}Packages{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return LocalizationResourceManager.Instance["Main_BadgeGamePass"];
            }
            var playerIds = Saves.Where(s => s.KindLabel == "PLAYER")
                .Select(s => SteamPersonaIndex.IdFromPlayerPath(s.FullPath))
                .Where(id => id is not null)
                .ToList();
            if (playerIds.Any(id => Core.PlayerSaves.PlayerIdentifier.IsSteamId(id))) return LocalizationResourceManager.Instance["Main_BadgeSteam"];
            if (Core.PlayerSaves.PlayerIdentifier.IsSteamId(AccountSegment(FolderPath))) return LocalizationResourceManager.Instance["Main_BadgeSteam"];
            return LocalizationResourceManager.Instance["Main_BadgeUnknown"];
        }
    }

    public bool HasCurrentSaveType => CurrentSaveType.Length > 0;

    /// <summary>The account folder segment of a client save path (…/SaveGames/&lt;account&gt;/Worlds/…).</summary>
    private static string? AccountSegment(string folder)
    {
        var parts = folder.Split('/', '\\');
        var i = Array.FindIndex(parts, p => p.Equals("SaveGames", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < parts.Length ? parts[i + 1] : null;
    }

    private void RaiseCurrentSaveTypeChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentSaveType)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCurrentSaveType)));
    }

    /// <summary>Opens a discovered world, routing Game Pass containers through the extract/apply flow.</summary>
    public async Task OpenDiscoveredWorldAsync(Core.Saves.DiscoveredWorld world)
    {
        if (world.IsGamePassContainer)
        {
            if (!await ConfirmLeaveCurrentEditorAsync()) return;
            await OpenGamePassContainerAsync(world.FolderPath, world.GamePassContainer!);
            return;
        }
        await LoadFolderGuardedAsync(world.FolderPath);
    }

    /// <summary>
    /// Opens a wgs folder picked/dropped directly (no discovery row). Finds its world container(s)
    /// and opens the first one through the working-copy flow. Assumes the leave-gate already passed.
    /// </summary>
    private async Task OpenGamePassFolderAsync(string wgsFolder)
    {
        string? container;
        try
        {
            var set = await Task.Run(() => Core.GamePass.GamePassSaveSet.Open(wgsFolder));
            container = set.Entries().Select(e => e.ContainerName).Distinct().FirstOrDefault();
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", $"Reading Game Pass folder {wgsFolder} failed", ex);
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_GpReadFailedTitle"], ex.Message);
            return;
        }
        if (container is null)
        {
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_GpNoWorldsTitle"],
                LocalizationResourceManager.Instance["Main_GpNoWorldsMessage"]);
            return;
        }
        await OpenGamePassContainerAsync(wgsFolder, container);
    }

    private async Task OpenGamePassContainerAsync(string wgsFolder, string container)
    {
        try
        {
            var (session, dir) = await Task.Run(() =>
            {
                var set = Core.GamePass.GamePassSaveSet.Open(wgsFolder);
                var working = Path.Combine(Path.GetTempPath(), "AbioticEditor", "GamePass",
                    $"{container}-{Guid.NewGuid():N}");
                var worldName = set.ExtractWorld(container, working);
                return (new GamePassSession(set, container, worldName, wgsFolder, working), working);
            });

            await LoadFolderAsync(dir);
            _gamePassSession = session;
            RaiseGamePassSessionChanged();
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_GpOpened", session.WorldName);
            EditorLog.Info("GamePass", $"Opened container '{session.Container}' -> working copy {dir}");
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", "Open Game Pass world failed", ex);
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_GpOpenFailedTitle"], ex.Message);
        }
    }

    /// <summary>
    /// After any editor save, a Game Pass working copy is packed straight back into its Xbox
    /// container - the SAVE button handles it, so there is no separate "apply" step or banner. The
    /// editor already wrote the .sav to the working folder; this re-reads the folder and rewrites
    /// the container blob (the wgs folder is backed up on the first write).
    /// </summary>
    private void OnEditorSaved()
    {
        if (_gamePassSession is null) return;
        _ = RepackGamePassAsync();
    }

    private async Task RepackGamePassAsync()
    {
        if (_gamePassSession is not { } session) return;
        try
        {
            await Task.Run(() => session.Set.ApplyWorld(session.Container, session.WorkingDir));
            EditorLog.Info("GamePass", $"Saved into Game Pass container '{session.Container}'.");
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_GpSaved", session.WorldName);
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", "Packing the save into the Game Pass container failed", ex);
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["Main_GpWriteFailedTitle"],
                LocalizationResourceManager.Instance.Format("Main_GpWriteFailedMessage", ex.Message));
        }
    }

    private void RaiseGamePassSessionChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGamePassSession)));
        RaiseCurrentSaveTypeChanged();
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
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_SlotSelected", slot.DisplayName, slot.Index);
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
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_SortedStacks", items.Count);
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
            StatusMessage = LocalizationResourceManager.Instance["Main_DropSelectFirst"];
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
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_Dropping", name);
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

            StatusMessage = LocalizationResourceManager.Instance.Format("Main_Dropped", name, Path.GetFileName(targetFile));
        }
        else
        {
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_DropRemovedNoTarget", name);
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
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_DismantleNoRecipe", slot.DisplayName);
            return;
        }

        var siblings = FindSlotCollection(slot);
        var empties = siblings.Where(s => s != slot && s.IsEmpty && !s.HasRole).ToList();
        var slotReusable = !slot.HasRole;
        var needed = recipe.IngredientList.Count - (slotReusable ? 1 : 0);
        if (empties.Count < needed)
        {
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_DismantleNeedsSlots", needed, empties.Count);
            return;
        }

        var catalog = GameDataServices.Catalog;
        var yield = string.Join("\n", recipe.IngredientList.Select(i =>
            $"  {i.Count}× {catalog?.Find(i.ItemId)?.DisplayName ?? i.ItemId}"));
        var destination = slot.HasRole
            ? LocalizationResourceManager.Instance["Main_DismantleDestEquipment"]
            : LocalizationResourceManager.Instance["Main_DismantleDestRegular"];
        DismantlePreviewText = LocalizationResourceManager.Instance.Format("Main_DismantlePreview", slot.DisplayName, yield, destination);
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
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_DismantleNeedsSlots", needed, empties.Count);
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
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_Dismantled", recipe.IngredientList.Count);
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
        EditorLog.Info("Teleporter", $"Teleporter synced to {_selectedBench.Label} (bench id {_selectedBench.Id})");
        UpdateTeleporterSyncText();
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_TeleporterSynced", _selectedBench.Label);
    });
    private RelayCommand? _syncTeleporterCommand;

    public System.Windows.Input.ICommand UnsyncTeleporterCommand => _unsyncTeleporterCommand ??= new RelayCommand(() =>
    {
        if (_activeSlot is null) return;
        _activeSlot.PlayerMadeString = string.Empty;
        EditorLog.Info("Teleporter", "Teleporter unsynced.");
        UpdateTeleporterSyncText();
        StatusMessage = LocalizationResourceManager.Instance["Main_TeleporterUnsynced"];
    });
    private RelayCommand? _unsyncTeleporterCommand;

    private async Task EnsureBenchOptionsAsync()
    {
        var deployables = await LoadWorldDeployablesAsync();
        BenchOptions.Clear();
        if (deployables is null)
        {
            TeleporterSyncText = LocalizationResourceManager.Instance["Main_BenchListUnavailable"];
            return;
        }

        // Name each bench by the base it anchors so the picker is human-readable.
        var bases = Core.WorldSaves.BaseDetector.Detect(deployables);
        foreach (var bench in deployables.Where(d => d.IsCraftingBench))
        {
            var home = bases.FirstOrDefault(b => b.Deployables.Contains(bench));
            var label = LocalizationResourceManager.Instance.Format("Main_BenchLabel", bench.DisplayName, home?.Name ?? LocalizationResourceManager.Instance["Main_Unclustered"],
                bench.X.ToString("F0", CultureInfo.CurrentCulture), bench.Y.ToString("F0", CultureInfo.CurrentCulture));
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
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
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
                if (SteamPersonaIndex.IdFromPlayerPath(s.FullPath) is not { } id) continue;
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

        TeleporterSyncText = LocalizationResourceManager.Instance["Main_LoadingBenches"];
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
        // A synced teleporter's PlayerMadeString is "<benchGuid>,<benchName>" (the name is the
        // bench's player-set label, which may be empty). Split it: match the GUID against this
        // world's benches first, otherwise fall back to the embedded name so a bench in another
        // region save still shows its friendly name instead of a raw GUID.
        var raw = _activeSlot?.PlayerMadeString;
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim(',', ' ').Length == 0)
        {
            TeleporterSyncText = LocalizationResourceManager.Instance["Main_NotSynced"];
            return;
        }
        var comma = raw.IndexOf(',');
        var guid = (comma >= 0 ? raw[..comma] : raw).Trim();
        var embeddedName = comma >= 0 ? raw[(comma + 1)..].Trim() : string.Empty;

        var match = BenchOptions.FirstOrDefault(b => string.Equals(b.Id, guid, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            TeleporterSyncText = LocalizationResourceManager.Instance.Format("Main_SyncedTo", match.Label);
        }
        else if (embeddedName.Length > 0)
        {
            TeleporterSyncText = LocalizationResourceManager.Instance.Format("Main_SyncedToOtherRegion", embeddedName);
        }
        else
        {
            var shortId = guid.Length > 8 ? guid[..8] : guid;
            TeleporterSyncText = LocalizationResourceManager.Instance.Format("Main_SyncedToMissing", shortId);
        }
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

        // Loading runs after the VM is constructed, so refresh the bindings that depend on the
        // outcome (the distinguishing empty-state text on inventory / recipes / lore).
        RaiseGameDataStatusChanged();

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

    /// <summary>
    /// Empty-state text for the lore / codex list: the ordinary "nothing matches your search"
    /// when the catalogs are loaded, otherwise the specific reason game data is missing (install
    /// not found vs Mappings.usmap missing) with the fix - so the user isn't left with a vague
    /// "unavailable". Bound by the codex tab's empty view.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string CodexEmptyMessage => GameDataServices.IsGameDataLoaded
        ? LocalizationResourceManager.Instance["PlayerCodex_NothingMatches"]
        : GameDataServices.StatusMessage;

    /// <summary>Empty-state text for the recipe browser - see <see cref="CodexEmptyMessage"/>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string RecipesEmptyMessage => GameDataServices.IsGameDataLoaded
        ? LocalizationResourceManager.Instance["PlayerRecipes_NoRecipesMatch"]
        : GameDataServices.StatusMessage;

    /// <summary>
    /// True when game data isn't loaded, so catalogs are empty or running on the built-in
    /// fallbacks (item ids without names/icons, the trader snapshot, etc.). Drives the global
    /// "game data not detected" banner so the editor never silently presents fallback data as if
    /// it were complete.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public bool GameDataMissing => !GameDataServices.IsGameDataLoaded;

    /// <summary>
    /// The reason game data is unavailable and how to fix it (install not found vs Mappings.usmap
    /// missing vs load failed), shown in the banner body. Reuses the same differentiated text as
    /// the recipe/codex empty states.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string GameDataNotice => GameDataServices.StatusMessage;

    /// <summary>
    /// Label for the banner's fix button, matched to <see cref="GameDataServices.Status"/>: import a
    /// usmap when the game was found but its data file is missing (locating the folder wouldn't
    /// help there), otherwise locate the install folder.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string GameDataActionText => GameDataServices.Status == GameDataStatus.MappingsMissing
        ? LocalizationResourceManager.Instance["GameData_ImportButton"]
        : LocalizationResourceManager.Instance["GameData_LocateButton"];

    private System.Windows.Input.ICommand? _fixGameDataCommand;

    /// <summary>
    /// Applies the fix that matches the current status (import a usmap, or locate the install
    /// folder) and reloads game data live - the in-place action on the banner. Settings and the
    /// first-run prompt run the same <see cref="Services.GameDataPrompt"/> flow. No-op if the
    /// picker is dismissed or the choice didn't resolve.
    /// </summary>
    public System.Windows.Input.ICommand FixGameDataCommand => _fixGameDataCommand ??= new RelayCommand(async () =>
    {
        if (!await Services.GameDataPrompt.FixAsync()) return;
        await ReloadGameDataAsync();
        await DialogViewModel.Current.AlertAsync(
            LocalizationResourceManager.Instance["Main_GameDataTitle"],
            GameDataServices.IsGameDataLoaded
                ? LocalizationResourceManager.Instance["Main_GameDataLoaded"]
                : GameDataServices.StatusMessage);
    });

    /// <summary>Refreshes every binding that depends on the game-data load outcome.</summary>
    private void RaiseGameDataStatusChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(CodexEmptyMessage), nameof(RecipesEmptyMessage), nameof(HasItemPalette),
                     nameof(GameDataMissing), nameof(GameDataNotice), nameof(GameDataActionText),
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Re-runs game-data loading against the current install setting (after the user points the
    /// editor at a folder, reverts to auto-detect, or imports a usmap) so the catalogs fill in or
    /// empty out without an app restart. Rebuilds the searchable item palette from the new
    /// catalog - it is otherwise only built once at startup, so the item picker would stay empty
    /// after a first successful LOCATE. The open save keeps its staged edits; reopening it is what
    /// refreshes its catalog-derived display (item names, icons, traders), which is why the
    /// Settings card asks the user to do so.
    /// </summary>
    public async Task ReloadGameDataAsync()
    {
        await GameDataServices.ReloadAsync();

        ItemPalette = GameDataServices.Catalog is { } catalog
            ? await Task.Run(() => new ItemPaletteViewModel(catalog))
            : null;

        RaiseGameDataStatusChanged();
    }

    private const string LastFolderPreferenceKey = "LastSaveFolder";

    /// <summary>
    /// Applies the testing/automation startup override: when <c>ABIOTIC_EDITOR_FOLDER</c> is
    /// set, that folder is loaded and (optionally) a save matching <c>ABIOTIC_EDITOR_AUTOSELECT</c>
    /// is selected. With no override this is a no-op: the app deliberately does NOT auto-open the
    /// previous session's world. The landing page shows the worlds detected on this machine
    /// (see <see cref="DiscoverWorldsAsync"/>) and lets the user decide what to open.
    /// </summary>
    public async Task ApplyStartupFolderOverrideAsync()
    {
        var folder = Environment.GetEnvironmentVariable("ABIOTIC_EDITOR_FOLDER");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        await LoadFolderAsync(folder);

        var autoSelect = Environment.GetEnvironmentVariable("ABIOTIC_EDITOR_AUTOSELECT");
        if (!string.IsNullOrEmpty(autoSelect))
        {
            SelectedSave = Saves.FirstOrDefault(s =>
                s.FullPath.EndsWith(autoSelect, StringComparison.OrdinalIgnoreCase));
        }

        // TEMP DIAGNOSTIC (remove): after the auto-selected editor settles, dump dirty state.
        if (Environment.GetEnvironmentVariable("ABIOTIC_DIAG_DIRTY") is { Length: > 0 } diagPath)
        {
            Core.Diagnostics.EditorLog.Enabled = true;
            var openSpawn = Environment.GetEnvironmentVariable("ABIOTIC_DIAG_OPENSPAWN") is { Length: > 0 };
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < 80; i++)
                {
                    await Task.Delay(500);
                    if (openSpawn && i == 6)
                    {
                        MainThread.BeginInvokeOnMainThread(() => PlayerEditor?.ShowSpawnCommand.Execute(null));
                    }
                    try
                    {
                        var line = $"[{i}] sel={(_selectedSave is null ? "-" : Path.GetFileName(_selectedSave.FullPath))} player={PlayerEditor is not null} dirty={PlayerEditor?.IsDirty} world={WorldEditor is not null} wdirty={WorldEditor?.IsDirty} :: {PlayerEditor?.DescribeDirty()}\n";
                        File.AppendAllText(diagPath, line);
                    }
                    catch { }
                }
            });
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
        if (PlayerEditor is not { } pe) return LocalizationResourceManager.Instance["Main_NoPlayerLoaded"];
        var newId = newIdText?.Trim() ?? string.Empty;
        if (!Core.PlayerSaves.PlayerIdentifier.IsSafeFileToken(newId))
        {
            return LocalizationResourceManager.Instance["Main_ChangeIdInvalid"];
        }

        var oldPath = pe.FilePath;
        var dir = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, $"Player_{newId}.sav");
        if (File.Exists(newPath))
            return LocalizationResourceManager.Instance.Format("Main_ChangeIdFileExists", newId);
        var oldId = SteamPersonaIndex.IdFromPlayerPath(oldPath);

        try
        {
            await Task.Run(() => Core.PlayerSaves.PlayerSaveIdentity.ChangeSteamId(oldPath, newId));
            EditorLog.Info("App", $"Re-homed player save {Path.GetFileName(oldPath)} -> {Path.GetFileName(newPath)} (SaveIdentifier rewritten)");
        }
        catch (Exception ex)
        {
            EditorLog.Error("App", $"Player id change failed for {oldPath}", ex);
            return LocalizationResourceManager.Instance.Format("Main_ChangeIdFailed", ex.Message);
        }

        // World data references the player too: bed claims embed the owner id. Player
        // saves live in PlayerData/ next to the world saves, so when those exist the
        // identity change follows into them and the player keeps their beds. A claim
        // rewrite is only safe when the old and new ids are the same length (always true
        // between two SteamID64s); a different-length swap is reported, not forced.
        var claims = 0;
        var claimsNote = string.Empty;
        if (oldId is { } from)
        {
            var worldDir = Path.GetDirectoryName(dir);
            if (worldDir is not null)
            {
                try
                {
                    claims = await Task.Run(() => WorldSteamIdPatcher.PatchFolder(worldDir, from, newId));
                    EditorLog.Info("CrossSave",
                        $"Player id change {from} -> {newId}: rewrote {claims} bed claim(s) across world saves in "
                        + $"{Path.GetFileName(worldDir)} (.bak kept per file)");
                }
                catch (InvalidOperationException ex)
                {
                    claimsNote = LocalizationResourceManager.Instance["Main_ChangeIdClaimsNotMigrated"];
                    EditorLog.Warn("CrossSave",
                        $"Bed-claim migration skipped for {from} -> {newId}: {ex.Message}");
                }
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
            ? LocalizationResourceManager.Instance.Format("Main_ChangeIdDoneWithClaims", newId, claims)
            : LocalizationResourceManager.Instance.Format("Main_ChangeIdDone", newId,
                claimsNote.Length > 0 ? claimsNote : LocalizationResourceManager.Instance["Main_ChangeIdNoClaimsFound"]);
        return null;
    }

    public async Task LoadFolderAsync(string folder)
    {
        // A Game Pass / Xbox container folder (one with containers.index) holds no loose .sav files;
        // open it through the extract/apply working-copy flow instead of scanning. This is the single
        // chokepoint every entry point (Open Folder, drag-drop, discovery, restore) funnels through.
        // The working copy is a normal loose-file folder, so the recursive load below won't re-enter.
        if (Core.GamePass.GamePassSaveSet.IsGamePassFolder(folder))
        {
            await OpenGamePassFolderAsync(folder);
            return;
        }

        // Any plain folder load leaves a Game Pass working-copy session (the GP open flow
        // re-establishes it immediately after calling this).
        if (_gamePassSession is not null)
        {
            _gamePassSession = null;
            RaiseGamePassSessionChanged();
        }
        FolderPath = folder;
        Preferences.Default.Set(LastFolderPreferenceKey, folder);
        EditorLog.ResetUnknownDedup();
        // Cross-folder state: drop cached world data and the progress gates so the new
        // folder's codex/recipe gating can't judge against the previous world's flags.
        _benchCache.Clear();
        _worldFlagCache.Clear();
        _deviceFolderIndex = null;
        Services.ProgressContext.WorldFlags = null;
        IniEditor = null;
        SelectedConfigFile = null;
        DiscoverConfigFiles(folder);
        Saves.Clear();
        SelectedSave = null;
        IsScanning = true;
        StatusMessage = LocalizationResourceManager.Instance.Format("Main_Scanning", folder);

        try
        {
            var results = await Task.Run(() => SaveFolderScanner.Scan(folder));
            foreach (var item in results)
            {
                Saves.Add(item);
            }
            RebuildSaveGroups();
            RaiseCurrentSaveTypeChanged();
            var failures = results.Count(r => r.LoadError is not null);
            StatusMessage = failures == 0
                ? LocalizationResourceManager.Instance.Format("Main_LoadedSaves", results.Count)
                : LocalizationResourceManager.Instance.Format("Main_LoadedSavesWithFailures", results.Count, failures);
            _ = EnrichPlayerNamesAsync(folder);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationResourceManager.Instance.Format("Main_ScanFailed", ex.Message);
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
    public Task ReloadSelectedSaveAsync()
    {
        if (_selectedSave is { } s)
        {
            EditorLog.Info("App", $"Reloading save from disk: {Path.GetFileName(s.FullPath)}");
        }
        return LoadEditorForAsync(_selectedSave);
    }

    private RelayCommand? _reloadSaveCommand;

    /// <summary>
    /// Bound to the RELOAD button. Re-reads the open save from disk after a confirm
    /// gate when the editor has unsaved edits. No-op while a load is in flight or when
    /// no save is selected.
    /// </summary>
    public System.Windows.Input.ICommand ReloadSaveCommand =>
        _reloadSaveCommand ??= new RelayCommand(async () => await ReloadSelectedSaveGuardedAsync());

    /// <summary>
    /// RELOAD entry point: discard the in-memory parse and re-read the open save from
    /// disk. Unlike REVERT (reset staged edits back to the parsed baseline) this picks up
    /// changes the game wrote since the file was opened. Confirms first only when the
    /// editor is dirty, since reloading throws those staged edits away.
    /// </summary>
    public async Task ReloadSelectedSaveGuardedAsync()
    {
        if (_selectedSave is null || IsLoadingEditor) return;

        var dirtyName =
            PlayerEditor?.IsDirty == true ? Path.GetFileName(PlayerEditor.FilePath)
            : WorldEditor?.IsDirty == true ? Path.GetFileName(WorldEditor.FilePath)
            : null;

        if (dirtyName is not null)
        {
            var confirmed = await DialogViewModel.Current.ConfirmAsync(
                LocalizationResourceManager.Instance.Format("Main_ReloadTitle", dirtyName),
                LocalizationResourceManager.Instance["Main_ReloadMessage"],
                LocalizationResourceManager.Instance["Main_DiscardAndReloadButton"],
                LocalizationResourceManager.Instance["Common_Cancel"], DialogTone.Danger);
            if (!confirmed) return;
            EditorLog.Info("App", $"User discarded staged changes to reload {dirtyName} from disk.");
        }

        await ReloadSelectedSaveAsync();
    }

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
        EditorLog.Info("Open", $"Opening {Path.GetFileName(summary.FullPath)} ({summary.KindLabel}).");
        try
        {
            await GameDataServices.EnsureLoadedAsync();

            if (summary.SaveClass == PlayerSaveClass)
            {
                var data = await Task.Run(() => PlayerSaveReader.ReadFromFile(summary.FullPath));
                if (token != _loadSequence) return;
                PlayerEditor = new PlayerEditorViewModel(data, summary.FullPath);
                PlayerEditor.Saved += OnEditorSaved;
            }
            else if (summary.SaveClass == WorldSaveClass || summary.SaveClass == WorldMetaSaveClass)
            {
                var data = await Task.Run(() => WorldSaveReader.ReadFromFile(summary.FullPath));
                if (token != _loadSequence) return;
                WorldEditor = new WorldEditorViewModel(
                    data, summary.FullPath, NavigateToWorldDeviceAsync, ResolveDevicesAsync);
                WorldEditor.Saved += OnEditorSaved;
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
                StatusMessage = LocalizationResourceManager.Instance.Format("Main_LoadSaveFailed", ex.Message);
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
    string? OwnerId = null, string? OwnerName = null)
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
        ?? Item.Slot.ItemId ?? LocalizationResourceManager.Instance["Main_UnknownItem"];

    /// <summary>"3.4 m away · ×2 · WorldSave_Facility".</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(3)
            {
                LocalizationResourceManager.Instance.Format("Main_MetersAway", (Distance / 100).ToString("0.0", CultureInfo.CurrentCulture)),
            };
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
        Core.Ini.AbioticIniKind.ServerAdmin => LocalizationResourceManager.Instance["Main_ChipAdmin"],
        Core.Ini.AbioticIniKind.SandboxSettings => LocalizationResourceManager.Instance["Main_ChipSandbox"],
        Core.Ini.AbioticIniKind.ClientConfig => LocalizationResourceManager.Instance["Main_ChipClient"],
        _ => LocalizationResourceManager.Instance["Main_ChipIni"],
    };
}

/// <summary>Row for a world found by machine-wide discovery (hoisted for XamlC).</summary>
public sealed record DiscoveredWorldOption(Core.Saves.DiscoveredWorld World)
{
    public string Name => World.WorldName;

    /// <summary>Platform tag shown on the discovery row: STEAM / GAME PASS / SERVER / UNKNOWN.</summary>
    public string SourceLabel => World.PlatformLabel;

    /// <summary>"Last played 2026-06-10 · 38 saves · account 7656...".</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(3);
            if (World.LastPlayed > DateTime.MinValue)
            {
                parts.Add(LocalizationResourceManager.Instance.Format("Main_LastPlayed", World.LastPlayed.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)));
            }
            parts.Add(LocalizationResourceManager.Instance.Format("Main_SaveFileCount", World.SaveFileCount));
            if (World.AccountId is { } account)
            {
                parts.Add(LocalizationResourceManager.Instance.Format("Main_Account", account));
            }
            // For a Game Pass container the folder isn't obvious (a GUID path under XboxGames or
            // Packages), so show where it lives.
            if (World.IsGamePassContainer)
            {
                parts.Add(World.FolderPath);
            }
            return string.Join("  ·  ", parts);
        }
    }

    public string SourceHint => World.IsGamePassContainer
        ? LocalizationResourceManager.Instance["Main_HintGamePass"]
        : World.Source == Core.Saves.DiscoveredWorldSource.Client
            ? LocalizationResourceManager.Instance["Main_HintClient"]
            : LocalizationResourceManager.Instance["Main_HintServer"];

    public string FolderPath => World.FolderPath;
}
