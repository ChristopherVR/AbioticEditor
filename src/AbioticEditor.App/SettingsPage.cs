using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;

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

    public SettingsPage(MainViewModel vm, Func<Task> rebuildHost)
    {
        _vm = vm;
        _rebuildHost = rebuildHost;
        Title = "Settings";
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        Content = BuildContent();
    }

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
        var currentLanguageName = Services.LocalizationService.Available
            .FirstOrDefault(l => l.Code == Services.LocalizationService.CurrentCode)?.NativeName
            ?? Services.LocalizationService.CurrentCode;
        var languageButton = new Button { Text = currentLanguageName };
        languageButton.Clicked += async (_, _) => await Navigation.PushModalAsync(new LanguagePage());
        var languageCard = ModalChrome.Card(loc["Settings_Language"], loc["Settings_Language_Hint"],
            new HorizontalStackLayout { Spacing = 10, Children = { languageButton } });

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
        spoilerSwitch.Toggled += (_, e) => { SpoilerService.Enabled = e.Value; _spoilerChanged = true; };
        resealButton.Clicked += (_, _) => { SpoilerService.ResetReveals(); RefreshReveals(); _spoilerChanged = true; };

        var spoilerCard = ModalChrome.Card("SPOILERS",
            "Seals content you haven't reached yet (future quest flags, traders, recipes, hidden achievements, codex entries, contained anomalies) behind a CLASSIFIED stamp. Tap any sealed item to override clearance and reveal it - revealed items stay visible from then on.",
            LabeledRow("Spoiler protection", spoilerSwitch),
            revealedHint,
            new HorizontalStackLayout { Spacing = 10, Children = { resealButton } });

        // ----- GAME DATA -----
        var importUsmap = new Button { Text = "IMPORT USMAP", Command = _vm.ImportMappingsCommand };
        var gameDataCard = ModalChrome.Card("GAME DATA",
            "Install a newer Mappings.usmap (dumped with FModel or Dumper-7) for future game versions. Takes effect after restart.",
            new HorizontalStackLayout { Spacing = 10, Children = { importUsmap } });

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

        // ----- WORLD MAPS -----
        var worldMaps = new Button { Text = "EDIT WORLD MAPS" };
        worldMaps.Clicked += async (_, _) => await Navigation.PushModalAsync(new WorldMapsPage(_vm));
        var worldMapsCard = ModalChrome.Card("WORLD MAPS",
            "Edit per-actor world-state maps that aren't in the main editor: elevators, buttons, "
                + "resource nodes, NPC spawners, power sockets, vehicles, portals, triggers and more. "
                + "Pick a world save, choose a feature, and edit an entry's fields (every save keeps a .bak).",
            new HorizontalStackLayout { Spacing = 10, Children = { worldMaps } });

        // ----- UPDATES -----
        var updatesCard = BuildUpdatesCard();

        // ----- ABOUT -----
        var aboutCard = ModalChrome.Card("ABOUT",
            $"Abiotic Editor · {AppInfo.Current.VersionString} · every save write keeps a .bak next to the file.");

        var close = ModalChrome.Button("CLOSE", primary: false);
        close.Clicked += async (_, _) => await CloseAsync();

        return ModalChrome.Scaffold(
            "EDITOR CONFIGURATION", "Settings",
            new View[] { themeCard, languageCard, diagnosticsCard, spoilerCard, gameDataCard, worldMapsCard, pluginsCard, updatesCard, aboutCard },
            ModalChrome.Footer(close),
            maxWidth: 620);
    }

    /// <summary>
    /// The UPDATES card: a CHECK FOR UPDATES button that queries GitHub releases and, when a
    /// newer version is found, reveals DOWNLOAD &amp; INSTALL (which swaps the files in place
    /// and restarts the app). Self-contained so it survives the theme-driven rebuild.
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
        installButton.IsVisible = false;

        Updater.UpdateCheckResult? available = null;

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

        installButton.Clicked += async (_, _) =>
        {
            if (available is null)
            {
                return;
            }
            var ok = await Views.ViewUtils.ConfirmAsync(this, "Install update",
                $"Download and install {available.LatestVersion}? The app will close and reopen.",
                "Install", "Cancel");
            if (!ok)
            {
                return;
            }

            checkButton.IsEnabled = false;
            installButton.IsEnabled = false;
            var progress = new Progress<double>(p => status.Text = $"Downloading... {(int)(p * 100)}%");
            try
            {
                status.Text = "Downloading update...";
                await Services.UpdateService.DownloadInstallAndRestartAsync(available, progress);
                // If we get here the app is about to quit; leave a note in case it lingers.
                status.Text = "Installing update; the app will restart...";
            }
            catch (Exception ex)
            {
                status.Text = $"Update failed: {ex.Message}";
                checkButton.IsEnabled = true;
                installButton.IsEnabled = true;
            }
        };

        var hint = Services.UpdateService.IsPlaceholder
            ? "Checks GitHub releases for a newer build. (No release repository is configured yet - "
                + "the developer must set it before this works.)"
            : "Checks GitHub releases for a newer build and installs it in place. Every save still keeps its .bak.";

        return ModalChrome.Card("UPDATES", hint,
            status,
            new HorizontalStackLayout { Spacing = 10, Children = { checkButton, installButton } });
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
        if (accent == ThemeService.Accent && light == ThemeService.IsLight) return;
        ThemeService.Apply(accent, light);
        _themeChanged = true;
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
