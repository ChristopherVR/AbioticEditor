using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Core.Steam;
using AbioticEditor.Core.WorldSaves;
using CommunityToolkit.Maui.Storage;

namespace AbioticEditor.App;

/// <summary>
/// Multi-step "Create New World" wizard modal. Four steps: world name + save location,
/// players, difficulty preset, and a final review/create screen. Built in code like
/// SettingsPage so it picks up the live palette every time it opens.
/// </summary>
public sealed class CreateWorldPage : ContentPage
{
    private readonly MainViewModel _vm;

    // Step state
    private int _step;
    private string _worldName = string.Empty;
    private string _saveLocation = string.Empty;
    private int _playerCount = 1;
    private readonly string[] _steamIds = new string[4];
    private int _difficulty = 2; // Normal

    // Known local Steam accounts for auto-fill hint
    private readonly IReadOnlyDictionary<ulong, string> _machineAccounts;

    // Lazily loaded templates
    private static byte[]? _metaTemplate;
    private static byte[]? _playerTemplate;

    public CreateWorldPage(MainViewModel vm)
    {
        _vm = vm;
        Title = "Create New World";
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        _machineAccounts = SteamPersonaIndex.LoadMachineAccounts();

        // Seed default save location from the first known Steam account.
        _saveLocation = DefaultSaveLocation();

        Rebuild();
    }

    // ---------- step rendering ----------

    private void Rebuild()
    {
        Content = _step switch
        {
            0 => BuildStepWorld(),
            1 => BuildStepPlayers(),
            2 => BuildStepDifficulty(),
            _ => BuildStepReview(),
        };
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
            step: 1, total: 4,
            cards: new View[] { worldCard },
            back: null,
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
        var playerFieldsStack = new VerticalStackLayout { Spacing = 10 };

        void RebuildPlayerFields()
        {
            playerFieldsStack.Children.Clear();
            for (var i = 0; i < _playerCount; i++)
            {
                var idx = i;
                var entry = new Entry
                {
                    Placeholder = "17-digit SteamID64",
                    Text = _steamIds[idx] ?? string.Empty,
                    Keyboard = Keyboard.Numeric,
                    MaxLength = 17,
                    ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
                };
                entry.TextChanged += (_, e) => _steamIds[idx] = e.NewTextValue?.Trim() ?? string.Empty;

                // Auto-fill button when there is exactly one local account and this is the first field.
                if (idx == 0 && _machineAccounts.Count == 1)
                {
                    var (autoId, autoName) = _machineAccounts.First();
                    var autoBtn = ModalChrome.Button($"USE {autoName.ToUpperInvariant()}", primary: false);
                    autoBtn.FontSize = 10;
                    autoBtn.Padding = new Thickness(10, 5);
                    autoBtn.Clicked += (_, _) =>
                    {
                        _steamIds[0] = autoId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        entry.Text = _steamIds[0];
                    };
                    var row = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                        },
                        ColumnSpacing = 8,
                    };
                    row.Add(entry, 0, 0);
                    row.Add(autoBtn, 1, 0);
                    playerFieldsStack.Children.Add(LabelRow($"Player {idx + 1}", row));
                }
                else
                {
                    playerFieldsStack.Children.Add(LabelRow($"Player {idx + 1}", entry));
                }
            }
        }

        var countSegment = ModalChrome.Segmented(
            new[] { "1", "2", "3", "4" }, _playerCount - 1,
            idx => { _playerCount = idx + 1; RebuildPlayerFields(); });

        RebuildPlayerFields();

        var bodyViews = new List<View> { LabelRow("Number of players", countSegment), playerFieldsStack };
        if (_machineAccounts.Count > 0)
        {
            var hints = string.Join(", ", _machineAccounts.Select(kv => $"{kv.Value} ({kv.Key})"));
            bodyViews.Add(new Label
            {
                Text = $"Accounts found on this machine: {hints}",
                Style = ModalChrome.St("AfMuted"),
                FontSize = 10,
            });
        }

        var playersCard = ModalChrome.Card("PLAYERS",
            "Each player needs their own 17-digit SteamID64. You can add more players later via the + button in the sidebar.",
            bodyViews.ToArray());

        return BuildScaffold(
            step: 2, total: 4,
            cards: new View[] { playersCard },
            back: MakeBackBtn(() => { _step = 0; Rebuild(); }),
            next: MakeNextBtn("NEXT: DIFFICULTY", async () =>
            {
                for (var i = 0; i < _playerCount; i++)
                {
                    var id = _steamIds[i];
                    if (string.IsNullOrWhiteSpace(id) || !ulong.TryParse(id, out _) || id.Length != 17)
                    {
                        await AlertAsync("Invalid SteamID",
                            $"Player {i + 1}: enter a valid 17-digit SteamID64 (e.g. 76561198000000000).");
                        return false;
                    }
                }
                var ids = Enumerable.Range(0, _playerCount).Select(i => _steamIds[i]).ToList();
                if (ids.Distinct().Count() != ids.Count)
                {
                    await AlertAsync("Duplicate SteamID", "Each player must have a unique SteamID64.");
                    return false;
                }
                return true;
            }));
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
            step: 3, total: 4,
            cards: new View[] { diffCard },
            back: MakeBackBtn(() => { _step = 1; Rebuild(); }),
            next: MakeNextBtn("NEXT: REVIEW", () => Task.FromResult(true)));
    }

    private View BuildStepReview()
    {
        var playerIds = Enumerable.Range(0, _playerCount)
            .Select(i =>
            {
                var id = _steamIds[i];
                if (ulong.TryParse(id, out var uid) && _machineAccounts.TryGetValue(uid, out var name))
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

        AddRow("World name", _worldName);
        AddRow("Location", worldPath);
        AddRow("Players", string.Join("\n", playerIds));
        AddRow("Difficulty", difficultyName);

        var statusLabel = new Label { IsVisible = false, Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        var createBtn = ModalChrome.Button("CREATE WORLD", primary: true);

        createBtn.Clicked += async (_, _) =>
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
                    PlayerSteamIds = Enumerable.Range(0, _playerCount)
                        .Select(i => ulong.Parse(_steamIds[i], System.Globalization.CultureInfo.InvariantCulture))
                        .ToList(),
                    GameDifficulty = _difficulty,
                };

                var worldDir = await Task.Run(() => WorldSaveFactory.CreateWorldFolder(options, meta, player));

                statusLabel.Text = "World created. Loading...";
                await Navigation.PopModalAsync();
                await _vm.LoadFolderGuardedAsync(worldDir);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.EditorLog.Error("CreateWorld", "World creation failed", ex);
                statusLabel.Text = $"Failed: {ex.Message}";
                createBtn.IsEnabled = true;
            }
        };

        var reviewCard = ModalChrome.Card("REVIEW",
            "Check the details below, then press CREATE WORLD.",
            rows,
            statusLabel);

        return BuildScaffold(
            step: 4, total: 4,
            cards: new View[] { reviewCard },
            back: MakeBackBtn(() => { _step = 2; Rebuild(); }),
            next: null,
            extraFooter: createBtn);
    }

    // ---------- scaffolding helpers ----------

    private View BuildScaffold(int step, int total, IEnumerable<View> cards, View? back, View? next, View? extraFooter = null)
    {
        var progressLabel = new Label
        {
            Text = $"STEP {step} OF {total}",
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
                firstId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Worlds");
            if (Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticFactor", "Saved", "SaveGames");
    }
}
