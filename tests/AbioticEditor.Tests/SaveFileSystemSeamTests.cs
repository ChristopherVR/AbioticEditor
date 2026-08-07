using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers saving through <see cref="ISaveFileSystem"/> rather than straight to disk.
/// </summary>
/// <remarks>
/// The sessions accept a null file system and then write with <c>System.IO</c> directly, which
/// is what every older session test exercises. Both hosts now pass a real one instead, so
/// without these the path the shipping app actually takes would be untested - and the promise
/// that matters most (a <c>.bak</c> exists before anything is overwritten) would be unguarded
/// on exactly the code that keeps it.
/// </remarks>
public sealed class SaveFileSystemSeamTests
{
    [Fact]
    public async Task Player_save_through_the_file_system_seam_keeps_a_backup_and_applies_the_edit()
    {
        using var world = CopyCascadeWorld();
        var playerPath = FindPlayer(world.Path);
        var originalBytes = File.ReadAllBytes(playerPath);

        var data = PlayerSaveReader.ReadFromFile(playerPath);
        var session = new PlayerSaveSession(data, playerPath, files: new DesktopSaveFileSystem());
        var money = session.Vitals.Money;

        session.Vitals.Money = money + 42;
        session.MarkChanged();
        await session.SaveAsync();

        Assert.True(File.Exists(playerPath + ".bak"), "the seam must back up before overwriting");
        Assert.Equal(originalBytes, File.ReadAllBytes(playerPath + ".bak"));
        Assert.Equal((int)(money + 42), PlayerSaveReader.ReadFromFile(playerPath).Stats.Money);
    }

    [Fact]
    public async Task World_save_through_the_file_system_seam_keeps_a_backup_and_applies_the_edit()
    {
        using var world = CopyCascadeWorld();
        var worldPath = Path.Combine(world.Path, "WorldSave_Facility.sav");
        var originalBytes = File.ReadAllBytes(worldPath);

        var data = WorldSaveReader.ReadFromFile(worldPath);
        var session = new WorldSaveSession(data, worldPath, new DesktopSaveFileSystem());
        var flag = "AbioticEditorSeamProbe";

        session.Flags.Add(flag);
        await session.SaveAsync();

        Assert.True(File.Exists(worldPath + ".bak"), "the seam must back up before overwriting");
        Assert.Equal(originalBytes, File.ReadAllBytes(worldPath + ".bak"));
        Assert.Contains(flag, WorldSaveReader.ReadFromFile(worldPath).Flags);
    }

    /// <summary>
    /// The whole path the app takes: open a folder, pick a save, edit it, save. Everything here
    /// - discovery, the header probe that classifies each file, reading, and writing - now goes
    /// through the seam, so this is the end-to-end guard that it is wired up correctly.
    /// </summary>
    [Fact]
    public async Task Workspace_opens_selects_and_saves_entirely_through_the_seam()
    {
        using var world = CopyCascadeWorld();
        using var workspace = new SaveWorkspaceSessionService(
            new RecipeVocabularyService(), new ProgressionVocabularyService(),
            new CodexVocabularyService(), new DesktopSaveFileSystem());

        var opened = await workspace.OpenAsync(world.Path);
        Assert.True(workspace.HasLocalPaths);
        Assert.NotEmpty(opened.Saves);

        // Discovery must still classify saves from their header, not merely their name.
        var player = opened.Saves.First(save => save.Kind == SaveDocumentKind.Player);
        Assert.Contains("CharacterSave", player.SaveClass, StringComparison.OrdinalIgnoreCase);

        await workspace.SelectAsync(player.Path);
        var session = workspace.Current!.PlayerSession!;
        var money = session.Vitals.Money;
        session.Vitals.Money = money + 7;
        session.MarkChanged();

        await workspace.SaveSelectedAsync();

        Assert.True(File.Exists(player.Path + ".bak"));
        Assert.Equal((int)(money + 7), PlayerSaveReader.ReadFromFile(player.Path).Stats.Money);
    }

    private static string FindPlayer(string worldPath)
        => Directory.EnumerateFiles(Path.Combine(worldPath, "PlayerData"), "Player_*.sav")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();

    private static TempWorld CopyCascadeWorld()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var directory = Directory.CreateTempSubdirectory("save-fs-seam-");
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
            try { directory.Delete(recursive: true); }
            catch (IOException) { }
        }
    }
}
