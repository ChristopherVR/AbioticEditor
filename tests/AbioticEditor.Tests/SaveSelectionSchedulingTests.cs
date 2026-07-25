using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class SaveSelectionSchedulingTests
{
    [Fact]
    public void Resolving_the_always_visible_slot_catalog_does_not_scan_game_paks()
    {
        var progression = new ProgressionVocabularyService();

        using var catalog = new ItemCatalogService(progression);

        Assert.NotEmpty(catalog.Entries);
        Assert.False(progression.TryGet(out _, out _));
    }

    [Fact]
    public async Task Opening_and_selecting_saves_does_not_start_optional_pak_catalog_scans()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        using var copy = CopyWorld(Fixtures.CascadeDir!);
        var recipes = new RecipeVocabularyService();
        var progression = new ProgressionVocabularyService();
        var codex = new CodexVocabularyService();
        var upgrades = new ItemUpgradeVocabularyService();
        using var workspace = new SaveWorkspaceSessionService(recipes, upgrades, progression, codex);

        var opened = await workspace.OpenAsync(copy.Path);
        var player = opened.Saves.First(save => save.Kind == SaveDocumentKind.Player);
        var world = opened.Saves.First(save => save.Kind == SaveDocumentKind.World);
        await workspace.SelectAsync(player.Path).WaitAsync(TimeSpan.FromSeconds(15));
        await workspace.SelectAsync(world.Path).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(recipes.TryGetRecipes(out _));
        Assert.False(progression.TryGet(out _, out _));
        Assert.False(codex.TryGet(out _));
        Assert.False(upgrades.TryGet(out _));
    }

    private static TempWorld CopyWorld(string sourceRoot)
    {
        var destination = Directory.CreateTempSubdirectory("save-scheduling-");
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination.FullName, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return new TempWorld(destination);
    }

    private sealed class TempWorld(DirectoryInfo directory) : IDisposable
    {
        public string Path => directory.FullName;
        public void Dispose()
        {
            try { directory.Delete(recursive: true); }
            catch (IOException) { }
        }
    }
}
