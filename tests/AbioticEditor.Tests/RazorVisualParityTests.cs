namespace AbioticEditor.Tests;

public sealed class RazorVisualParityTests
{
    [Fact]
    public void Desktop_shell_loads_native_fonts_component_styles_and_parity_contract_last()
    {
        // This asserts the ORDER the two stylesheets are linked in, so it has to look at real
        // markup only: a Razor comment naming a stylesheet is stripped by the compiler and must
        // not count as a link, or explaining the markup silently inverts the measured order.
        var app = System.Text.RegularExpressions.Regex.Replace(
            Source("Components", "App.razor"), @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var componentStyles = app.IndexOf("AbioticEditor.Web.styles.css", StringComparison.Ordinal);
        var parityStyles = app.IndexOf("parity.css", StringComparison.Ordinal);

        Assert.True(componentStyles >= 0);
        Assert.True(parityStyles > componentStyles);
        foreach (var asset in new[]
                 {
                     "wwwroot/fonts/OpenSans-Regular.ttf", "wwwroot/fonts/OpenSans-Semibold.ttf",
                     "wwwroot/fonts/Digital7.ttf", "wwwroot/fonts/MaterialSymbolsOutlined.ttf",
                     "wwwroot/images/abiotic-factor.png", "wwwroot/images/abiotic-factor-transparent.png",
                 })
            Assert.True(UiSource.Exists(asset), $"Missing visual asset {asset}");
    }

    [Fact]
    public void Cascade_palettes_match_the_retired_native_theme()
    {
        var css = Source("wwwroot", "parity.css");

        foreach (var token in new[]
                 {
                     "--shell:#081119", "--page:#0c1a24", "--panel:#132736",
                     "--elevated:#1b3648", "--divider:#2e5471", "--ink:#dceff9",
                     "--orange:#f89a4f", "--hazard:#ffe563", "--terminal:#7fe9e2",
                     "--section:#71c5f6", "--shell:#dce6ee", "--page:#e9f1f7",
                 })
            Assert.Contains(token, css, StringComparison.Ordinal);
    }

    [Fact]
    public void Hazard_palettes_and_accent_control_remain_available()
    {
        var css = Source("wwwroot", "parity.css");
        var settings = Source("Components", "Pages", "Settings.razor");
        var service = Source("Services", "HostThemeService.cs");

        Assert.Contains(".app-frame.accent-hazard", css, StringComparison.Ordinal);
        Assert.Contains("--shell:#0c0b07", css, StringComparison.Ordinal);
        Assert.Contains("--shell:#e8e2d2", css, StringComparison.Ordinal);
        Assert.Contains("class=\"theme-segments\"", settings, StringComparison.Ordinal);
        Assert.Contains("HostThemeAccent.Cascade", settings, StringComparison.Ordinal);
        Assert.Contains("HostThemeAccent.Hazard", settings, StringComparison.Ordinal);
        // Cascade (the game-accurate blue-teal facility palette) is the default, matching the
        // native app's ThemeService; Hazard is the legacy alternate, not the default.
        Assert.Contains(": HostThemeAccent.Cascade", service, StringComparison.Ordinal);
        Assert.Contains(": HostTheme.Dark", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_recreates_the_retired_full_window_modal_sheet()
    {
        var layout = Source("Components", "Pages", "MainLayout.razor");
        var settings = Source("Components", "Pages", "Settings.razor");
        var css = Source("wwwroot", "parity.css");

        Assert.DoesNotContain("@page \"/settings\"", settings, StringComparison.Ordinal);
        Assert.Contains("<Settings OnClose=\"CloseSettings\" />", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-sheet\"", settings, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-sheet-header\"", settings, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-tab-rail\"", settings, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-sheet-footer\"", settings, StringComparison.Ordinal);
        Assert.Contains("SettingsTab.Compare", settings, StringComparison.Ordinal);
        Assert.Contains(".settings-sheet{position:fixed;inset:0;z-index:80", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:208px minmax(0,1fr)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_retains_separate_game_language_mod_and_plugin_management()
    {
        var settings = Source("Components", "Pages", "Settings.razor");

        Assert.Contains("Languages.AvailableGameData", settings, StringComparison.Ordinal);
        Assert.Contains("SetGameDataLanguageAsync", settings, StringComparison.Ordinal);
        Assert.Contains("GameDataSettings_SyncLanguageMessage", settings, StringComparison.Ordinal);
        Assert.Contains("Languages.SetGameDataLanguage(_gameLanguageWasAutomatic ? null : _pendingGameLanguage)", settings, StringComparison.Ordinal);
        Assert.Contains("if (_gameLanguageWasAutomatic) Languages.SetGameDataLanguage(_previousGameLanguage)", settings, StringComparison.Ordinal);
        Assert.Contains("@R(\"Settings_Mods\")", settings, StringComparison.Ordinal);
        Assert.Contains("HostSettings.InstalledMods", settings, StringComparison.Ordinal);
        Assert.Contains("SetModEnabled", settings, StringComparison.Ordinal);
        Assert.Contains("@R(\"Settings_PluginsHint\", HostSettings.Plugins.Count)", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>@R(\"Settings_Plugins\")</h2>\n                            <p class=\"muted\">@R(\"GameDataSettings_LoadModsHint\")", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_retains_native_desktop_chrome_and_accessible_responsive_drawers()
    {
        var layout = Source("Components", "Pages", "MainLayout.razor");
        var home = Source("Components", "Pages", "Home.razor");
        var css = Source("wwwroot", "parity.css");

        Assert.Contains("class=\"brand-lockup\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"active-folder\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"status-footer\"", layout, StringComparison.Ordinal);
        Assert.Contains("Main_LoggingTo", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("more-menu", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/compare\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Languages.Text(\"utilities\")\"", layout, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"PickFolderAsync\"", layout, StringComparison.Ordinal);
        Assert.Contains("FolderPicker.PickFolderAsync", layout, StringComparison.Ordinal);
        Assert.Contains("Workspace.Current?.SelectedSave is null", home, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows:78px 3px minmax(0,1fr) 49px", css, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:900px)", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion:reduce", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_dialog_footer_settings_and_controls_match_native_chrome()
    {
        var layout = Source("Components", "Pages", "MainLayout.razor");
        var settings = Source("Components", "Pages", "Settings.razor");
        var modal = Source("Components", "Shared", "ModalHost.razor");
        var toast = Source("Components", "Shared", "ToastHost.razor");
        var css = Source("wwwroot", "parity.css");

        // The app's own name no longer sits in the footer: it pushed SETTINGS - the only control
        // in that bar - away from the right edge, and repeated what the title bar already says.
        Assert.DoesNotContain("class=\"footer-product\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"settings-action\"", layout, StringComparison.Ordinal);
        Assert.Contains(".host-nav{display:flex!important;flex:0 0 auto;align-items:center;gap:8px!important;margin:0 0 0 auto!important}", css, StringComparison.Ordinal);
        Assert.Contains("FolderPicker.PickFolderAsync", settings, StringComparison.Ordinal);
        Assert.Contains("class=\"blocking-busy-overlay\"", settings, StringComparison.Ordinal);
        Assert.Contains("Task.Run(work)", settings, StringComparison.Ordinal);
        Assert.Contains("class=\"modal-accent\"", modal, StringComparison.Ordinal);
        Assert.DoesNotContain("Close dialog", modal, StringComparison.Ordinal);
        Assert.Contains("Common_Working", modal, StringComparison.Ordinal);
        Assert.Contains("Common_Notifications", toast, StringComparison.Ordinal);

        Assert.Contains("transform:scale(1.02);opacity:.92", css, StringComparison.Ordinal);
        Assert.Contains("transform:scale(.97);background-color:var(--orange-dim)", css, StringComparison.Ordinal);
        Assert.Contains(".modal-backdrop{position:fixed;inset:0;z-index:100", css, StringComparison.Ordinal);
        Assert.Contains("width:min(520px,100%)", css, StringComparison.Ordinal);
        Assert.Contains(".modal-accent{height:3px;background:var(--orange)}", css, StringComparison.Ordinal);
        Assert.Contains(".toast-stack{position:fixed", css, StringComparison.Ordinal);
        Assert.Contains(".blocking-busy-spinner{width:40px;height:40px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Loaded_world_sidebar_matches_the_retired_five_column_workbench()
    {
        var shell = Source("Components", "Shared", "WorkspaceShell.razor");
        var shellCode = Source("Components", "Shared", "WorkspaceShell.razor.cs");
        var css = Source("wwwroot", "parity.css");

        Assert.Contains("class=\"file-sidebar-header\"", shell, StringComparison.Ordinal);
        Assert.Contains("FileSidebar_SearchSavesAndConfig", shell, StringComparison.Ordinal);
        Assert.Contains("class=\"save-group-heading\"", shell, StringComparison.Ordinal);
        Assert.Contains("class=\"save-kind-marker\"", shell, StringComparison.Ordinal);
        Assert.Contains("class=\"save-file-meta\"", shell, StringComparison.Ordinal);
        Assert.Contains("class=\"config-file-list\"", shell, StringComparison.Ordinal);
        Assert.Contains("IniSessions.OpenDiscovered(worldFolder, config.FullPath)", shell, StringComparison.Ordinal);
        Assert.Contains("/ini/{Uri.EscapeDataString(Path.GetFileName(config.FullPath))}", shell, StringComparison.Ordinal);
        Assert.Contains("aria-current=", shell, StringComparison.Ordinal);
        Assert.Contains("Main_GroupWorldStory", shell, StringComparison.Ordinal);
        Assert.Contains("Main_GroupPlayers", shell, StringComparison.Ordinal);
        Assert.Contains("Main_GroupWorldRegions", shell, StringComparison.Ordinal);
        Assert.Contains("save.RelativePath", shell, StringComparison.Ordinal);
        Assert.Contains("SteamPersonaIndex.IdFromPlayerPath", shell, StringComparison.Ordinal);
        Assert.Contains("persona", shell, StringComparison.Ordinal);
        Assert.Contains("Main_ChipSandbox", shellCode, StringComparison.Ordinal);
        Assert.Contains("Path.GetDirectoryName(config.FullPath)", shellCode, StringComparison.Ordinal);
        // Relative, not "/": the published browser editor lives in a sub-folder, where a
        // rooted path leaves the editor entirely and lands on a 404.
        Assert.Contains("Navigation.NavigateTo(\"./\")", shell, StringComparison.Ordinal);

        Assert.Contains("grid-template-columns:var(--file-pane-width) 16px minmax(0,1fr) 16px var(--details-pane-width)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows:auto minmax(0,1fr) auto", css, StringComparison.Ordinal);
        Assert.Contains("height:38px", css, StringComparison.Ordinal);
        Assert.Contains(".save-group-heading", css, StringComparison.Ordinal);
        Assert.Contains(".save-file-row.selected", css, StringComparison.Ordinal);
        Assert.Contains(".config-file-list{max-height:160px", css, StringComparison.Ordinal);
        Assert.Contains(".shell-splitter{position:relative", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_chrome_does_not_embed_english_interface_copy()
    {
        var mainLayout = Source("Components", "Pages", "MainLayout.razor");
        var settings = Source("Components", "Pages", "Settings.razor");
        var workspace = Source("Components", "Shared", "WorkspaceShell.razor");
        var slotEditor = Source("Components", "Player", "InventorySlotEditor.razor");

        foreach (var literal in new[] { "SAVE EDITOR", "ACTIVE SAVE FOLDER", "NONE SELECTED", "More tools", ">MORE<", "READY ·", "SAVE(S)" })
            Assert.DoesNotContain(literal, mainLayout, StringComparison.Ordinal);
        foreach (var literal in new[] { ">ACCENT<", "CASCADE · FACILITY BLUE", "HAZARD · AMBER CRT", "System follows your computer" })
            Assert.DoesNotContain(literal, settings, StringComparison.Ordinal);
        foreach (var literal in new[] { "ITEM(S)", "No saves match", "NEW PLAYER STEAMID64", "Copy selected player", ">ADD PLAYER<", ">SLOT EDITOR<", "Resize save files pane", "Resize slot editor pane" })
            Assert.DoesNotContain(literal, workspace, StringComparison.Ordinal);
        foreach (var literal in new[] { "Tap any inventory slot", "TELEPORTER SYNC", "Crafting bench", "Choose a bench", ">DISMANTLE<", "CONFIRM DISMANTLE", "ITEM CATALOG", "Search items by name or ID", "to this slot" })
            Assert.DoesNotContain(literal, slotEditor, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts) => UiSource.ReadAllText(parts);
}
