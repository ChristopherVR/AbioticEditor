using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.App;

/// <summary>
/// Modal settings sheet: theme accent (facility blue / hazard orange), light or dark
/// mode, diagnostic logging, log folder and usmap import. Built in code so it picks up
/// the freshly applied palette every time it opens.
/// </summary>
public sealed class SettingsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly Func<Task> _rebuildHost;
    private bool _themeChanged;
    private bool _spoilerChanged;

    // The language code recorded when we last (re)built the sheet. LanguagePage applies the
    // change live and has no callback, so we compare against this on Appearing (fires when the
    // chooser modal pops back to us) to log the change and refresh the read-only label.
    private string _languageCode = Services.LocalizationService.CurrentCode;
    private Label? _languageValueLabel;

    public SettingsPage(MainViewModel vm, Func<Task> rebuildHost)
    {
        _vm = vm;
        _rebuildHost = rebuildHost;
        Title = "Settings";
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        Content = BuildContent();
        Appearing += OnAppearing;
    }

    /// <summary>
    /// Re-reads the current language when this sheet reappears (e.g. after the LanguagePage
    /// chooser pops). LanguagePage applies the language live with no callback, so this is where
    /// we notice the change, log it, and refresh the read-only language label.
    /// </summary>
    private void OnAppearing(object? sender, EventArgs e)
    {
        var current = Services.LocalizationService.CurrentCode;
        if (current != _languageCode)
        {
            EditorLog.Info("Settings", $"Language changed from {_languageCode} to {current}");
            _languageCode = current;
        }
        if (_languageValueLabel is not null)
        {
            _languageValueLabel.Text = CurrentLanguageName();
        }
    }

    /// <summary>Native display name for the current language (falls back to the raw code).</summary>
    private static string CurrentLanguageName()
        => Services.LocalizationService.Available
            .FirstOrDefault(l => l.Code == Services.LocalizationService.CurrentCode)?.NativeName
            ?? Services.LocalizationService.CurrentCode;

    private View BuildContent()
    {
        // ----- THEME -----
        var cascadeButton = ThemeButton("FACILITY BLUE", ThemeAccent.Cascade);
        var hazardButton = ThemeButton("HAZARD ORANGE", ThemeAccent.Hazard);

        var modeSwitch = new Switch { IsToggled = ThemeService.IsLight };
        modeSwitch.Toggled += (_, e) => ApplyTheme(ThemeService.Accent, e.Value);

        var themeCard = ModalChrome.Card("THEME",
            "Accent family for the editor chrome. Applies immediately; the editor view reloads.",
            new HorizontalStackLayout { Spacing = 10, Children = { cascadeButton, hazardButton } },
            LabeledRow("Light mode", modeSwitch));

        // ----- LANGUAGE -----
        var loc = Services.LocalizationResourceManager.Instance;
        // Current language is read-only; a separate CHANGE button opens the chooser. OnAppearing
        // re-reads CurrentCode when the chooser pops, so this label stays in sync.
        _languageValueLabel = new Label
        {
            Text = CurrentLanguageName(),
            Style = ModalChrome.St("AfFieldValue"),
            VerticalOptions = LayoutOptions.Center,
        };
        var changeLanguageButton = ModalChrome.Button("CHANGE", primary: false);
        changeLanguageButton.Clicked += async (_, _) => await Navigation.PushModalAsync(new LanguagePage());
        var languageRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        languageRow.Add(_languageValueLabel, 0, 0);
        languageRow.Add(changeLanguageButton, 1, 0);
        var languageCard = ModalChrome.Card(loc["Settings_Language"], loc["Settings_Language_Hint"],
            languageRow);

        // ----- DIAGNOSTICS -----
        var logSwitch = new Switch { IsToggled = _vm.DiagnosticLoggingEnabled };
        logSwitch.Toggled += (_, e) => _vm.DiagnosticLoggingEnabled = e.Value;
        var openLogs = new Button { Text = "OPEN LOG FOLDER", Command = _vm.OpenLogFolderCommand };

        var diagnosticsCard = ModalChrome.Card("DIAGNOSTICS",
            "Logs save loads/writes, staged edits, JSON transfers and unrecognized save content. Off by default.",
            LabeledRow("Diagnostic logging", logSwitch),
            new HorizontalStackLayout { Spacing = 10, Children = { openLogs } });

        // ----- SPOILERS -----
        var spoilerSwitch = new Switch { IsToggled = SpoilerService.Enabled };
        var resealButton = ModalChrome.Button("RE-SEAL REVEALED ITEMS", primary: false);
        var revealedHint = new Label { Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        void RefreshReveals()
            => revealedHint.Text = SpoilerService.RevealedCount == 0
                ? "Nothing has been individually revealed yet."
                : $"{SpoilerService.RevealedCount} item(s) revealed (clearance overridden).";
        RefreshReveals();
        spoilerSwitch.Toggled += (_, e) =>
        {
            if (e.Value == SpoilerService.Enabled) return;
            SpoilerService.Enabled = e.Value;
            _spoilerChanged = true;
            EditorLog.Info("Settings", $"Spoilers {(e.Value ? "shown" : "hidden")}");
        };
        resealButton.Clicked += (_, _) => { SpoilerService.ResetReveals(); RefreshReveals(); _spoilerChanged = true; };

        var spoilerCard = ModalChrome.Card("SPOILERS",
            "Seals content you haven't reached yet (future quest flags, traders, recipes, hidden achievements, codex entries, contained anomalies) behind a CLASSIFIED stamp. Tap any sealed item to override clearance and reveal it - revealed items stay visible from then on.",
            LabeledRow("Spoiler protection", spoilerSwitch),
            revealedHint,
            new HorizontalStackLayout { Spacing = 10, Children = { resealButton } });

        // ----- GAME DATA -----
        var gameDataCard = BuildGameDataCard();

        // ----- SAVE CONVERSION (Steam <-> Game Pass) -----
        var conversionCard = BuildSaveConversionCard();

        // ----- PLUGINS -----
        var managePlugins = new Button { Text = "MANAGE PLUGINS" };
        managePlugins.Clicked += async (_, _) =>
            await Navigation.PushModalAsync(new PluginsPage(_vm, _rebuildHost));
        var pluginDescriptors = Services.PluginService.Descriptors;
        var pluginCount = pluginDescriptors.Count;

        var pluginToggles = new VerticalStackLayout { Spacing = 6 };
        foreach (var d in pluginDescriptors)
        {
            pluginToggles.Children.Add(PluginRow(d));
        }

        var pluginsCard = ModalChrome.Card("PLUGINS",
            $"Extend the editor with save operations, commands, panels, menu actions, and event "
                + $"hooks (managed or JavaScript). {pluginCount} plugin(s) installed. Toggle one off to stop "
                + "loading it (restart to apply). Plugins run with full trust - only install plugins you trust.",
            pluginToggles,
            new HorizontalStackLayout { Spacing = 10, Children = { managePlugins } });

        // ----- UPDATES -----
        var updatesCard = BuildUpdatesCard();

        // ----- tabs (one tab at a time instead of one long scroll) -----
        var tabs = new (string Label, View[] Cards)[]
        {
            ("GENERAL", new View[] { themeCard, languageCard, diagnosticsCard }),
            ("EDITOR", new View[] { spoilerCard }),
            ("GAME DATA", new View[] { gameDataCard }),
            ("CONVERT", new View[] { conversionCard }),
            ("PLUGINS", new View[] { pluginsCard }),
            ("UPDATES", new View[] { updatesCard }),
        };

        // Cards for the selected tab are swapped into this container; only the active tab's
        // cards are mounted, so the sheet shows one short section instead of everything stacked.
        var tabContent = new VerticalStackLayout { Spacing = 14 };
        void ShowTab(int index)
        {
            tabContent.Children.Clear();
            foreach (var card in tabs[index].Cards)
            {
                tabContent.Children.Add(card);
            }
        }
        // Full-width strip, pinned above the scroll area so it stays visible as cards scroll.
        var tabBar = ModalChrome.Segmented(tabs.Select(t => t.Label).ToList(), 0, ShowTab, fill: true);
        ShowTab(0);

        var close = ModalChrome.Button("CLOSE", primary: false);
        close.Clicked += async (_, _) => await CloseAsync();

        return ModalChrome.Scaffold(
            "EDITOR CONFIGURATION", "Settings",
            new View[] { tabContent },
            ModalChrome.Footer(close),
            maxWidth: 620,
            pinnedHeader: tabBar);
    }

    /// <summary>
    /// The GAME DATA card: shows whether the installed game's data loaded, lets the user point
    /// the editor at a non-Steam / moved install (Game Pass, Epic, a library Steam can't
    /// enumerate), revert to auto-detection, and import a newer usmap. The trader roster and
    /// the flags gating each trade come entirely from this game data, so this card is what
    /// unblocks an empty TRADERS tab. Picking a folder reloads game data live (the open save
    /// must be reopened to refresh its trader/icon views).
    /// </summary>
    private View BuildGameDataCard()
    {
        var status = new Label { Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        var folderValue = new Label
        {
            Style = ModalChrome.St("AfFieldValue"),
            FontSize = 12,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        };
        var usmapValue = new Label
        {
            Style = ModalChrome.St("AfFieldValue"),
            FontSize = 12,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        };
        var locateFolder = new Button { Text = "SET GAME FOLDER" };
        var autoDetect = ModalChrome.Button("USE AUTO-DETECT", primary: false);

        void Refresh()
        {
            var custom = Services.GameDataServices.CustomInstallPath;
            status.Text = Services.GameDataServices.IsGameDataLoaded
                ? (custom is null ? "Loaded from your auto-detected game install." : "Loaded from the folder you set.")
                : Services.GameDataServices.StatusMessage;

            // The folder actually in use: the one you set, else the auto-detected install.
            var folder = custom
                ?? AbioticEditor.Core.Assets.AfInstallLocator.FindInstallRoot()
                ?? AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory();
            folderValue.Text = "Game folder:  " + (folder ?? "(not found - set it below)");

            // The data file (.usmap) that lets the editor read the game's tables.
            var usmap = AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings();
            usmapValue.Text = "Game data (.usmap):  " + (usmap is null
                ? "(none found - the game's tables can't be read)"
                : System.IO.Path.GetFileName(usmap)
                    + (usmap == AbioticEditor.Core.Assets.GameAssetProvider.UserMappingsPath ? "  [imported]" : "  [bundled]"));

            autoDetect.IsVisible = custom is not null;
        }

        Refresh();

        // Reload game data in place after a folder/auto-detect change so the catalogs fill in
        // (or empty out) without relaunching. Already-open editor views cache their catalog-
        // derived state, so we ask the user to reopen the save to see traders/icons refresh.
        async Task ApplyAndReloadAsync(string okTitle)
        {
            // Goes through the view-model so the searchable item palette is rebuilt from the new
            // catalog too - GameDataServices.ReloadAsync alone leaves the picker on stale data.
            await _vm.ReloadGameDataAsync();
            Refresh();
            await ViewUtils.AlertAsync(this, okTitle,
                Services.GameDataServices.IsGameDataLoaded
                    ? "Game data loaded. Reopen your save to refresh traders and item icons."
                    : Services.GameDataServices.StatusMessage);
        }

        locateFolder.Clicked += async (_, _) =>
        {
            try
            {
                var result = await CommunityToolkit.Maui.Storage.FolderPicker.PickAsync(CancellationToken.None);
                if (!result.IsSuccessful || result.Folder is null)
                {
                    return; // dismissed
                }
                var picked = result.Folder.Path;
                var paks = AbioticEditor.Core.Assets.AfInstallLocator.ResolvePaksDirectory(picked);
                if (paks is null)
                {
                    await ViewUtils.AlertAsync(this, "No game data there",
                        $"Couldn't find Abiotic Factor's pak files under:\n{picked}\n\n"
                            + "Pick the game's install folder (the one containing the AbioticFactor "
                            + "folder), its AbioticFactor subfolder, or the Content\\Paks folder.");
                    return;
                }
                Services.GameDataServices.CustomInstallPath = picked;
                EditorLog.Info("Settings", $"Game install folder set to {picked} (paks: {paks})");
                await ApplyAndReloadAsync("Game folder set");
            }
            catch (Exception ex) when (!IsPickerCancellation(ex))
            {
                EditorLog.Error("Settings", "Game folder pick failed", ex);
                await ViewUtils.AlertAsync(this, "Folder picker failed", ex.Message);
            }
        };

        autoDetect.Clicked += async (_, _) =>
        {
            Services.GameDataServices.CustomInstallPath = null;
            EditorLog.Info("Settings", "Game install folder reset to auto-detect");
            await ApplyAndReloadAsync("Auto-detect restored");
        };

        var importUsmap = new Button { Text = "IMPORT DATA FILE", Command = _vm.ImportMappingsCommand };

        return ModalChrome.Card("GAME DATA",
            "Traders, item icons and recipes come from your installed copy of the game, not from the "
                + "save. Auto-detection finds Steam installs; for a Game Pass, Epic, or moved install, "
                + "set your game folder here. If a game update stops the data from loading, import an "
                + "updated data file (.usmap).",
            status,
            folderValue,
            usmapValue,
            new HorizontalStackLayout { Spacing = 10, Children = { locateFolder, autoDetect } },
            new HorizontalStackLayout { Spacing = 10, Children = { importUsmap } });
    }

    /// <summary>
    /// The SAVE CONVERSION card: convert a save between Steam (loose files) and Game Pass (Xbox
    /// container) packaging. Each direction picks a source folder, writes the converted copy next to
    /// it, and never touches the original. The content is identical either way; only the packaging
    /// differs.
    /// </summary>
    private static View BuildSaveConversionCard()
    {
        // Optional account id: re-homes the (single) player save to this id during conversion.
        var idEntry = new Entry
        {
            Placeholder = "leave blank to keep existing ids",
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            Keyboard = Keyboard.Plain,
        };
        var idHint = new Label
        {
            Text = "Optional. A 17-digit SteamID64 (Steam) or account folder id (Game Pass). The player "
                + "save is re-homed to it so the world is yours on the target platform. Single-player only.",
            Style = ModalChrome.St("AfMuted"),
            FontSize = 10,
        };

        var toGamePass = ModalChrome.Button("CONVERT  ·  STEAM  →  GAME PASS", primary: true);
        var toGamePassHint = new Label
        {
            Text = "Pick a Steam world folder (WorldSave_*.sav + PlayerData). Writes a Game Pass copy next to it.",
            Style = ModalChrome.St("AfMuted"), FontSize = 10,
        };
        var toSteam = ModalChrome.Button("CONVERT  ·  GAME PASS  →  STEAM", primary: true);
        var toSteamHint = new Label
        {
            Text = "Pick a Game Pass folder (the one with containers.index). Writes a Steam world folder next to it.",
            Style = ModalChrome.St("AfMuted"), FontSize = 10,
        };

        var result = new Label
        {
            IsVisible = false, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap,
            Style = ModalChrome.St("AfFieldValue"),
        };
        var openFolder = ModalChrome.Button("OPEN OUTPUT FOLDER", primary: false);
        openFolder.IsVisible = false;
        string? lastOut = null;
        openFolder.Clicked += (_, _) => { if (lastOut is not null) TryOpenFolder(lastOut); };

        async Task RunAsync(Func<string, string, string?, string> convert, Func<string, bool> validate, string badMsg, string suffix)
        {
            string? picked = null;
            try
            {
                var pick = await CommunityToolkit.Maui.Storage.FolderPicker.PickAsync(CancellationToken.None);
                if (!pick.IsSuccessful || pick.Folder is null) return; // dismissed
                picked = pick.Folder.Path;
                openFolder.IsVisible = false;
                result.IsVisible = true;
                result.TextColor = ModalChrome.Col("AfTextSecondary");

                if (!validate(picked))
                {
                    result.Text = badMsg;
                    return;
                }
                var dest = picked.TrimEnd('/', '\\') + suffix;
                var id = idEntry.Text?.Trim();
                id = string.IsNullOrWhiteSpace(id) ? null : id;

                result.Text = "Converting…  (the first run may download the Oodle compressor)";
                toGamePass.IsEnabled = toSteam.IsEnabled = false;

                lastOut = await Task.Run(() => convert(picked, dest, id));
                EditorLog.Info("Settings", $"Save converted to {lastOut}");
                result.TextColor = ModalChrome.Col("AfTerminalGreen");
                result.Text = $"Done. Converted copy written to:\n{lastOut}\n"
                    + "Your original was not changed. Verify it loads in-game before relying on it.";
                openFolder.IsVisible = true;
            }
            catch (Exception ex) when (!IsPickerCancellation(ex))
            {
                EditorLog.Error("Settings", "Save conversion failed", ex);
                result.IsVisible = true;
                result.TextColor = ModalChrome.Col("AfAlertRed");
                result.Text = $"Couldn't convert: {ex.Message}";
            }
            finally
            {
                toGamePass.IsEnabled = toSteam.IsEnabled = true;
            }
        }

        toGamePass.Clicked += async (_, _) => await RunAsync(
            (src, dest, id) => AbioticEditor.Core.GamePass.GamePassConverter.SteamWorldToGamePass(src, dest, null, id),
            src => System.IO.Directory.EnumerateFiles(src, "WorldSave_*.sav").Any(),
            "That folder has no WorldSave_*.sav files - pick a Steam world folder.",
            "-GamePass");

        toSteam.Clicked += async (_, _) => await RunAsync(
            (src, dest, id) => AbioticEditor.Core.GamePass.GamePassConverter.GamePassToSteamWorld(src, null, dest, id),
            src => AbioticEditor.Core.GamePass.GamePassSaveSet.IsGamePassFolder(src),
            "That folder has no containers.index - pick a Game Pass save folder.",
            "-Steam");

        var idLabel = new Label
        {
            Text = "PLAYER ACCOUNT ID",
            Style = ModalChrome.St("AfFieldLabel"),
        };

        return ModalChrome.Card("SAVE CONVERSION",
            "Convert a world between Steam (loose .sav files) and Game Pass / Xbox (one container). The "
                + "save content is identical either way; only the packaging changes. The converted copy is "
                + "written next to the folder you pick - your original is untouched.",
            idLabel, idEntry, idHint,
            toGamePass, toGamePassHint,
            toSteam, toSteamHint,
            result,
            new HorizontalStackLayout { Spacing = 10, Children = { openFolder } });
    }

    private static void TryOpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Settings", $"Could not open folder {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// The toolkit reports a dismissed folder dialog as a failure carrying an exception;
    /// closing the picker without choosing is a normal outcome, not an error.
    /// </summary>
    private static bool IsPickerCancellation(Exception ex)
        => ex is OperationCanceledException
            || ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The UPDATES card: a CHECK FOR UPDATES button that queries GitHub releases and, when a
    /// newer version is found, reveals DOWNLOAD &amp; INSTALL (which swaps the files in place
    /// and restarts the app). While a download runs it shows a progress bar and a CANCEL
    /// button (the download honours the cancellation token). Self-contained so it survives
    /// the theme-driven rebuild.
    /// </summary>
    private View BuildUpdatesCard()
    {
        var status = new Label
        {
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
            Text = $"Installed version: {Services.UpdateService.CurrentVersion}.",
        };

        var checkButton = ModalChrome.Button("CHECK FOR UPDATES", primary: false);
        var installButton = ModalChrome.Button("DOWNLOAD & INSTALL", primary: true);
        var cancelButton = ModalChrome.Button("CANCEL", primary: false);
        installButton.IsVisible = false;
        cancelButton.IsVisible = false;

        // The download fraction made visible. Hidden until a download is in flight.
        var progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = (Color)Application.Current!.Resources["AfAmber"],
            IsVisible = false,
        };

        Updater.UpdateCheckResult? available = null;
        // Set for the duration of a download so CANCEL can abort it; cleared when idle.
        CancellationTokenSource? downloadCts = null;

        checkButton.Clicked += async (_, _) =>
        {
            installButton.IsVisible = false;
            checkButton.IsEnabled = false;
            status.Text = "Checking GitHub for updates...";
            try
            {
                var result = await Services.UpdateService.CheckAsync();
                if (result.UpdateAvailable)
                {
                    available = result;
                    status.Text = $"{result.LatestVersion} is available "
                        + $"(you have {result.CurrentVersion}).";
                    installButton.IsVisible = true;
                }
                else
                {
                    available = null;
                    status.Text = result.Message ?? "You are on the latest version.";
                }
            }
            catch (Updater.UpdaterConfigurationException)
            {
                status.Text = "Updates are not configured yet (no release repository set).";
            }
            catch (Exception ex)
            {
                status.Text = $"Could not check for updates: {ex.Message}";
            }
            finally
            {
                checkButton.IsEnabled = true;
            }
        };

        cancelButton.Clicked += (_, _) =>
        {
            // Ask the in-flight download to stop; the install handler's catch resets the UI.
            cancelButton.IsEnabled = false;
            status.Text = "Cancelling download...";
            downloadCts?.Cancel();
        };

        installButton.Clicked += async (_, _) =>
        {
            if (available is null)
            {
                return;
            }
            // DisplayAlert routes through the native WinUI dialog which works correctly
            // from within a modal page. The shared DialogViewModel overlay is rendered in
            // the main page tree and appears invisible behind a modal, so ViewUtils.ConfirmAsync
            // must not be used here.
            var ok = await DisplayAlertAsync(
                "Install update",
                $"Download and install {available.LatestVersion}? The app will close and reopen.",
                "Install",
                "Cancel");
            if (!ok)
            {
                return;
            }

            checkButton.IsEnabled = false;
            installButton.IsEnabled = false;
            installButton.Text = "DOWNLOADING...";
            progressBar.Progress = 0;
            progressBar.IsVisible = true;
            cancelButton.IsEnabled = true;
            cancelButton.IsVisible = true;
            using var cts = new CancellationTokenSource();
            downloadCts = cts;
            var progress = new Progress<double>(p =>
            {
                progressBar.Progress = p;
                status.Text = $"Downloading... {(int)(p * 100)}%";
                installButton.Text = $"DOWNLOADING {(int)(p * 100)}%";
            });
            try
            {
                status.Text = "Downloading update...";
                await Services.UpdateService.DownloadInstallAndRestartAsync(available, progress, cts.Token);
                // If we get here the app is about to quit; leave a note in case it lingers.
                status.Text = "Installing update; the app will restart...";
                installButton.Text = "INSTALLING...";
                cancelButton.IsVisible = false;
            }
            catch (OperationCanceledException)
            {
                status.Text = "Download cancelled.";
                ResetInstallControls();
            }
            catch (Exception ex)
            {
                status.Text = $"Update failed: {ex.Message}";
                ResetInstallControls();
            }
            finally
            {
                downloadCts = null;
            }

            void ResetInstallControls()
            {
                installButton.Text = "DOWNLOAD & INSTALL";
                checkButton.IsEnabled = true;
                installButton.IsEnabled = true;
                cancelButton.IsVisible = false;
                cancelButton.IsEnabled = true;
                progressBar.IsVisible = false;
            }
        };

        var hint = Services.UpdateService.IsPlaceholder
            ? "Checks GitHub releases for a newer build. (No release repository is configured yet - "
                + "the developer must set it before this works.)"
            : "Checks GitHub releases for a newer build and installs it in place. Every save still keeps its .bak.";

        return ModalChrome.Card("UPDATES", hint,
            status,
            progressBar,
            new HorizontalStackLayout { Spacing = 10, Children = { checkButton, installButton, cancelButton } });
    }

    /// <summary>One enable/disable row for an installed plugin (name + state, right-aligned switch).</summary>
    private View PluginRow(Core.Plugins.PluginDescriptor d)
    {
        var sw = new Switch { IsToggled = d.Manifest.Enabled, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += async (_, e) =>
        {
            if (!d.SetEnabled(e.Value))
            {
                sw.IsToggled = d.Manifest.Enabled;
                await Views.ViewUtils.AlertAsync(this, "Plugins", "Could not update the plugin manifest (read-only folder?).");
                return;
            }
            EditorLog.Info("Plugins", $"Plugin {(e.Value ? "enabled" : "disabled")}: {d.Manifest.Name}");
            await Views.ViewUtils.AlertAsync(this, "Plugins",
                $"{d.Manifest.Name} {(e.Value ? "enabled" : "disabled")}. Restart to apply.");
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        grid.Add(new Label
        {
            Text = $"{d.Manifest.Name}  ({d.State})",
            Style = ModalChrome.St("AfFieldValue"),
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        grid.Add(sw, 1, 0);
        return grid;
    }

    private Button ThemeButton(string text, ThemeAccent accent)
    {
        var active = ThemeService.Accent == accent;
        // Active = primary fill, inactive = ghost outline (reads as a segmented control).
        var button = ModalChrome.Button(active ? $"{text}  ✓" : text, primary: active);
        button.Clicked += (_, _) => ApplyTheme(accent, ThemeService.IsLight);
        return button;
    }

    private static Grid LabeledRow(string label, Switch control)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(new Label
        {
            Text = label,
            Style = ModalChrome.St("AfFieldValue"),
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        grid.Add(control, 1, 0);
        return grid;
    }

    private void ApplyTheme(ThemeAccent accent, bool light)
    {
        var accentChanged = accent != ThemeService.Accent;
        var modeChanged = light != ThemeService.IsLight;
        if (!accentChanged && !modeChanged) return;
        ThemeService.Apply(accent, light);
        _themeChanged = true;
        if (accentChanged)
        {
            EditorLog.Info("Settings", $"Theme accent set to {accent}");
        }
        if (modeChanged)
        {
            EditorLog.Info("Settings", $"Theme mode set to {(light ? "light" : "dark")}");
        }
        // Repaint this sheet with the new palette; the host rebuilds on close.
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        Content = BuildContent();
    }

    private async Task CloseAsync()
    {
        await Navigation.PopModalAsync();
        // Rebuild the editor host so every open surface re-evaluates concealment / palette.
        if (_themeChanged || _spoilerChanged)
        {
            await _rebuildHost();
        }
    }
}
