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

    public CreateWorldPage(MainViewModel vm)
    {
        _vm = vm;
        Title = "Create New World";
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
            "Steam - a loose-file world folder (WorldSave_*.sav + PlayerData/Player_*.sav) under the "
                + "Steam SaveGames tree. Players are owned by a 17-digit SteamID64.",
            "Game Pass / Microsoft Store - an Xbox \"wgs\" container (the same saves packed into one "
                + "Oodle-compressed bundle). Players are owned by an Xbox account id (XUID).",
        };

        var descLabel = new Label
        {
            Text = descriptions[(int)_platform],
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
        };

        var segmented = ModalChrome.Segmented(
            new[] { "STEAM", "GAME PASS" }, (int)_platform,
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

        var card = ModalChrome.Card("PLATFORM",
            "Which copy of the game is this world for? You can also add a copy for the other platform "
            + "on the final step.",
            segmented,
            descLabel);

        return BuildScaffold(
            step: 1,
            cards: new View[] { card },
            back: null,
            next: MakeNextBtn("NEXT: WORLD", () => Task.FromResult(true)));
    }

    private View BuildStepWorld()
    {
        var nameEntry = new Entry
        {
            Placeholder = "e.g. Cascade",
            Text = _worldName,
            MaxLength = 64,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        nameEntry.TextChanged += (_, e) => _worldName = e.NewTextValue?.Trim() ?? string.Empty;

        var locationEntry = new Entry
        {
            Placeholder = "Save folder path",
            Text = _saveLocation,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        locationEntry.TextChanged += (_, e) => _saveLocation = e.NewTextValue?.Trim() ?? string.Empty;

        var browseBtn = ModalChrome.Button("BROWSE", primary: false);
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

        var worldCard = ModalChrome.Card("WORLD SETUP",
            "Choose a name and the parent folder where the new world folder will be created.",
            LabelRow("World name", nameEntry),
            LabelRow("Save location", locationRow));

        return BuildScaffold(
            step: 2,
            cards: new View[] { worldCard },
            back: MakeBackBtn(() => { _step = 0; Rebuild(); }),
            next: MakeNextBtn("NEXT: PLAYERS", async () =>
            {
                if (string.IsNullOrWhiteSpace(_worldName))
                {
                    await AlertAsync("World name required", "Please enter a name for the new world folder.");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(_saveLocation) || !Directory.Exists(_saveLocation))
                {
                    await AlertAsync("Location not found",
                        "The save location folder does not exist. Please choose an existing folder.");
                    return false;
                }
                var target = Path.Combine(_saveLocation, _worldName);
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                {
                    await AlertAsync("Already exists",
                        $"A non-empty folder named '{_worldName}' already exists at that location. " +
                        "Choose a different name or location.");
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

        var accountWord = _platform == SavePlatform.GamePass ? "Xbox account id (XUID)" : "SteamID64";
        var playersCard = ModalChrome.Card("PLAYERS",
            $"Each player needs their own owner id. Pick a known account on this machine, or choose "
            + $"\"Custom id...\" to type a {accountWord} in. You can add more players later via the + "
            + "button in the sidebar.",
            new View[] { LabelRow("Number of players", countSegment), playerFieldsStack });

        return BuildScaffold(
            step: 3,
            cards: new View[] { playersCard },
            back: MakeBackBtn(() => { _step = 1; Rebuild(); }),
            next: MakeNextBtn("NEXT: DIFFICULTY", async () =>
            {
                for (var i = 0; i < _playerCount; i++)
                {
                    var id = _steamIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        await AlertAsync("Player id needed",
                            $"Player {i + 1}: pick an account or type a custom id before continuing.");
                        return false;
                    }
                    if (!Core.PlayerSaves.PlayerIdentifier.IsSafeFileToken(id))
                    {
                        await AlertAsync("That id can't be used",
                            $"Player {i + 1}: \"{id}\" can't name a save file. Use letters, digits, "
                            + "'-', '_' or '.' only (a SteamID64 looks like 76561198000000000).");
                        return false;
                    }
                }
                var ids = Enumerable.Range(0, _playerCount).Select(i => _steamIds[i]).ToList();
                if (ids.Distinct().Count() != ids.Count)
                {
                    await AlertAsync("Duplicate id", "Each player must have a unique owner id.");
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
        const string customOption = "Custom id...";
        var current = _steamIds[idx] ?? string.Empty;

        var pickerItems = _accountOptions.Select(o => o.Label).Append(customOption).ToList();
        var picker = new Picker
        {
            Title = "Choose an account",
            ItemsSource = pickerItems,
        };

        var customEntry = new Entry
        {
            Placeholder = _platform == SavePlatform.GamePass
                ? "Xbox account id (XUID)"
                : "SteamID64, e.g. 76561198000000000",
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
        return LabelRow($"Player {idx + 1}", stack);
    }

    private View BuildStepDifficulty()
    {
        var difficultyDescriptions = new[]
        {
            "Casual - reduced enemy difficulty, boosted XP and item stacks. Perfect for exploring.",
            "Normal - balanced challenge, the intended experience for most players.",
            "Survival - harder enemies, natural item stacks, higher spawn rate.",
            "Nightmare - maximum difficulty: reduced XP, heavy enemy damage, relentless spawns.",
        };

        var descLabel = new Label
        {
            Text = difficultyDescriptions[_difficulty - 1],
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
        };

        var segmented = ModalChrome.Segmented(
            new[] { "CASUAL", "NORMAL", "SURVIVAL", "NIGHTMARE" },
            _difficulty - 1,
            idx =>
            {
                _difficulty = idx + 1;
                descLabel.Text = difficultyDescriptions[idx];
            });

        var diffCard = ModalChrome.Card("DIFFICULTY",
            "Sets the GameDifficulty and related multipliers in SandboxSettings.ini. You can edit them in the Config tab after creation.",
            segmented,
            descLabel);

        return BuildScaffold(
            step: 4,
            cards: new View[] { diffCard },
            back: MakeBackBtn(() => { _step = 2; Rebuild(); }),
            next: MakeNextBtn("NEXT: REVIEW", () => Task.FromResult(true)));
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
            1 => "Casual",
            3 => "Survival",
            4 => "Nightmare",
            _ => "Normal",
        };

        var worldPath = Path.Combine(_saveLocation, _worldName);
        var primaryPlatform = _platform == SavePlatform.GamePass ? "Game Pass" : "Steam";
        var otherPlatform = _platform == SavePlatform.GamePass ? "Steam" : "Game Pass";

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

        AddRow("Platform", primaryPlatform);
        AddRow("World name", _worldName);
        AddRow("Location", worldPath);
        AddRow("Players", string.Join("\n", playerIds));
        AddRow("Difficulty", difficultyName);

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
            Text = $"Also write a {otherPlatform} copy next to it",
            Style = ModalChrome.St("AfFieldValue"),
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        otherRow.Add(otherSwitch, 1, 0);

        var statusLabel = new Label { IsVisible = false, Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        var createBtn = ModalChrome.Button("CREATE WORLD", primary: true);

        createBtn.Clicked += async (_, _) => await CreateAsync(createBtn, statusLabel);

        var reviewCard = ModalChrome.Card("REVIEW",
            "Check the details below, then press CREATE WORLD.",
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
        statusLabel.Text = "Creating world...";

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
                statusLabel.Text = "Writing Game Pass container...";
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
            var regionNote = "Only WorldSave_MetaData.sav exists yet - the game writes each "
                + "region/facility save the first time you load the world and visit that area.";

            string status;
            if (primaryIsGamePass && wgsDir is null)
            {
                status = $"Game Pass packing was skipped (see the log) - opened the Steam folder "
                    + $"\"{Path.GetFileName(worldDir)}\" instead. {regionNote}";
            }
            else if (openGamePass)
            {
                status = keepSteamFolder
                    ? $"Created the world for both platforms. Opened the Game Pass container. {regionNote}"
                    : $"Created the Game Pass world \"{_worldName}\". {regionNote}";
            }
            else
            {
                status = wgsDir is not null
                    ? $"Created the world for both platforms (Steam folder + Game Pass container next to it). {regionNote}"
                    : $"Created the Steam world \"{_worldName}\". {regionNote}";
            }

            statusLabel.Text = "World created. Loading...";
            await Navigation.PopModalAsync();
            await _vm.OpenCreatedWorldAsync(openPath, gamePass: openGamePass);
            _vm.StatusMessage = status;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Error("CreateWorld", "World creation failed", ex);
            statusLabel.Text = $"Failed: {ex.Message}";
            createBtn.IsEnabled = true;
        }
    }

    // ---------- scaffolding helpers ----------

    private View BuildScaffold(int step, IEnumerable<View> cards, View? back, View? next, View? extraFooter = null)
    {
        var progressLabel = new Label
        {
            Text = $"STEP {step} OF {StepCount}",
            Style = ModalChrome.St("AfFieldLabel"),
            TextColor = ModalChrome.Col("AfTextSecondary"),
            CharacterSpacing = 2,
            VerticalOptions = LayoutOptions.Center,
        };

        var cancel = ModalChrome.Button("CANCEL", primary: false);
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

        return ModalChrome.Scaffold("NEW WORLD", "Create World", cards, footer, maxWidth: 580);
    }

    private static Button MakeBackBtn(Action action)
    {
        var btn = ModalChrome.Button("BACK", primary: false);
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

    private Task AlertAsync(string title, string message)
        => ViewUtils.AlertAsync(this, title, message);

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

    private string DefaultSaveLocation()
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
