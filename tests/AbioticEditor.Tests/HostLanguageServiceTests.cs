using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class HostLanguageServiceTests
{
    [Fact]
    public void Application_translations_live_only_in_resx_catalogs()
    {
        Assert.Empty(UiSource.EnumerateFiles("Services", "HostLanguageService.*Strings.cs"));

        var implementation = UiSource.ReadAllText("Services", "HostLanguageService.cs");
        Assert.DoesNotContain("Dictionary<string, IReadOnlyDictionary<string, string>>", implementation, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeStrings", implementation, StringComparison.Ordinal);
        Assert.DoesNotContain("EditorStrings", implementation, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryStrings", implementation, StringComparison.Ordinal);

        foreach (var culture in new[] { "", ".es", ".fr", ".de", ".ru" })
        {
            var resource = UiSource.Resolve("Localization", $"AppResources{culture}.resx");
            Assert.True(File.Exists(resource), $"Missing application resource catalog: {resource}");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Host_resources_cover_primary_actions_and_player_tabs(string language)
    {
        foreach (var key in new[] { "open", "choose", "save", "revert", "compare.files", "tab.inventory", "tab.achievements" })
        {
            var text = HostLanguageService.TextFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Desktop_chrome_resources_are_complete(string language)
    {
        foreach (var key in HostLanguageService.HostResourceKeys)
            Assert.True(HostLanguageService.HasTextResource(language, key), $"Missing {language} host resource: {key}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Detailed_editor_resources_cover_primary_actions(string language)
    {
        foreach (var key in new[] { "filter", "details", "confirm", "cancel", "unlock.all", "discover.all", "heal.all", "max.all", "character.stats", "body.health" })
        {
            var text = HostLanguageService.DetailFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Detailed_player_and_workspace_resources_are_complete(string language)
    {
        foreach (var key in HostLanguageService.EditorResourceKeys)
        {
            var text = HostLanguageService.EditorFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void Detailed_resources_preserve_format_placeholders()
    {
        foreach (var language in new[] { "en", "es", "fr", "de", "ru" })
        {
            Assert.Contains("{0}", HostLanguageService.EditorFor(language, "common.shown"));
            Assert.Contains("{0}", HostLanguageService.EditorFor(language, "spawn.confirm"));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Inventory_resources_are_complete(string language)
    {
        foreach (var key in HostLanguageService.InventoryResourceKeys)
        {
            Assert.True(HostLanguageService.HasInventoryResource(language, key), $"Missing {language} inventory resource: {key}");
            var text = HostLanguageService.InventoryFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Migrated_resx_covers_world_editor_surfaces(string language)
    {
        foreach (var key in new[]
        {
            "WorldBases_BaseManager", "WorldContainers_SelectAContainer", "WorldContainment_ContainmentUnits",
            "WorldDoors_State", "WorldDropped_ItemsOnTheGround", "WorldFeature_EditHint", "WorldFlags_QuestFlags",
            "WorldNpcs_NpcsIntro", "WorldPets_TamedPetsInThisWorld", "WorldStory_MainQuestProgression",
            "WorldTraders_Traders", "WorldVehicles_VehiclesInThisWorld",
        })
        {
            var text = HostLanguageService.ResourceFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Fact]
    public void Unknown_language_uses_english_resource_fallback()
        => Assert.Equal("Save", HostLanguageService.TextFor("ja", "save"));

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Game_data_language_names_live_in_each_resx_catalog(string language)
    {
        foreach (var key in new[]
                 {
                     "GameDataLanguage_en", "GameDataLanguage_de", "GameDataLanguage_es_419",
                     "GameDataLanguage_fr", "GameDataLanguage_ja", "GameDataLanguage_pt_BR",
                     "GameDataLanguage_ru", "GameDataLanguage_zh_Hans", "GameDataLanguage_zh_Hant",
                 })
        {
            var text = HostLanguageService.ResourceFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Global_shell_and_dialog_copy_lives_in_each_resx_catalog(string language)
    {
        foreach (var key in new[]
                 {
                     "Common_AppName", "Common_Working", "Common_Confirm", "Common_Notifications",
                     "Home_SaveFolderOpenFailed", "Home_SaveFolderOpenGuidance",
                     "Home_FolderPickerFailed", "Home_FolderPickerGuidance",
                     "Main_ChipPlayer", "Main_ChipMeta", "Main_ChipWorld", "Main_ChipSave",
                 })
        {
            var text = HostLanguageService.ResourceFor(language, key);
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData("es", "es-419")]
    [InlineData("de", "de")]
    [InlineData("unknown", "en")]
    public void Editor_language_maps_to_the_game_culture_set(string editorLanguage, string expected)
        => Assert.Equal(expected, HostLanguageService.MapEditorToGameData(editorLanguage));

    [Fact]
    public void Chrome_format_resources_preserve_placeholders()
    {
        foreach (var language in new[] { "en", "es", "fr", "de", "ru" })
        {
            Assert.Contains("{0}", HostLanguageService.TextFor(language, "status.loaded"));
            Assert.Contains("{0}", HostLanguageService.TextFor(language, "saves.no.matches"));
            Assert.Contains("{0}", HostLanguageService.InventoryFor(language, "matches"));
            Assert.Contains("{0}", HostLanguageService.InventoryFor(language, "addItemTitle"));
        }
    }

}
