namespace AbioticEditor.Tests;

/// <summary>
/// Guards the one-for-one ownership map used to port the retired desktop UI.
/// This list is intentionally exhaustive: adding or renaming a counterpart
/// requires an explicit decision here instead of silently dropping a surface.
/// </summary>
public sealed class UiParityContractTests
{
    private static readonly ParitySurface[] Surfaces =
    [
        Surface("App.xaml", "Components/App.razor"),
        Surface("AppShell.xaml", "Components/Routes.razor", "Components/Pages/MainLayout.razor"),
        Surface("MainPage.xaml", "Components/Pages/MainLayout.razor", "Components/Pages/Home.razor", "Components/Shared/WorkspaceShell.razor"),
        Surface("Platforms/Windows/App.xaml", "Program.cs"),
        Surface("Resources/Styles/AbioticStyles.xaml", "wwwroot/parity.css"),
        Surface("Resources/Styles/Colors.xaml", "wwwroot/parity.css"),
        Surface("Resources/Styles/Styles.xaml", "wwwroot/parity.css"),
        Surface("Views/DialogHostView.xaml", "Components/Shared/ModalHost.razor"),
        Surface("Views/EmptyStateView.xaml", "Components/Pages/Home.razor"),
        Surface("Views/FileSidebarView.xaml", "Components/Shared/WorkspaceShell.razor", "Components/Shared/WorkspaceShell.razor.css"),
        Surface("Views/HeaderBarView.xaml", "Components/Pages/MainLayout.razor"),
        Surface("Views/IniEditorView.xaml", "Components/Pages/IniEditor.razor"),
        Surface("Views/Player/PlayerAchievementsTab.xaml", "Components/Player/PlayerAchievementsTab.razor"),
        Surface("Views/Player/PlayerCharacterTab.xaml", "Components/Player/PlayerCharacterTab.razor"),
        Surface("Views/Player/PlayerCodexTab.xaml", "Components/Player/PlayerCodexTab.razor"),
        Surface("Views/Player/PlayerEditorView.xaml", "Components/Player/PlayerEditor.razor", "Components/Player/PlayerEditor.razor.css"),
        Surface("Views/Player/PlayerGeneralTab.xaml", "Components/Player/PlayerGeneralTab.razor"),
        Surface("Views/Player/PlayerInventoryTab.xaml", "Components/Player/PlayerInventoryTab.razor", "Components/Player/PlayerInventoryTab.razor.css"),
        Surface("Views/Player/PlayerPetsTab.xaml", "Components/Player/PlayerCompanionsTab.razor"),
        Surface("Views/Player/PlayerRawTab.xaml", "Components/Player/PlayerRawDataTab.razor"),
        Surface("Views/Player/PlayerRecipesTab.xaml", "Components/Player/PlayerRecipesTab.razor"),
        Surface("Views/Player/PlayerSkillsTab.xaml", "Components/Player/PlayerSkillsTab.razor"),
        Surface("Views/Player/PlayerSpawnTab.xaml", "Components/Player/PlayerSpawnTab.razor"),
        Surface("Views/Player/PlayerTransmogTab.xaml", "Components/Player/PlayerTransmogTab.razor"),
        Surface("Views/Player/PlayerVitalsTab.xaml", "Components/Player/PlayerVitalsTab.razor"),
        Surface("Views/SlotSidebarView.xaml", "Components/Player/InventorySlotEditor.razor", "Components/Player/InventorySlotEditor.razor.css"),
        Surface("Views/StatusBarView.xaml", "Components/Pages/MainLayout.razor"),
        Surface("Views/World/WorldBasesTab.xaml", "Components/World/WorldBasesTab.razor"),
        Surface("Views/World/WorldContainersTab.xaml", "Components/World/WorldContainersTab.razor"),
        Surface("Views/World/WorldContainmentTab.xaml", "Components/World/WorldContainmentTab.razor"),
        Surface("Views/World/WorldDoorsTab.xaml", "Components/World/WorldDoorsTab.razor"),
        Surface("Views/World/WorldDroppedTab.xaml", "Components/World/WorldDroppedItemsTab.razor"),
        Surface("Views/World/WorldEditorView.xaml", "Components/Pages/SaveEditorSurface.razor"),
        Surface("Views/World/WorldFeatureTab.xaml", "Components/World/WorldFeaturesTab.razor", "Components/World/WorldEntitlementsTab.razor"),
        Surface("Views/World/WorldFlagsTab.xaml", "Components/World/WorldFlagsTab.razor"),
        // The native app retired its dedicated NPCs tab; NPC editing surfaces as the
        // NPC SPAWNS world-map feature, rendered by the shared feature editor.
        Surface("Views/World/WorldNpcsTab.xaml", "Components/World/WorldFeaturesTab.razor"),
        Surface("Views/World/WorldPetsTab.xaml", "Components/World/WorldPetsTab.razor"),
        Surface("Views/World/WorldRawTab.xaml", "Components/World/WorldRawTab.razor"),
        Surface("Views/World/WorldStoryTab.xaml", "Components/World/WorldStoryTab.razor"),
        Surface("Views/World/WorldTradersTab.xaml", "Components/World/WorldTradersTab.razor"),
        Surface("Views/World/WorldVehiclesTab.xaml", "Components/World/WorldVehiclesTab.razor"),
    ];

    [Fact]
    public void Every_retired_desktop_surface_has_an_owned_Blazor_counterpart()
    {
        Assert.Equal(41, Surfaces.Length);
        Assert.Equal(Surfaces.Length, Surfaces.Select(surface => surface.NativeSource).Distinct(StringComparer.Ordinal).Count());

        foreach (var surface in Surfaces)
        {
            Assert.NotEmpty(surface.BlazorTargets);
            foreach (var target in surface.BlazorTargets)
                Assert.True(File.Exists(UiSource.Resolve(target)),
                    $"{surface.NativeSource} has a missing Blazor counterpart: {target}");
        }
    }

    [Fact]
    public void Native_reference_screenshots_cover_each_primary_workbench()
    {
        var references = new[]
        {
            "01-loaded.png", "10-player-vitals.png", "11-player-inventory.png",
            "12-player-skills.png", "13-player-recipes.png", "14-player-character.png",
            "15-player-gatepal.png", "16-player-transmog.png", "20-world.png",
            "21-world-questflags.png", "22-world-npcs.png", "25-config-ini.png",
            "30-settings.png", "31-compare.png",
        };

        foreach (var reference in references)
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "docs", "public", "screenshots", reference)),
                $"Missing UI parity reference screenshot: {reference}");
    }

    [Fact]
    public void Shared_visual_contract_preserves_native_geometry_typography_and_states()
    {
        var css = UiSource.ReadAllText("wwwroot", "parity.css");
        foreach (var rule in new[]
        {
            "--shell:#081119", "--page:#0c1a24", "--panel:#132736", "--elevated:#1b3648",
            "--hover:#26475d", "--divider:#2e5471", "--line:#224158", "--ink:#dceff9",
            "--orange:#f89a4f", "--hazard:#ffe563", "--terminal:#7fe9e2", "--section:#71c5f6",
            "grid-template-columns:var(--file-pane-width) 16px minmax(0,1fr) 16px var(--details-pane-width)",
            // The seven-segment "Digital7" readout face was retired: it made numbers hard to
            // read on phones and could not draw non-Latin translated readouts. Numbers now use
            // the shared --font-num face, so the contract asserts that token exists instead.
            "--font-num:", "border-radius:6px", "min-height:36px",
            ":hover", ":active", ":focus-visible", "prefers-reduced-motion:reduce",
        })
            Assert.Contains(rule, css, StringComparison.OrdinalIgnoreCase);

        var preferences = UiSource.ReadAllText("Services", "ShellPreferencesService.cs");
        Assert.Contains("new(340, 400, false, false)", preferences, StringComparison.Ordinal);
    }

    private static ParitySurface Surface(string source, params string[] targets) => new(source, targets);

    private static string RepositoryRoot() => UiSource.RepositoryRoot;

    private sealed record ParitySurface(string NativeSource, string[] BlazorTargets);
}
