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
        Title = Services.LocalizationResourceManager.Instance["Settings_ScaffoldSubtitle"];
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
        var loc = Services.LocalizationResourceManager.Instance;

        // ----- THEME -----
        var cascadeButton = ThemeButton(loc["Settings_ThemeCascade"], ThemeAccent.Cascade);
        var hazardButton = ThemeButton(loc["Settings_ThemeHazard"], ThemeAccent.Hazard);

        var modeSwitch = new Switch { IsToggled = ThemeService.IsLight };
        modeSwitch.Toggled += (_, e) => ApplyTheme(ThemeService.Accent, e.Value);

        var themeCard = ModalChrome.Card(loc["Settings_Theme"],
            loc["Settings_ThemeHint"],
            new HorizontalStackLayout { Spacing = 10, Children = { cascadeButton, hazardButton } },
            LabeledRow(loc["Settings_LightMode"], modeSwitch));

        // ----- LANGUAGE -----
        // Current language is read-only; a separate CHANGE button opens the chooser. OnAppearing
        // re-reads CurrentCode when the chooser pops, so this label stays in sync.
        _languageValueLabel = new Label
        {
            Text = CurrentLanguageName(),
            Style = ModalChrome.St("AfFieldValue"),
            VerticalOptions = LayoutOptions.Center,
        };
        var changeLanguageButton = ModalChrome.Button(loc["Settings_LanguageChange"], primary: false);
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
        var openLogs = new Button { Text = loc["Settings_OpenLogFolder"], Command = _vm.OpenLogFolderCommand };

        var diagnosticsCard = ModalChrome.Card(loc["Settings_Diagnostics"],
            loc["Settings_DiagnosticsHint"],
            LabeledRow(loc["Settings_DiagnosticLogging"], logSwitch),
            new HorizontalStackLayout { Spacing = 10, Children = { openLogs } });

        // ----- SPOILERS -----
        var spoilerSwitch = new Switch { IsToggled = SpoilerService.Enabled };
        var resealButton = ModalChrome.Button(loc["Settings_ResealItems"], primary: false);
        var revealedHint = new Label { Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        void RefreshReveals()
            => revealedHint.Text = SpoilerService.RevealedCount == 0
                ? loc["Settings_NothingRevealed"]
                : loc.Format("Settings_ItemsRevealed", SpoilerService.RevealedCount);
        RefreshReveals();
        spoilerSwitch.Toggled += (_, e) =>
        {
            if (e.Value == SpoilerService.Enabled) return;
            SpoilerService.Enabled = e.Value;
            _spoilerChanged = true;
            EditorLog.Info("Settings", $"Spoilers {(e.Value ? "shown" : "hidden")}");
        };
        resealButton.Clicked += (_, _) => { SpoilerService.ResetReveals(); RefreshReveals(); _spoilerChanged = true; };

        var spoilerCard = ModalChrome.Card(loc["Settings_Spoilers"],
            loc["Settings_SpoilersHint"],
            LabeledRow(loc["Settings_SpoilerProtection"], spoilerSwitch),
            revealedHint,
            new HorizontalStackLayout { Spacing = 10, Children = { resealButton } });

        // ----- GAME DATA -----
        var gameDataCard = BuildGameDataCard();
        var modsCard = BuildModsCard();

        // ----- SAVE CONVERSION (Steam <-> Game Pass) -----
        var conversionCard = BuildSaveConversionCard();

        // ----- PLUGINS -----
        var managePlugins = new Button { Text = loc["Settings_ManagePlugins"] };
        managePlugins.Clicked += async (_, _) =>
            await Navigation.PushModalAsync(new PluginsPage(_vm, _rebuildHost));
        var pluginDescriptors = Services.PluginService.Descriptors;
        var pluginCount = pluginDescriptors.Count;

        var pluginToggles = new VerticalStackLayout { Spacing = 6 };
        foreach (var d in pluginDescriptors)
        {
            pluginToggles.Children.Add(PluginRow(d));
        }

        var pluginsCard = ModalChrome.Card(loc["Settings_Plugins"],
            loc.Format("Settings_PluginsHint", pluginCount),
            pluginToggles,
            new HorizontalStackLayout { Spacing = 10, Children = { managePlugins } });

        // ----- UPDATES -----
        var updatesCard = BuildUpdatesCard();

        // ----- tabs (one tab at a time instead of one long scroll) -----
        var tabs = new (string Label, View[] Cards)[]
        {
            (loc["Settings_TabGeneral"], new View[] { themeCard, languageCard, diagnosticsCard }),
            (loc["Settings_TabEditor"], new View[] { spoilerCard }),
            (loc["Settings_GameData"], new View[] { gameDataCard, modsCard }),
            (loc["Settings_TabConvert"], new View[] { conversionCard }),
            (loc["Settings_Plugins"], new View[] { pluginsCard }),
            (loc["Settings_TabUpdates"], new View[] { updatesCard }),
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
        // Vertical rail down the left edge so the sections read as tabs (not a cramped strip).
        var tabRail = ModalChrome.VerticalTabRail(tabs.Select(t => t.Label).ToList(), 0, ShowTab);
        ShowTab(0);

        var close = ModalChrome.Button(loc["Common_Close"], primary: false);
        close.Clicked += async (_, _) => await CloseAsync();

        return ModalChrome.ScaffoldWithSidebar(
            loc["Settings_ScaffoldTitle"], loc["Settings_ScaffoldSubtitle"],
            tabRail,
            tabContent,
            ModalChrome.Footer(close),
            contentMaxWidth: 640);
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
        var loc = Services.LocalizationResourceManager.Instance;
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
        var locateFolder = new Button { Text = loc["GameDataSettings_SetGameFolder"] };
        var autoDetect = ModalChrome.Button(loc["GameDataSettings_UseAutoDetect"], primary: false);

        void Refresh()
        {
            var custom = Services.GameDataServices.CustomInstallPath;
            status.Text = Services.GameDataServices.IsGameDataLoaded
                ? (custom is null ? loc["GameDataSettings_LoadedAuto"] : loc["GameDataSettings_LoadedCustom"])
                : Services.GameDataServices.StatusMessage;

            // The folder actually in use: the one you set, else the auto-detected install.
            var folder = custom
                ?? AbioticEditor.Core.Assets.AfInstallLocator.FindInstallRoot()
                ?? AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory();
            folderValue.Text = loc.Format("GameDataSettings_GameFolderLabel",
                folder ?? loc["GameDataSettings_FolderNotFound"]);

            // The data file (.usmap) that lets the editor read the game's tables.
            var usmap = AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings();
            usmapValue.Text = loc.Format("GameDataSettings_UsmapLabel", usmap is null
                ? loc["GameDataSettings_UsmapNotFound"]
                : System.IO.Path.GetFileName(usmap)
                    + (usmap == AbioticEditor.Core.Assets.GameAssetProvider.UserMappingsPath
                        ? loc["GameDataSettings_UsmapImported"]
                        : loc["GameDataSettings_UsmapBundled"]));

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
            await this.AlertAsync(okTitle,
                Services.GameDataServices.IsGameDataLoaded
                    ? loc["GameDataSettings_GameDataLoadedMessage"]
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
                    await this.AlertAsync(loc["GameDataSettings_NoGameDataTitle"],
                        loc.Format("GameDataSettings_NoGameDataMessage", picked));
                    return;
                }
                Services.GameDataServices.CustomInstallPath = picked;
                EditorLog.Info("Settings", $"Game install folder set to {picked} (paks: {paks})");
                await ApplyAndReloadAsync(loc["GameDataSettings_GameFolderSetTitle"]);
            }
            catch (Exception ex) when (!IsPickerCancellation(ex))
            {
                EditorLog.Error("Settings", "Game folder pick failed", ex);
                await this.AlertAsync(loc["GameDataSettings_FolderPickerFailedTitle"], ex.Message);
            }
        };

        autoDetect.Clicked += async (_, _) =>
        {
            Services.GameDataServices.CustomInstallPath = null;
            EditorLog.Info("Settings", "Game install folder reset to auto-detect");
            await ApplyAndReloadAsync(loc["GameDataSettings_AutoDetectRestoredTitle"]);
        };

        var importUsmap = new Button { Text = loc["GameDataSettings_ImportDataFile"], Command = _vm.ImportMappingsCommand };

        return ModalChrome.Card(loc["Settings_GameData"],
            loc["GameDataSettings_CardHint"],
            status,
            folderValue,
            usmapValue,
            new HorizontalStackLayout { Spacing = 10, Children = { locateFolder, autoDetect } },
            new HorizontalStackLayout { Spacing = 10, Children = { importUsmap } });
    }

    /// <summary>
    /// The MODS card: a master "load mods" switch plus one toggle per mod installed under the game's
    /// <c>~mods</c>/<c>LogicMods</c> folders, so a player can hide an individual mod's content (or
    /// all of it) in the editor. Toggling persists to <see cref="Core.Assets.ModLoadStore"/> and
    /// reloads game data in place. The per-mod switches lock when the master is off or the
    /// <c>ABIOTIC_NO_MODS</c> env var disables mods entirely.
    /// </summary>
    private View BuildModsCard()
    {
        var loc = Services.LocalizationResourceManager.Instance;

        var masterSwitch = new Switch
        {
            IsToggled = Services.GameDataServices.ModsEnabled,
            IsEnabled = !Services.GameDataServices.ModsDisabledByEnv,
            VerticalOptions = LayoutOptions.Center,
        };
        var statusLine = new Label { Style = ModalChrome.St("AfMuted"), FontSize = 11 };
        var modsList = new VerticalStackLayout { Spacing = 6 };

        // Reload after any toggle so the catalogs (and the item palette) rebuild without a relaunch.
        async Task ApplyAndReloadAsync()
        {
            await _vm.ReloadGameDataAsync();
            RefreshList();
            await this.AlertAsync(loc["GameDataSettings_ModsToggledTitle"],
                Services.GameDataServices.IsGameDataLoaded
                    ? loc["GameDataSettings_GameDataLoadedMessage"]
                    : Services.GameDataServices.StatusMessage);
        }

        void RefreshList()
        {
            var envDisabled = Services.GameDataServices.ModsDisabledByEnv;
            var masterOn = Services.GameDataServices.ModsEnabled;
            masterSwitch.IsToggled = masterOn;

            var installed = Services.GameDataServices.InstalledMods;
            statusLine.Text = envDisabled
                ? loc["GameDataSettings_ModsDisabledByEnv"]
                : installed.Count == 0
                    ? loc["ModsSettings_NoneInstalled"]
                    : loc.Format("ModsSettings_Status", Services.GameDataServices.LoadedMods.Count, installed.Count);

            modsList.Children.Clear();
            foreach (var mod in installed)
            {
                var name = mod.Name; // capture for the handler
                var sw = new Switch
                {
                    IsToggled = Services.GameDataServices.IsModEnabled(name),
                    IsEnabled = masterOn && !envDisabled,
                    VerticalOptions = LayoutOptions.Center,
                };
                sw.Toggled += async (_, e) =>
                {
                    if (e.Value == Services.GameDataServices.IsModEnabled(name)) return;
                    Services.GameDataServices.SetModEnabled(name, e.Value);
                    EditorLog.Info("Settings", $"Mod '{name}' {(e.Value ? "enabled" : "disabled")}");
                    await ApplyAndReloadAsync();
                };
                modsList.Children.Add(LabeledRow(name, sw));
            }
        }

        masterSwitch.Toggled += async (_, e) =>
        {
            if (e.Value == Services.GameDataServices.ModsEnabled) return;
            Services.GameDataServices.ModsEnabled = e.Value;
            EditorLog.Info("Settings", $"Mod loading {(e.Value ? "enabled" : "disabled")}");
            await ApplyAndReloadAsync();
        };

        RefreshList();

        return ModalChrome.Card(loc["Settings_Mods"],
            loc["ModsSettings_CardHint"],
            LabeledRow(loc["GameDataSettings_LoadMods"], masterSwitch),
            statusLine,
            modsList);
    }

    /// <summary>
    /// The SAVE CONVERSION card: convert a save between Steam (loose files) and Game Pass (Xbox
    /// container) packaging. Each direction picks a source folder, writes the converted copy next to
    /// it, and never touches the original. The content is identical either way; only the packaging
    /// differs.
    /// </summary>
    private static View BuildSaveConversionCard()
    {
        var loc = Services.LocalizationResourceManager.Instance;
        // Optional account id: re-homes the (single) player save to this id during conversion.
        var idEntry = new Entry
        {
            Placeholder = loc["Settings_ConvertIdPlaceholder"],
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            Keyboard = Keyboard.Plain,
        };
        var idHint = new Label
        {
            Text = loc["Settings_ConvertIdHint"],
            Style = ModalChrome.St("AfMuted"),
            FontSize = 10,
        };

        var toGamePass = ModalChrome.Button(loc["Settings_ConvertToGamePass"], primary: true);
        var toGamePassHint = new Label
        {
            Text = loc["Settings_ConvertToGamePassHint"],
            Style = ModalChrome.St("AfMuted"), FontSize = 10,
        };
        var toSteam = ModalChrome.Button(loc["Settings_ConvertToSteam"], primary: true);
        var toSteamHint = new Label
        {
            Text = loc["Settings_ConvertToSteamHint"],
            Style = ModalChrome.St("AfMuted"), FontSize = 10,
        };

        var result = new Label
        {
            IsVisible = false, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap,
            Style = ModalChrome.St("AfFieldValue"),
        };
        var openFolder = ModalChrome.Button(loc["Settings_OpenOutputFolder"], primary: false);
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

                result.Text = loc["Settings_Converting"];
                toGamePass.IsEnabled = toSteam.IsEnabled = false;

                lastOut = await Task.Run(() => convert(picked, dest, id));
                EditorLog.Info("Settings", $"Save converted to {lastOut}");
                result.TextColor = ModalChrome.Col("AfTerminalGreen");
                result.Text = loc.Format("Settings_ConvertDone", lastOut);
                openFolder.IsVisible = true;
            }
            catch (Exception ex) when (!IsPickerCancellation(ex))
            {
                EditorLog.Error("Settings", "Save conversion failed", ex);
                result.IsVisible = true;
                result.TextColor = ModalChrome.Col("AfAlertRed");
                result.Text = loc.Format("Settings_ConvertFailed", ex.Message);
            }
            finally
            {
                toGamePass.IsEnabled = toSteam.IsEnabled = true;
            }
        }

        toGamePass.Clicked += async (_, _) => await RunAsync(
            (src, dest, id) => AbioticEditor.Core.GamePass.GamePassConverter.SteamWorldToGamePass(src, dest, null, id),
            src => System.IO.Directory.EnumerateFiles(src, "WorldSave_*.sav").Any(),
            loc["Settings_ConvertNoWorldSave"],
            "-GamePass");

        toSteam.Clicked += async (_, _) => await RunAsync(
            (src, dest, id) => AbioticEditor.Core.GamePass.GamePassConverter.GamePassToSteamWorld(src, null, dest, id),
            src => AbioticEditor.Core.GamePass.GamePassSaveSet.IsGamePassFolder(src),
            loc["Settings_ConvertNoContainers"],
            "-Steam");

        var idLabel = new Label
        {
            Text = loc["Settings_ConvertPlayerAccountId"],
            Style = ModalChrome.St("AfFieldLabel"),
        };

        return ModalChrome.Card(loc["Settings_SaveConversion"],
            loc["Settings_SaveConversionHint"],
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
        var loc = Services.LocalizationResourceManager.Instance;
        var status = new Label
        {
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
            Text = loc.Format("Settings_InstalledVersion", Services.UpdateService.CurrentVersion),
        };

        var checkButton = ModalChrome.Button(loc["Settings_CheckForUpdates"], primary: false);
        var installButton = ModalChrome.Button(loc["Settings_DownloadInstall"], primary: true);
        var cancelButton = ModalChrome.Button(loc["Common_Cancel"], primary: false);
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
            status.Text = loc["Settings_CheckingForUpdates"];
            try
            {
                var result = await Services.UpdateService.CheckAsync();
                if (result.UpdateAvailable)
                {
                    available = result;
                    status.Text = loc.Format("Settings_UpdateAvailable",
                        result.LatestVersion, result.CurrentVersion);
                    installButton.IsVisible = true;
                }
                else
                {
                    available = null;
                    status.Text = result.Message ?? loc["Settings_OnLatestVersion"];
                }
            }
            catch (Updater.UpdaterConfigurationException)
            {
                status.Text = loc["Settings_UpdatesNotConfigured"];
            }
            catch (Exception ex)
            {
                status.Text = loc.Format("Settings_CheckFailed", ex.Message);
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
            status.Text = loc["Settings_CancellingDownload"];
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
                loc["Settings_InstallUpdateTitle"],
                loc.Format("Settings_InstallUpdateMessage", available.LatestVersion),
                loc["Settings_InstallButton"],
                loc["Common_Cancel"]);
            if (!ok)
            {
                return;
            }

            checkButton.IsEnabled = false;
            installButton.IsEnabled = false;
            installButton.Text = loc["Settings_Downloading"];
            progressBar.Progress = 0;
            progressBar.IsVisible = true;
            cancelButton.IsEnabled = true;
            cancelButton.IsVisible = true;
            using var cts = new CancellationTokenSource();
            downloadCts = cts;
            var progress = new Progress<double>(p =>
            {
                progressBar.Progress = p;
                status.Text = loc.Format("Settings_DownloadingPercent", (int)(p * 100));
                installButton.Text = loc.Format("Settings_DownloadingButtonPercent", (int)(p * 100));
            });
            try
            {
                status.Text = loc["Settings_DownloadingUpdate"];
                await Services.UpdateService.DownloadInstallAndRestartAsync(available, progress, cts.Token);
                // If we get here the app is about to quit; leave a note in case it lingers.
                status.Text = loc["Settings_InstallingRestart"];
                installButton.Text = loc["Settings_Installing"];
                cancelButton.IsVisible = false;
            }
            catch (OperationCanceledException)
            {
                status.Text = loc["Settings_DownloadCancelled"];
                ResetInstallControls();
            }
            catch (Exception ex)
            {
                status.Text = loc.Format("Settings_UpdateFailed", ex.Message);
                ResetInstallControls();
            }
            finally
            {
                downloadCts = null;
            }

            void ResetInstallControls()
            {
                installButton.Text = loc["Settings_DownloadInstall"];
                checkButton.IsEnabled = true;
                installButton.IsEnabled = true;
                cancelButton.IsVisible = false;
                cancelButton.IsEnabled = true;
                progressBar.IsVisible = false;
            }
        };

        var hint = Services.UpdateService.IsPlaceholder
            ? loc["Settings_UpdatesHintPlaceholder"]
            : loc["Settings_UpdatesHint"];

        return ModalChrome.Card(loc["Settings_TabUpdates"], hint,
            status,
            progressBar,
            new HorizontalStackLayout { Spacing = 10, Children = { checkButton, installButton, cancelButton } });
    }

    /// <summary>One enable/disable row for an installed plugin (name + state, right-aligned switch).</summary>
    private View PluginRow(Core.Plugins.PluginDescriptor d)
    {
        var loc = Services.LocalizationResourceManager.Instance;
        var sw = new Switch { IsToggled = d.Manifest.Enabled, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += async (_, e) =>
        {
            if (!d.SetEnabled(e.Value))
            {
                sw.IsToggled = d.Manifest.Enabled;
                await this.AlertAsync(loc["Settings_Plugins"], loc["Settings_PluginManifestFailed"]);
                return;
            }
            EditorLog.Info("Plugins", $"Plugin {(e.Value ? "enabled" : "disabled")}: {d.Manifest.Name}");
            await this.AlertAsync(loc["Settings_Plugins"],
                string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    e.Value ? loc["Settings_PluginEnabledMessage"] : loc["Settings_PluginDisabledMessage"],
                    d.Manifest.Name));
        };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        grid.Add(new Label
        {
            Text = loc.Format("Settings_PluginRowLabel", d.Manifest.Name, d.State),
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
        var button = ModalChrome.Button(
            active ? Services.LocalizationResourceManager.Instance.Format("Settings_ThemeActive", text) : text,
            primary: active);
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
