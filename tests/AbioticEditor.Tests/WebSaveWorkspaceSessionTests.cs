using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Exercises the UI host-neutral save sessions against real save fixtures.  These tests use
/// disposable copies so the Razor editing workflow is verified without modifying fixtures.
/// </summary>
public sealed class WebSaveWorkspaceSessionTests
{
    [Fact]
    public async Task Workspace_opens_and_selects_player_and_world_saves()
    {
        using var world = CopyCascadeWorld();
        using var session = CreateWorkspace();

        var opened = await session.OpenAsync(world.Path);
        Assert.Equal(SavePlatform.Steam, opened.Platform);
        Assert.Contains(opened.Saves, save => save.Kind == SaveDocumentKind.Player);
        Assert.Contains(opened.Saves, save => save.Kind == SaveDocumentKind.WorldMetadata);

        var player = opened.Saves
            .Where(save => save.Kind == SaveDocumentKind.Player)
            .OrderBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        var selectedPlayer = await session.SelectAsync(player.Path);
        var playerSummary = Assert.IsType<PlayerSaveSummary>(selectedPlayer.Summary);
        Assert.NotNull(selectedPlayer.PlayerSession);
        Assert.Null(selectedPlayer.WorldSession);
        Assert.Equal(player.Path, playerSummary.Save.Path);

        var metadata = Assert.Single(opened.Saves, save => save.Kind == SaveDocumentKind.WorldMetadata);
        var selectedWorld = await session.SelectAsync(metadata.Path);
        Assert.IsType<WorldSaveSummary>(selectedWorld.Summary);
        Assert.NotNull(selectedWorld.WorldSession);
        Assert.Null(selectedWorld.PlayerSession);

        var region = Assert.Single(opened.Saves, save =>
            save.Kind == SaveDocumentKind.World
            && save.Name.Equals("WorldSave_Facility.sav", StringComparison.OrdinalIgnoreCase));
        var selectedRegion = await session.SelectAsync(region.Path);
        Assert.Equal(region.Path, selectedRegion.SelectedSave?.Path);
        Assert.Equal(region.Path, selectedRegion.WorldSession?.Path);
        Assert.IsType<WorldSaveSummary>(selectedRegion.Summary);
        Assert.Null(selectedRegion.PlayerSession);
    }

    [Fact]
    public async Task Selecting_a_save_publishes_the_choice_before_parsing_finishes()
    {
        using var world = CopyCascadeWorld();
        using var session = CreateWorkspace();
        var opened = await session.OpenAsync(world.Path);
        var player = opened.Saves.First(save => save.Kind == SaveDocumentKind.Player);
        var selectionWasPublishedWhileBusy = false;
        session.Changed += () =>
        {
            if (session.BusyOperation is not null && session.Current?.SelectedSave?.Path == player.Path)
                selectionWasPublishedWhileBusy = true;
        };

        var selected = await session.SelectAsync(player.Path);

        Assert.True(selectionWasPublishedWhileBusy);
        Assert.Equal(player.Path, selected.PlayerSession?.Path);
    }

    [Fact]
    public async Task Discovered_world_retains_its_real_platform_badge()
    {
        Assert.NotNull(Fixtures.ClientSavedDir);
        var world = Assert.Single(
            SaveDiscovery.DiscoverClientWorlds(Fixtures.ClientSavedDir!)
                .Where(candidate => candidate.Platform == SavePlatform.Steam)
                .Take(1));
        using var session = CreateWorkspace();

        var opened = await session.OpenAsync(world);

        Assert.Equal(SavePlatform.Steam, opened.Platform);
        Assert.Equal(world.Source, opened.Source);
    }

    [Fact]
    public void Ini_sidebar_session_opens_a_real_discovered_config_file()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var session = new IniEditorSessionService();

        session.Discover(Fixtures.CascadeDir!);
        var sandbox = Assert.Single(session.Files, file => file.Kind == AbioticEditor.Core.Ini.AbioticIniKind.SandboxSettings);
        session.Open(sandbox.FullPath);

        Assert.NotNull(session.Current);
        Assert.Equal(sandbox.FullPath, session.Current.File.FullPath);
        Assert.NotEmpty(session.Current.Sections);
    }

    [Fact]
    public void Ini_route_reconstructs_the_catalog_and_selected_document()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var discovered = AbioticEditor.Core.Ini.AbioticIniCatalog.Discover(Fixtures.CascadeDir!);
        var sandbox = Assert.Single(discovered, file => file.Kind == AbioticEditor.Core.Ini.AbioticIniKind.SandboxSettings);
        var session = new IniEditorSessionService();

        session.OpenDiscovered(Fixtures.CascadeDir!, sandbox.FullPath);

        Assert.Equal(discovered, session.Files);
        Assert.Equal(sandbox.FullPath, session.Current?.File.FullPath);
        Assert.NotEmpty(session.Current!.Sections);
    }

    [Fact]
    public void Failed_ini_route_keeps_the_current_document_open()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var discovered = AbioticEditor.Core.Ini.AbioticIniCatalog.Discover(Fixtures.CascadeDir!);
        var sandbox = Assert.Single(discovered, file => file.Kind == AbioticEditor.Core.Ini.AbioticIniKind.SandboxSettings);
        var session = new IniEditorSessionService();
        session.OpenDiscovered(Fixtures.CascadeDir!, sandbox.FullPath);

        Assert.Throws<InvalidOperationException>(() =>
            session.OpenDiscovered(Fixtures.CascadeDir!, Path.Combine(Fixtures.CascadeDir!, "not-catalogued.ini")));

        Assert.Equal(sandbox.FullPath, session.Current?.File.FullPath);
        Assert.Equal(discovered, session.Files);
    }

    [Fact]
    public void Ini_filename_route_opens_the_matching_world_config()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var session = new IniEditorSessionService();

        session.OpenNamedDiscovered(Fixtures.CascadeDir!, "SandboxSettings.ini");

        Assert.Equal("SandboxSettings.ini", session.Current?.FileName);
        Assert.NotEmpty(session.Current!.Sections);
    }

    [Fact]
    public void Opening_an_ini_notifies_the_active_editor_page()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var session = new IniEditorSessionService();
        var changes = 0;
        session.Changed += () => changes++;

        session.OpenNamedDiscovered(Fixtures.CascadeDir!, "SandboxSettings.ini");

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Player_session_stages_reverts_and_saves_with_backup()
    {
        using var world = CopyCascadeWorld();
        var playerPath = FindPlayer(world.Path);
        var originalBytes = File.ReadAllBytes(playerPath);
        var data = PlayerSaveReader.ReadFromFile(playerPath);
        var session = new PlayerSaveSession(data, playerPath);
        var originalMoney = session.Vitals.Money;
        Assert.False(session.IsDirty);

        session.Vitals.Money = originalMoney + 137;
        session.MarkChanged();
        Assert.True(session.IsDirty);
        Assert.Equal("Unsaved changes", session.Status);
        Assert.Equal(originalBytes, File.ReadAllBytes(playerPath));

        session.Revert();
        Assert.False(session.IsDirty);
        Assert.Equal(originalMoney, session.Vitals.Money);
        Assert.Equal("Changes reverted.", session.Status);
        Assert.Equal(originalBytes, File.ReadAllBytes(playerPath));

        session.Vitals.Money = originalMoney + 137;
        await session.SaveAsync();

        Assert.False(session.IsDirty);
        Assert.True(File.Exists(playerPath + ".bak"));
        Assert.Equal(originalBytes, File.ReadAllBytes(playerPath + ".bak"));
        Assert.Equal((int)(originalMoney + 137), PlayerSaveReader.ReadFromFile(playerPath).Stats.Money);
    }

    [Fact]
    public async Task World_session_stages_reverts_and_saves_with_backup()
    {
        using var world = CopyCascadeWorld();
        // The metadata save intentionally has no WorldFlags array. Facility is the fixture
        // world whose schema supports the flag editor.
        var worldPath = Path.Combine(world.Path, "WorldSave_Facility.sav");
        var originalBytes = File.ReadAllBytes(worldPath);
        var data = WorldSaveReader.ReadFromFile(worldPath);
        var session = new WorldSaveSession(data, worldPath);
        Assert.True(session.CanEditFlags);
        const string sentinel = "WebHostSession_TestFlag";

        session.SetFlag(sentinel, true);
        Assert.True(session.IsDirty);
        Assert.Contains(sentinel, session.Flags);
        Assert.Equal(originalBytes, File.ReadAllBytes(worldPath));

        session.Revert();
        Assert.False(session.IsDirty);
        Assert.DoesNotContain(sentinel, session.Flags);
        Assert.Equal(originalBytes, File.ReadAllBytes(worldPath));

        session.SetFlag(sentinel, true);
        await session.SaveAsync();

        Assert.False(session.IsDirty);
        Assert.True(File.Exists(worldPath + ".bak"));
        Assert.Equal(originalBytes, File.ReadAllBytes(worldPath + ".bak"));
        Assert.Contains(sentinel, WorldSaveReader.ReadFromFile(worldPath).Flags);
    }

    [Fact]
    public async Task World_session_stages_reverts_and_saves_global_recipes_with_backup()
    {
        using var world = CopyCascadeWorld();
        // GlobalRecipes (GlobalUnlocks.GlobalRecipesUnlocked_/Researched_) only exists on the
        // metadata save; region saves like Facility don't carry it.
        var metadataPath = Path.Combine(world.Path, "WorldSave_MetaData.sav");
        var originalBytes = File.ReadAllBytes(metadataPath);
        var data = WorldSaveReader.ReadFromFile(metadataPath);
        var session = new WorldSaveSession(data, metadataPath);
        Assert.True(session.CanEditGlobalRecipes);
        var originalRecipes = new HashSet<string>(session.GlobalRecipes, StringComparer.Ordinal);
        const string sentinel = "WebHostSession_TestRecipe";

        session.SetGlobalRecipe(sentinel, true);
        Assert.True(session.IsDirty);
        Assert.Contains(sentinel, session.GlobalRecipes);
        Assert.Equal(originalBytes, File.ReadAllBytes(metadataPath));

        session.Revert();
        Assert.False(session.IsDirty);
        Assert.DoesNotContain(sentinel, session.GlobalRecipes);
        Assert.Equal(originalRecipes, session.GlobalRecipes);
        Assert.Equal(originalBytes, File.ReadAllBytes(metadataPath));

        session.SetGlobalRecipe(sentinel, true);
        await session.SaveAsync();

        Assert.False(session.IsDirty);
        Assert.True(File.Exists(metadataPath + ".bak"));
        Assert.Equal(originalBytes, File.ReadAllBytes(metadataPath + ".bak"));
        var reread = WorldSaveReader.ReadFromFile(metadataPath);
        Assert.Contains(sentinel, reread.GlobalRecipes);
        foreach (var recipe in originalRecipes) Assert.Contains(recipe, reread.GlobalRecipes);
    }

    private static SaveWorkspaceSessionService CreateWorkspace()
        => new(new RecipeVocabularyService(), new ProgressionVocabularyService(), new CodexVocabularyService(), new DesktopSaveFileSystem());

    private static string FindPlayer(string worldPath)
        => Directory.EnumerateFiles(Path.Combine(worldPath, "PlayerData"), "Player_*.sav")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();

    private static TempWorld CopyCascadeWorld()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var directory = Directory.CreateTempSubdirectory("web-save-session-");
        foreach (var source in Directory.EnumerateFiles(Fixtures.CascadeDir!, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(directory.FullName, Path.GetRelativePath(Fixtures.CascadeDir!, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        return new TempWorld(directory);
    }

    private sealed class TempWorld(DirectoryInfo directory) : IDisposable
    {
        public string Path => directory.FullName;
        public void Dispose()
        {
            try { directory.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
