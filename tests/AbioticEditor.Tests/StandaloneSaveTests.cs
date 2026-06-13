using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Users may point the editor at a folder holding ONLY player saves or ONLY the
/// metadata save (no facility/world files). Everything must degrade to "no world
/// context" instead of failing.
/// </summary>
public class StandaloneSaveTests
{
    [Fact]
    public void Folder_with_only_a_player_save_scans_and_reads()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "PlayerData", "Player_76561197993781479.sav");

        var dir = Directory.CreateTempSubdirectory("standalone-player");
        try
        {
            var copy = Path.Combine(dir.FullName, Path.GetFileName(source));
            File.Copy(source, copy);

            var scanned = SaveFolderScanner.Scan(dir.FullName);
            var summary = Assert.Single(scanned);
            Assert.Equal("PLAYER", summary.KindLabel);

            // No world saves around: the level index is empty, not throwing.
            Assert.Empty(WorldLevelIndex.ScanFolder(dir.FullName));

            // The save itself still parses fully.
            var data = Core.PlayerSaves.PlayerSaveReader.ReadFromFile(copy);
            Assert.NotEmpty(data.Inventory.Equipment);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Folder_with_only_the_metadata_save_scans_and_reads()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav");

        var dir = Directory.CreateTempSubdirectory("standalone-meta");
        try
        {
            var copy = Path.Combine(dir.FullName, Path.GetFileName(source));
            File.Copy(source, copy);

            var scanned = SaveFolderScanner.Scan(dir.FullName);
            var summary = Assert.Single(scanned);
            Assert.Equal("META", summary.KindLabel);

            var data = WorldSaveReader.ReadFromFile(copy);
            Assert.NotNull(data.StoryProgressionRow);
            // No region saves and no facility flags: empty collections, not errors.
            Assert.Empty(data.Doors);
            Assert.Empty(data.Containers);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
