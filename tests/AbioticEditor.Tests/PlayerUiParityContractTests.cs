namespace AbioticEditor.Tests;

using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Structural contract derived from the last native player UI at faafc74^.
/// It intentionally maps every retired Player/*.xaml view to its Razor replacement.
/// </summary>
public sealed class PlayerUiParityContractTests
{
    public static TheoryData<string, string[]> ViewMatrix => new()
    {
        { "PlayerEditor.razor", ["player-tab-viewport", "role=\"tablist\"", "PlayerGeneralTab", "PlayerVitalsTab", "PlayerCharacterTab", "PlayerTransmogTab", "PlayerSpawnTab", "PlayerInventoryTab", "PlayerCompanionsTab", "PlayerSkillsTab", "PlayerRecipesTab", "PlayerCodexTab", "PlayerAchievementsTab", "PlayerRawDataTab"] },
        { "PlayerGeneralTab.razor", ["ChangeSelectedPlayerIdentifierAsync", "RequestRecipes", "RequestItems", "RequestCrafted", "RequestMaps", "UnlockAllRecipesGated"] },
        // Native heals immediately (no confirmation dialog) and pulls its copy from resx.
        { "PlayerVitalsTab.razor", ["vital-list", "money-field", "limb-grid", "HealAll", "PlayerVitals_HealAll"] },
        // Native adds/removes traits directly (no confirmation dialog), so the contract
        // tracks the browser + chip actions instead of a confirm step.
        { "PlayerCharacterTab.razor", ["TraitCatalog", "VisibleTraitOptions", "PlayerAppearanceEditor", "AddTrait", "RemoveTrait"] },
        // Native edits Game Pass appearance through the account's ProfileScientistCustomization
        // wgs container (CustomizationViewModel + GamePassSaveSet); the web reaches the same
        // container from a Game-Pass-origin workspace instead of silently showing nothing.
        // The "no saved look yet, go and customise your character" paragraph is deliberately
        // not part of this contract any more - it was dropped as too wordy for what it said.
        { "PlayerAppearanceEditor.razor", ["Customization_AvailableCaptionGamePass", "Customization_StatusEditingGamePass", "Customization_StatusNotFoundGamePass", "TryLocateGamePassStore", "LoadGamePass"] },
        // Transmog is a bespoke faithful port of its own: a role-mapped slot grid
        // with drag-and-drop and the game's EquipSlot eligibility rule, visibility toggles.
        // The item palette itself lives in the sidebar slot editor (native SlotSidebarView).
        { "PlayerTransmogTab.razor", ["transmog-grid", "@ondragstart", "@ondrop", "ValidateForRole", "FillFromCatalog", "TransmogVisibility", "SetVisibility"] },
        // The sidebar hosts the ITEM CATALOG whenever the inventory/transmog tab is open
        // (native ShowItemPalette), with the FITS SLOT filter, drag sources, double-click
        // quick-give and the native showing-of/LOAD MORE paging.
        { "InventorySlotEditor.razor", ["item-catalog", "Palette_FitsSlot", "Selection.Palette", "QuickGive", "BeginPaletteDrag", "Palette_ShowingOf", "Slot_LoadMore"] },
        // Faithful native contract: picking a terminal or region snaps the coordinates
        // immediately (no confirm), and only a bed claimed by another player confirms.
        { "PlayerSpawnTab.razor", ["RespawnTerminalCatalog", "TerminalSelected", "RegionSelected", "SetSpawnToBed", "Main_BedOtherPlayerTitle", "PlayerSpawn_HomeBed"] },
        // The inventory tab renders the native paper-doll (equipment grid, numbered hotbar
        // column, pockets grid) with drag-and-drop slot swapping and ground pickups.
        { "PlayerInventoryTab.razor", ["paper-doll", "hotbar-column", "pockets-grid", "DragStart", "@ondrop", "TrySwapInventorySlots", "DropActive", "PickUp", "SortBackpack"] },
        // Cross-save bed discovery mirrors native: sibling world saves are scanned read-only
        // for pet beds, and a send stages into a world session finished by SAVE WORLD.
        { "PlayerCompanionsTab.razor", ["CarriedPets", "ToggleDeleted", "Heal", "SendToBed", "SiblingWorlds", "GetBedsAsync", "GetOrLoadSessionAsync", "SaveTargetWorld", "PlayerPets_SendToAPetBed", "PlayerEditor_PetPlacedAt"] },
        // Native skills apply MAX ALL immediately and expose the per-skill XP RATE field.
        { "PlayerSkillsTab.razor", ["skill-grid", "MaxAll", "MultiplierPercent", "milestone-track"] },
        // Faithful native contract: RECIPE BOOK header, category chips, spoiler-sealed rows
        // and the wiki-style detail pane for the selected recipe.
        { "PlayerRecipesTab.razor", ["rb-toolbar", "VisibleRows", "SetUnlocked", "UnlockAll", "SelectRow", "rb-chip", "rb-detail", "PlayerRecipes_RecipeBook", "Gate.CheckUnlock", "Progress_RecipeGated", "TraderVocabulary"] },
        // GATEPal keeps the native PDA app tiles and UNREAD ONLY filter, loads the codex
        // vocabulary on demand when it opens, and its bait chips open the item's
        // encyclopedia card in the right-hand pane like the native tap.
        { "PlayerCodexTab.razor", ["gatepal-apps", "UnreadOnly", "MarkAll", "SetKnown", "ApplyCodexVocabulary", "ShowEncyclopedia", "PlayerCodex_Open"] },
        { "PlayerAchievementsTab.razor", ["SteamAchievements.LoadFor", "_showSpoilers", "OpenCommunityAsync", "Reveal", "SteamWebAchievements.FetchAsync", "SteamGameDetailsPrivateException", "achievements-gated", "TryCreateSignInAndViewAchievements", "PrivacySettings", "CompareCandidates", "LoadCompareAsync", "PlayerAchievements_CompareWith"] },
        // The native DATA tab is only the export -> edit externally -> import JSON workflow.
        { "PlayerRawDataTab.razor", ["ExportJsonAsync", "ImportJsonAsync", "PlayerRaw_RawSaveJson"] },
    };

    [Theory]
    [MemberData(nameof(ViewMatrix))]
    public void Every_retired_player_view_has_its_interactive_Razor_contract(string file, string[] required)
    {
        var source = PlayerSource(file);
        foreach (var token in required) Assert.Contains(token, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_layout_matches_native_bounded_tabs_limb_columns_and_inventory_panes()
    {
        var editorCss = PlayerSource("PlayerEditor.razor.css");
        var inventoryCss = PlayerSource("PlayerInventoryTab.razor.css");
        Assert.Contains("grid-template-rows:auto minmax(0,1fr)", editorCss, StringComparison.Ordinal);
        Assert.Contains("overflow:auto", editorCss, StringComparison.Ordinal);
        Assert.Contains(".limb-grid{display:grid;grid-template-columns:repeat(2", editorCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:minmax(240px,2fr) 126px minmax(360px,3fr)", inventoryCss, StringComparison.Ordinal);
        // The paper-doll, the numbered hotbar column and the pockets grid each keep their own
        // slot grid; the tiles themselves stay the native height.
        Assert.Contains("min-height:98px", inventoryCss, StringComparison.Ordinal);
        Assert.Contains(".hotbar-column", inventoryCss, StringComparison.Ordinal);
        Assert.Contains(".pockets-grid", inventoryCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Inventory_slots_support_native_click_drag_drop_and_selected_states()
    {
        var tab = PlayerSource("PlayerInventoryTab.razor");
        var css = PlayerSource("PlayerInventoryTab.razor.css");
        foreach (var token in new[] { "@onclick", "draggable=", "@ondragstart", "@ondragover:preventDefault", "@ondrop", "TrySwapInventorySlots" })
            Assert.Contains(token, tab, StringComparison.Ordinal);
        Assert.Contains(".inv-slot.selected", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_primary_headings_and_controls_resolve_copy_from_resx_resources()
    {
        var general = PlayerSource("PlayerGeneralTab.razor");
        var character = PlayerSource("PlayerCharacterTab.razor");
        var inventory = PlayerSource("PlayerInventoryTab.razor");
        var achievements = PlayerSource("PlayerAchievementsTab.razor");
        var codex = PlayerSource("PlayerCodexTab.razor");
        foreach (var pair in new[]
                 {
                     (general, "PlayerGeneral_General"), (character, "PlayerCharacter_Character"),
                     (inventory, "PlayerInventory_Inventory"), (achievements, "PlayerAchievements_SteamAchievements"),
                     (codex, "PlayerCodex_FooterHelp"),
                 })
            Assert.Contains($"L.Resource(\"{pair.Item2}\")", pair.Item1, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_ui_resource_references_exist_and_migrated_copy_does_not_return_to_Razor()
    {
        var resources = XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        var sources = UiSource.EnumerateFiles(Path.Combine("Components", "Player"), "*.razor")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.Ordinal);
        var resourceReference = new Regex("(?:L|Languages)\\.Resource\\(\\\"(?<key>PlayerUi_[^\\\"]+)", RegexOptions.CultureInvariant);
        foreach (var (file, source) in sources)
            foreach (Match match in resourceReference.Matches(source))
                Assert.Contains(match.Groups["key"].Value, resources);

        var migratedPhrases = new[]
        {
            "Appearance lives in a separate per-character save", "Loaded {_session.Fields.Count} appearance choices",
            "Move between areas", "The selected slots are no longer available", "Drop staged in both saves",
            "Mark all @Current.Count", "Hidden achievement", "Steam Community could not be opened",
            "Safe raw property editor", "Exporting complete save JSON", "Import replaces the save on disk",
            "Set spawn to a player bed", "Set every skill to level @SkillCatalog.MaxLevel",
            "The player ID could not be changed", "No installed game-data description is available",
            "$\"{Title} slot", "· level @pet.Level",
        };
        foreach (var (file, source) in sources)
            foreach (var phrase in migratedPhrases)
                Assert.DoesNotContain(phrase, source, StringComparison.Ordinal);
    }

    private static string PlayerSource(string file) => UiSource.ReadAllText("Components", "Player", file);
}
