using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.Steam;
using AbioticEditor.Core.WorldSaves;
using CommunityToolkit.Maui.Storage;

namespace AbioticEditor.App;

/// <summary>
/// Multi-step "Create New World" wizard modal. Five steps: target platform (Steam or Game
/// Pass), world name + save location, players, difficulty preset, and a final review/create
/// screen. Built in code like SettingsPage so it picks up the live palette every time it opens.
/// </summary>
public sealed class CreateWorldPage : ContentPage
{
    private const int StepCount = 5;

    private enum SavePlatform { Steam, GamePass }

    private readonly MainViewModel _vm;

    // Step state
    private int _step;
    private SavePlatform _platform = SavePlatform.Steam;
    private bool _alsoOtherPlatform; // review-step toggle: also write the other platform's copy
    private string _worldName = string.Empty;
    private string _saveLocation = string.Empty;
    private int _playerCount = 1;
    private readonly string[] _steamIds = new string[4];
    private int _difficulty = 2; // Normal

    // Known local Steam accounts (SteamID64 string -> persona) for the account dropdown.
    private readonly IReadOnlyDictionary<string, string> _machineAccounts;

    // Account dropdown options for the current platform, recomputed when the platform changes.
    private List<(string Label, string Id)> _accountOptions = new();

    // Lazily loaded templates
    private static byte[]? _metaTemplate;
    private static byte[]? _playerTemplate;

    private static string L(string key) => Services.LocalizationResourceManager.Instance[key];

    public CreateWorldPage(MainViewModel vm)
    {
        _vm = vm;
        Title = L("CreateWorld_Title");
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        _machineAccounts = SteamPersonaIndex.LoadMachineAccounts();

        RefreshAccountOptions();
        // Seed default save location from the first known Steam account.
        _saveLocation = DefaultSaveLocation();

        Rebuild();
    }

    // ---------- step rendering ----------

    private void Rebuild()
    {
        Content = _step switch
        {
            0 => BuildStepPlatform(),
            1 => BuildStepWorld(),
            2 => BuildStepPlayers(),
            3 => BuildStepDifficulty(),
            _ => BuildStepReview(),
        };
    }

    private View BuildStepPlatform()
    {
        var descriptions = new[]
        {
            L("CreateWorld_PlatformSteamDesc"),
            L("CreateWorld_PlatformGamePassDesc"),
        };

        var descLabel = new Label
        {
            Text = descriptions[(int)_platform],
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
        };

        var segmented = ModalChrome.Segmented(
            new[] { L("CreateWorld_SegSteam"), L("CreateWorld_SegGamePass") }, (int)_platform,
            idx =>
            {
                var next = (SavePlatform)idx;
                if (next == _platform) return;
                _platform = next;
                descLabel.Text = descriptions[idx];
                // The account dropdown and the default save location both depend on the platform;
                // refresh them and clear any ids picked for the previous platform.
                RefreshAccountOptions();
                Array.Clear(_steamIds);
                _saveLocation = DefaultSaveLocation();
            });

        var card = ModalChrome.Card(L("CreateWorld_PlatformCardTitle"),
            L("CreateWorld_PlatformCardDesc"),
            segmented,
            descLabel);

        return BuildScaffold(
            step: 1,
            cards: new View[] { card },
            back: null,
            next: MakeNextBtn(L("CreateWorld_NextWorld"), () => Task.FromResult(true)));
    }

    private View BuildStepWorld()
    {
        var nameEntry = new Entry
        {
            Placeholder = L("CreateWorld_WorldNamePlaceholder"),
            Text = _worldName,
            MaxLength = 64,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        nameEntry.TextChanged += (_, e) => _worldName = e.NewTextValue?.Trim() ?? string.Empty;

        var locationEntry = new Entry
        {
            Placeholder = L("CreateWorld_SaveFolderPlaceholder"),
            Text = _saveLocation,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        locationEntry.TextChanged += (_, e) => _saveLocation = e.NewTextValue?.Trim() ?? string.Empty;

        var browseBtn = ModalChrome.Button(L("CreateWorld_Browse"), primary: false);
        browseBtn.Clicked += async (_, _) =>
        {
            try
            {
                var result = await FolderPicker.PickAsync(CancellationToken.None);
                if (result.IsSuccessful && result.Folder is { } folder)
                {
                    _saveLocation = folder.Path;
                    locationEntry.Text = _saveLocation;
                }
            }
            catch (OperationCanceledException) { }
        };

        var locationRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        locationRow.Add(locationEntry, 0, 0);
        locationRow.Add(browseBtn, 1, 0);

        var worldCard = ModalChrome.Card(L("CreateWorld_WorldCardTitle"),
            L("CreateWorld_WorldCardDesc"),
            LabelRow(L("CreateWorld_WorldNameLabel"), nameEntry),
            LabelRow(L("CreateWorld_SaveLocationLabel"), locationRow));

        return BuildScaffold(
            step: 2,
            cards: new View[] { worldCard },
            back: MakeBackBtn(() => { _step = 0; Rebuild(); }),
            next: MakeNextBtn(L("CreateWorld_NextPlayers"), async () =>
            {
                if (string.IsNullOrWhiteSpace(_worldName))
                {
                    await AlertAsync(L("CreateWorld_WorldNameRequiredTitle"), L("CreateWorld_WorldNameRequiredMsg"));
                    return false;
                }
                if (string.IsNullOrWhiteSpace(_saveLocation) || !Directory.Exists(_saveLocation))
                {
                    await AlertAsync(L("CreateWorld_LocationNotFoundTitle"),
                        L("CreateWorld_LocationNotFoundMsg"));
                    return false;
                }
                var target = Path.Combine(_saveLocation, _worldName);
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                {
                    await AlertAsync(L("CreateWorld_AlreadyExistsTitle"),
                        Services.LocalizationResourceManager.Instance.Format("CreateWorld_AlreadyExistsMsg", _worldName));
                    return false;
                }
                return true;
            }));
    }

    private View BuildStepPlayers()
    {
        // playerFieldsStack must be declared before countSegment so the lambda can capture it.
        var playerFieldsStack = new VerticalStackLayout { Spacing = 12 };

        void RebuildPlayerFields()
        {
            playerFieldsStack.Children.Clear();
            for (var i = 0; i < _playerCount; i++)
            {
                playerFieldsStack.Children.Add(BuildPlayerRow(i));
            }
        }

        var countSegment = ModalChrome.Segmented(
            new[] { "1", "2", "3", "4" }, _playerCount - 1,
            idx => { _playerCount = idx + 1; RebuildPlayerFields(); });

        RebuildPlayerFields();

        var accountWord = _platform == SavePlatform.GamePass
            ? L("CreateWorld_AccountWordXuid")
            : L("CreateWorld_AccountWordSteam");
        var playersCard = ModalChrome.Card(L("CreateWorld_PlayersCardTitle"),
            Services.LocalizationResourceManager.Instance.Format("CreateWorld_PlayersCardDesc", accountWord),
            new View[] { LabelRow(L("CreateWorld_NumberOfPlayers"), countSegment), playerFieldsStack });

        return BuildScaffold(
            step: 3,
            cards: new View[] { playersCard },
            back: MakeBackBtn(() => { _step = 1; Rebuild(); }),
            next: MakeNextBtn(L("CreateWorld_NextDifficulty"), async () =>
            {
                for (var i = 0; i < _playerCount; i++)
                {
                    var id = _steamIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        await AlertAsync(L("CreateWorld_PlayerIdNeededTitle"),
                            Services.LocalizationResourceManager.Instance.Format("CreateWorld_PlayerIdNeededMsg", i + 1));
                        return false;
                    }
                    if (!Core.PlayerSaves.PlayerIdentifier.IsSafeFileToken(id))
                    {
                        await AlertAsync(L("CreateWorld_IdNotUsableTitle"),
                            Services.LocalizationResourceManager.Instance.Format("CreateWorld_IdNotUsableMsg", i + 1, id));
                        return false;
                    }
                }
                var ids = Enumerable.Range(0, _playerCount).Select(i => _steamIds[i]).ToList();
                if (ids.Distinct().Count() != ids.Count)
                {
                    await AlertAsync(L("CreateWorld_DuplicateIdTitle"), L("CreateWorld_DuplicateIdMsg"));
                    return false;
                }
                return true;
            }));
    }

    /// <summary>
    /// One player's owner-id row: a dropdown of known accounts on this machine plus a
    /// "Custom id..." option that reveals a free-text field. Selecting a known account fills
    /// <see cref="_steamIds"/> directly, so the id is always valid; only the custom field can
    /// produce an invalid token, which the NEXT gate explains.
    /// </summary>
    private View BuildPlayerRow(int idx)
    {
        string customOption = L("CreateWorld_CustomIdOption");
        var current = _steamIds[idx] ?? string.Empty;

        var pickerItems = _accountOptions.Select(o => o.Label).Append(customOption).ToList();
        var picker = new Picker
        {
            Title = L("CreateWorld_ChooseAccount"),
            ItemsSource = pickerItems,
        };

        var customEntry = new Entry
        {
            Placeholder = _platform == SavePlatform.GamePass
                ? L("CreateWorld_CustomIdPlaceholderXuid")
                : L("CreateWorld_CustomIdPlaceholderSteam"),
            Text = current,
            MaxLength = 64,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            IsVisible = false,
        };
        customEntry.TextChanged += (_, e) => _steamIds[idx] = e.NewTextValue?.Trim() ?? string.Empty;

        // Decide the initial selection: a known account if the current id matches one, else
        // Custom when an id is already typed, else default to account idx (first run).
        var matchIndex = _accountOptions.FindIndex(o => string.Equals(o.Id, current, StringComparison.Ordinal));
        if (matchIndex < 0 && current.Length == 0 && idx < _accountOptions.Count)
        {
            matchIndex = idx;
            _steamIds[idx] = _accountOptions[idx].Id;
        }

        picker.SelectedIndexChanged += (_, _) =>
        {
            var sel = picker.SelectedIndex;
            if (sel >= 0 && sel < _accountOptions.Count)
            {
                _steamIds[idx] = _accountOptions[sel].Id;
                customEntry.IsVisible = false;
            }
            else if (sel == _accountOptions.Count) // "Custom id..."
            {
                customEntry.IsVisible = true;
                _steamIds[idx] = customEntry.Text?.Trim() ?? string.Empty;
            }
        };

        // Apply the computed initial selection (raises SelectedIndexChanged, wiring visibility).
        picker.SelectedIndex = matchIndex >= 0 ? matchIndex : _accountOptions.Count;

        var stack = new VerticalStackLayout { Spacing = 8, Children = { picker, customEntry } };
        return LabelRow(Services.LocalizationResourceManager.Instance.Format("CreateWorld_PlayerN", idx + 1), stack);
    }

    private View BuildStepDifficulty()
    {
        var difficultyDescriptions = new[]
        {
            L("CreateWorld_DiffCasualDesc"),
            L("CreateWorld_DiffNormalDesc"),
            L("CreateWorld_DiffSurvivalDesc"),
            L("CreateWorld_DiffNightmareDesc"),
        };

        var descLabel = new Label
        {
            Text = difficultyDescriptions[_difficulty - 1],
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
        };

        var segmented = ModalChrome.Segmented(
            new[]
            {
                L("CreateWorld_DiffCasual"),
                L("CreateWorld_DiffNormal"),
                L("CreateWorld_DiffSurvival"),
                L("CreateWorld_DiffNightmare"),
            },
            _difficulty - 1,
            idx =>
            {
                _difficulty = idx + 1;
                descLabel.Text = difficultyDescriptions[idx];
            });

        var diffCard = ModalChrome.Card(L("CreateWorld_DifficultyCardTitle"),
            L("CreateWorld_DifficultyCardDesc"),
            segmented,
            descLabel);

        return BuildScaffold(
            step: 4,
            cards: new View[] { diffCard },
            back: MakeBackBtn(() => { _step = 2; Rebuild(); }),
            next: MakeNextBtn(L("CreateWorld_NextReview"), () => Task.FromResult(true)));
    }

    private View BuildStepReview()
    {
        var playerIds = Enumerable.Range(0, _playerCount)
            .Select(i =>
            {
                var id = _steamIds[i];
                if (_machineAccounts.TryGetValue(id, out var name))
                    return $"{name} ({id})";
                return id;
            });

        var difficultyName = _difficulty switch
        {
            1 => L("CreateWorld_DiffCasualName"),
            3 => L("CreateWorld_DiffSurvivalName"),
            4 => L("CreateWorld_DiffNightmareName"),
            _ => L("CreateWorld_DiffNormalName"),
        };

        var worldPath = Path.Combine(_saveLocation, _worldName);
        var primaryPlatform = _platform == SavePlatform.GamePass ? L("CreateWorld_PlatformNameGamePass") : L("CreateWorld_PlatformNameSteam");
        var otherPlatform = _platform == SavePlatform.GamePass ? L("CreateWorld_PlatformNameSteam") : L("CreateWorld_PlatformNameGamePass");

        var rows = new VerticalStackLayout { Spacing = 8 };
        void AddRow(string label, string value)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                },
                ColumnSpacing = 8,
            };
            var lbl = new Label { Text = label, Style = ModalChrome.St("AfFieldLabel"), VerticalOptions = LayoutOptions.Center };
            var val = new Label { Text = value, Style = ModalChrome.St("AfFieldValue"), VerticalOptions = LayoutOptions.Center };
            grid.Add(lbl, 0, 0);
            grid.Add(val, 1, 0);
            rows.Children.Add(grid);
        }

        AddRow(L("CreateWorld_ReviewPlatform"), primaryPlatform);
        AddRow(L("CreateWorld_ReviewWorldName"), _worldName);
        AddRow(L("CreateWorld_ReviewLocation"), worldPath);
        AddRow(L("CreateWorld_ReviewPlayers"), string.Join("\n", playerIds));
        AddRow(L("CreateWorld_ReviewDifficulty"), difficultyName);

        // Optional: also write the other platform's copy next to the primary one.
        var otherSwitch = new Switch { IsToggled = _alsoOtherPlatform };
        otherSwitch.Toggled += (_, e) => _alsoOtherPlatform = e.Value;
        var otherRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        otherRow.Add(new Label
        {
            Text = Services.LocalizationResourceManager.Instance.Format("CreateWorld_AlsoWriteCopy", otherPlatform),
            Style = ModalChrome.St("AfFieldValue"),
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        otherRow.Add(otherSwitch, 1, 0);

        var statusLabel = new Label { IsVisible = false, Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        var createBtn = ModalChrome.Button(L("CreateWorld_CreateWorld"), primary: true);

        createBtn.Clicked += async (_, _) => await CreateAsync(createBtn, statusLabel);

        var reviewCard = ModalChrome.Card(L("CreateWorld_ReviewCardTitle"),
            L("CreateWorld_ReviewCardDesc"),
            rows,
            otherRow,
            statusLabel);

        return BuildScaffold(
            step: 5,
            cards: new View[] { reviewCard },
            back: MakeBackBtn(() => { _step = 3; Rebuild(); }),
            next: null,
            extraFooter: createBtn);
    }

    // ---------- create ----------

    private async Task CreateAsync(Button createBtn, Label statusLabel)
    {
        createBtn.IsEnabled = false;
        statusLabel.IsVisible = true;
        statusLabel.Text = L("CreateWorld_StatusCreating");

        try
        {
            var meta = await LoadMetaTemplateAsync();
            var player = await LoadPlayerTemplateAsync();

            var options = new CreateWorldOptions
            {
                WorldName = _worldName,
                ParentDirectory = _saveLocation,
                PlayerIds = Enumerable.Range(0, _playerCount).Select(i => _steamIds[i]).ToList(),
                GameDifficulty = _difficulty,
            };

            var primaryIsGamePass = _platform == SavePlatform.GamePass;
            var keepSteamFolder = !primaryIsGamePass || _alsoOtherPlatform;
            var wantGamePass = primaryIsGamePass || _alsoOtherPlatform;

            // The loose Steam folder is built first either way: it is the source the Game Pass
            // container is packed from, and the Steam output when one is wanted.
            var worldDir = await Task.Run(() => WorldSaveFactory.CreateWorldFolder(options, meta, player));

            // Pack a Game Pass (Xbox container) copy when requested. This needs the Oodle
            // compressor; if it isn't available the conversion throws and we fall back to Steam.
            string? wgsDir = null;
            if (wantGamePass)
            {
                statusLabel.Text = L("CreateWorld_StatusWritingContainer");
                try
                {
                    wgsDir = await Task.Run(() =>
                        GamePassConverter.SteamWorldToGamePass(worldDir, worldDir + "-GamePass", _worldName));
                }
                catch (Exception ex)
                {
                    Core.Diagnostics.EditorLog.Warn("CreateWorld", $"Game Pass copy skipped: {ex.Message}");
                }
            }

            // Game Pass only: drop the temporary loose folder once the container exists.
            if (primaryIsGamePass && !keepSteamFolder && wgsDir is not null)
            {
                try { Directory.Delete(worldDir, recursive: true); }
                catch (Exception ex) { Core.Diagnostics.EditorLog.Warn("CreateWorld", $"Could not remove temp Steam folder: {ex.Message}"); }
            }

            // Decide what to open and the note to show. A brand-new world only has
            // WorldSave_MetaData.sav; the game writes each region/facility save on first visit.
            var openGamePass = primaryIsGamePass && wgsDir is not null;
            var openPath = openGamePass ? wgsDir! : worldDir;
            var regionNote = L("CreateWorld_RegionNote");

            string status;
            if (primaryIsGamePass && wgsDir is null)
            {
                status = Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusGamePassSkipped", Path.GetFileName(worldDir), regionNote);
            }
            else if (openGamePass)
            {
                status = keepSteamFolder
                    ? Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusBothOpenedGamePass", regionNote)
                    : Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusGamePassCreated", _worldName, regionNote);
            }
            else
            {
                status = wgsDir is not null
                    ? Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusBothCreated", regionNote)
                    : Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusSteamCreated", _worldName, regionNote);
            }

            statusLabel.Text = L("CreateWorld_StatusLoading");
            await Navigation.PopModalAsync();
            await _vm.OpenCreatedWorldAsync(openPath, gamePass: openGamePass);
            _vm.StatusMessage = status;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("CreateWorld", "World creation failed", ex);
            statusLabel.Text = Services.LocalizationResourceManager.Instance.Format("CreateWorld_StatusFailed", ex.Message);
            createBtn.IsEnabled = true;
        }
    }

    // ---------- scaffolding helpers ----------

    private View BuildScaffold(int step, IEnumerable<View> cards, View? back, View? next, View? extraFooter = null)
    {
        var progressLabel = new Label
        {
            Text = Services.LocalizationResourceManager.Instance.Format("CreateWorld_StepProgress", step, StepCount),
            Style = ModalChrome.St("AfFieldLabel"),
            TextColor = ModalChrome.Col("AfTextSecondary"),
            CharacterSpacing = 2,
            VerticalOptions = LayoutOptions.Center,
        };

        var cancel = ModalChrome.Button(L("CreateWorld_Cancel"), primary: false);
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var footerItems = new List<View> { progressLabel, cancel };
        if (back is not null) footerItems.Add(back);
        if (next is not null) footerItems.Add(next);
        if (extraFooter is not null) footerItems.Add(extraFooter);

        var footerRow = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        foreach (var item in footerItems)
        {
            footerRow.Children.Add(item);
        }
        var footer = new Border { Style = ModalChrome.St("AfChrome"), Padding = new Thickness(20, 12), Content = footerRow };

        return ModalChrome.Scaffold(L("CreateWorld_Eyebrow"), L("CreateWorld_ScaffoldTitle"), cards, footer, maxWidth: 580);
    }

    private static Button MakeBackBtn(Action action)
    {
        var btn = ModalChrome.Button(L("CreateWorld_Back"), primary: false);
        btn.Clicked += (_, _) => action();
        return btn;
    }

    private Button MakeNextBtn(string text, Func<Task<bool>> validate)
    {
        var btn = ModalChrome.Button(text, primary: true);
        btn.Clicked += async (_, _) =>
        {
            if (!await validate()) return;
            _step++;
            Rebuild();
        };
        return btn;
    }

    // The in-app DialogHostView overlay lives on MainPage, behind this modal, so its alerts
    // would render hidden underneath. Use the page's native alert, which draws on top of the modal.
    private Task AlertAsync(string title, string message)
        => DisplayAlertAsync(title, message, "OK");

    // ---------- account options ----------

    /// <summary>
    /// Rebuilds the account dropdown for the current platform: Steam personas from
    /// <c>loginusers.vdf</c>, or Xbox account ids (XUIDs) discovered from existing Game Pass
    /// saves on this machine.
    /// </summary>
    private void RefreshAccountOptions()
    {
        _accountOptions = _platform == SavePlatform.GamePass
            ? GamePassDiscovery.DiscoverAll()
                .Select(s => (Label: $"{s.AccountId} (Game Pass)", Id: s.AccountId))
                .DistinctBy(o => o.Id)
                .ToList()
            : _machineAccounts
                .Select(kv => (Label: $"{kv.Value} ({kv.Key})", Id: kv.Key))
                .ToList();
    }

    // ---------- template loaders ----------

    private static async Task<byte[]> LoadMetaTemplateAsync()
    {
        if (_metaTemplate is not null) return _metaTemplate;
        using var stream = await FileSystem.OpenAppPackageFileAsync("blank-world-template.sav");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return _metaTemplate = ms.ToArray();
    }

    private static async Task<byte[]> LoadPlayerTemplateAsync()
    {
        if (_playerTemplate is not null) return _playerTemplate;
        using var stream = await FileSystem.OpenAppPackageFileAsync("blank-player-template.sav");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return _playerTemplate = ms.ToArray();
    }

    // ---------- layout helpers ----------

    private static View LabelRow(string label, View control)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(120)),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
        };
        grid.Add(new Label
        {
            Text = label,
            Style = ModalChrome.St("AfFieldLabel"),
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        grid.Add(control, 1, 0);
        return grid;
    }

    // ---------- default save location ----------

    /// <summary>
    /// The default parent folder for the new world, per platform: the Game Pass container-store
    /// area for a Game Pass world, or the Steam SaveGames tree for a Steam world. The two
    /// platforms keep their saves in different places, so the default follows the chosen one.
    /// </summary>
    private string DefaultSaveLocation()
        => _platform == SavePlatform.GamePass ? DefaultGamePassLocation() : DefaultSteamLocation();

    private static string DefaultGamePassLocation()
    {
        // Prefer this machine's Game Pass container-store (wgs) area so the new world lands in the
        // platform's own save location; fall back to Documents when no Game Pass install is found.
        var root = GamePassDiscovery.ContainerStoreRoots().FirstOrDefault(Directory.Exists);
        return root ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string DefaultSteamLocation()
    {
        // Try the first known Steam account's worlds directory.
        if (_machineAccounts.Count > 0)
        {
            var firstId = _machineAccounts.Keys.First();
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AbioticFactor", "Saved", "SaveGames",
                firstId, "Worlds");
            if (Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticFactor", "Saved", "SaveGames");
    }
}
