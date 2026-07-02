using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class RespawnEditTests
{
    private readonly ITestOutputHelper _output;

    public RespawnEditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WorldLevelIndex_FindsLevelGuids_FastScan()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var levels = WorldLevelIndex.ScanFolder(Fixtures.CascadeDir!);
        foreach (var l in levels) _output.WriteLine($"{l.LevelGuid} = {l.DisplayName}");

        Assert.True(levels.Count >= 5, "the Cascade fixture has many region saves");

        // The fixture players' respawn GUID belongs to Facility_Office1.
        var office1 = levels.FirstOrDefault(l =>
            string.Equals(l.LevelGuid, "EB422B4245ACC9F546C26989FC936F5C", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(office1);
        Assert.Contains("Office1", office1!.FileName);
        Assert.Equal("Facility Office1", office1.DisplayName);
    }

    [Fact]
    public void ApplyRespawn_RoundTrips()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();

        var data = PlayerSaveReader.ReadFromFile(source);
        var newGuid = "0123456789ABCDEF0123456789ABCDEF";
        PlayerSaveWriter.ApplyRespawn(data, 111.5, -222.25, 333, newGuid);

        var temp = Path.Combine(Path.GetTempPath(), $"respawn-test-{Guid.NewGuid():N}.sav");
        try
        {
            PlayerSaveWriter.WriteToFile(data, temp);
            var reread = PlayerSaveReader.ReadFromFile(temp);

            Assert.Equal(111.5, reread.RespawnX, 3);
            Assert.Equal(-222.25, reread.RespawnY, 3);
            Assert.Equal(333, reread.RespawnZ, 3);
            Assert.Equal(newGuid, reread.RespawnLevelGuid);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // ---------- chapter -> terminal resolution ----------

    [Fact]
    public void ForChapter_ReturnsTheSectorsOwnTerminal_WhenOneExists()
    {
        Assert.Equal("The Office Sector", RespawnTerminalCatalog.ForChapter("Office")?.LocationName);
        Assert.Equal("Manufacturing West", RespawnTerminalCatalog.ForChapter("MF")?.LocationName);
        Assert.Equal("Cascade Laboratories", RespawnTerminalCatalog.ForChapter("Labs")?.LocationName);
        Assert.Equal("The Reactors", RespawnTerminalCatalog.ForChapter("ReactorsEntry")?.LocationName);
        Assert.Equal("Residence Sector", RespawnTerminalCatalog.ForChapter("Residence")?.LocationName);
    }

    [Fact]
    public void ForChapter_FallsBackToTheNearestEarlierSector_ForPortalWorldChapters()
    {
        // Flathill is a portal world reached from the Office - no terminal of its own.
        Assert.Equal("The Office Sector", RespawnTerminalCatalog.ForChapter("Flathill")?.LocationName);
        // Voussoir is reached from the Hydroplant.
        Assert.Equal("Hydroplant", RespawnTerminalCatalog.ForChapter("Voussoir")?.LocationName);
        // EndGame (the finale) falls all the way back to Residence Sector, the last real terminal.
        Assert.Equal("Residence Sector", RespawnTerminalCatalog.ForChapter("EndGame")?.LocationName);
    }

    [Fact]
    public void ForChapter_ReturnsNull_ForAnUnknownRow()
    {
        Assert.Null(RespawnTerminalCatalog.ForChapter("NotAChapter"));
    }

    // ---------- moving players back on a story revert ----------

    [Fact]
    public void MoveToChapterTerminal_OnARealSave_MovesEveryPlayer_WithoutTouchingLevelGuid()
    {
        if (Fixtures.ServerWorldsDir is null) return;
        var metaSrc = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_MetaData.sav");
        var playerDataSrc = Path.Combine(Fixtures.ServerWorldsDir, "PlayerData");
        if (!File.Exists(metaSrc) || !Directory.Exists(playerDataSrc)) return;
        var playerFiles = Directory.GetFiles(playerDataSrc, "Player_*.sav");
        if (playerFiles.Length == 0) return;

        var dir = Directory.CreateTempSubdirectory("respawn-revert");
        try
        {
            File.Copy(metaSrc, Path.Combine(dir.FullName, "WorldSave_MetaData.sav"));
            var playerDataDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "PlayerData"));
            foreach (var f in playerFiles)
            {
                File.Copy(f, Path.Combine(playerDataDir.FullName, Path.GetFileName(f)));
            }
            var metaCopy = Path.Combine(dir.FullName, "WorldSave_MetaData.sav");

            var before = Directory.GetFiles(playerDataDir.FullName, "Player_*.sav")
                .ToDictionary(f => Path.GetFileName(f)!, f => PlayerSaveReader.ReadFromFile(f).RespawnLevelGuid);

            var (moved, message) = PlayerRespawnRevert.MoveToChapterTerminal(metaCopy, "Office");
            Assert.Equal(playerFiles.Length, moved);
            Assert.Contains("The Office Sector", message);

            var terminal = RespawnTerminalCatalog.ForChapter("Office")!;
            foreach (var f in Directory.GetFiles(playerDataDir.FullName, "Player_*.sav"))
            {
                var after = PlayerSaveReader.ReadFromFile(f);
                Assert.Equal(terminal.X, after.RespawnX, 1);
                Assert.Equal(terminal.Y, after.RespawnY, 1);
                Assert.Equal(terminal.Z, after.RespawnZ, 1);
                Assert.Equal(terminal.TerminalGuid, after.TerminalRespawnId, ignoreCase: true);
                // LastSafeWorldGUID_ is deliberately left untouched.
                Assert.Equal(before[Path.GetFileName(f)], after.RespawnLevelGuid, ignoreCase: true);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
