using System.Globalization;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class WebSaveComparisonServiceTests
{
    [Fact]
    public void Compare_same_save_uses_the_semantic_core_comparer()
    {
        var path = Fixtures.CascadeDir is { } directory ? Path.Combine(directory, "WorldSave_MetaData.sav") : null;
        if (path is null || !File.Exists(path)) return;

        var result = NewService().Compare(path, path);

        Assert.NotNull(result.Save);
        Assert.True(result.Save.AreIdentical);
        Assert.Null(result.Folder);
    }

    [Fact]
    public void Compare_rejects_mixing_a_folder_and_save()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => NewService().Compare(Path.GetTempPath(), path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Reproduces the scenario a parallel agent flagged as a suspected "SAVE doesn't persist
    /// money" bug: edit a player's money, save, then folder-compare the edited copy against the
    /// original. The write always persisted (see WebSaveWorkspaceSessionTests); what folder mode
    /// was actually missing is the readable summary, so a real money change sat unnoticed in a
    /// list of hundreds of raw property rows. This asserts folder mode now surfaces it the same
    /// way file-vs-file mode always has.
    /// </summary>
    [Fact]
    public async Task Compare_folder_mode_surfaces_a_money_change_via_the_readable_summary()
    {
        if (Fixtures.CascadeDir is not { } cascadeDir) return;

        using var original = CopyCascadeWorld(cascadeDir);
        using var edited = CopyCascadeWorld(cascadeDir);

        var playerPath = FindPlayer(edited.Path);
        var data = PlayerSaveReader.ReadFromFile(playerPath);
        var session = new PlayerSaveSession(data, playerPath);
        var originalMoney = session.Vitals.Money;
        session.Vitals.Money = originalMoney + 137;
        await session.SaveAsync();

        Assert.Equal((int)(originalMoney + 137), PlayerSaveReader.ReadFromFile(playerPath).Stats.Money);

        var result = NewService().Compare(original.Path, edited.Path);

        Assert.NotNull(result.Folder);
        Assert.NotNull(result.FolderSemantics);
        var relativePlayerPath = Path.GetRelativePath(edited.Path, playerPath);
        var semantic = Assert.Contains(relativePlayerPath, (IDictionary<string, (string Kind, List<SemanticSection> Sections)>)result.FolderSemantics!);
        var progression = Assert.Single(semantic.Sections, section => section.Scalars.Any(scalar => scalar.Label == "Money"));
        var moneyScalar = Assert.Single(progression.Scalars, scalar => scalar.Label == "Money");
        Assert.Equal(originalMoney.ToString("N0", CultureInfo.CurrentCulture), moneyScalar.A);
        Assert.Equal((originalMoney + 137).ToString("N0", CultureInfo.CurrentCulture), moneyScalar.B);
    }

    private static SaveComparisonService NewService()
    {
        using var items = new ItemCatalogService();
        var semantic = new SaveSemanticDiff(items, new RecipeVocabularyService(), new CodexVocabularyService(), new HostLanguageService());
        return new SaveComparisonService(semantic);
    }

    private static string FindPlayer(string worldPath)
        => Directory.EnumerateFiles(Path.Combine(worldPath, "PlayerData"), "Player_*.sav")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();

    private static TempWorld CopyCascadeWorld(string cascadeDir)
    {
        var directory = Directory.CreateTempSubdirectory("web-compare-service-");
        foreach (var source in Directory.EnumerateFiles(cascadeDir, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(directory.FullName, Path.GetRelativePath(cascadeDir, source));
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
