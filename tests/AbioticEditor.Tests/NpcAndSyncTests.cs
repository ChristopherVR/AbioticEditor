using System.IO;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class NpcAndSyncTests
{
    private readonly ITestOutputHelper _output;

    public NpcAndSyncTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FacilitySave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void HeaderScan_MatchesFullParse()
    {
        if (Fixtures.CascadeDir is null) return;

        // The lightweight header read must agree with the full parser for every fixture.
        foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir, "*.sav", SearchOption.AllDirectories).Take(8))
        {
            var fromHeader = SaveFolderScanner.ReadSaveClassFromHeader(path);
            using var fs = File.OpenRead(path);
            var full = UeSaveGame.SaveGame.LoadFrom(fs);
            Assert.Equal(full.SaveClass?.ToString(), fromHeader);
        }
    }

    [Fact]
    public void Reads_Npcs()
    {
        var path = FacilitySave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        _output.WriteLine($"{data.Npcs.Count} NPCs");
        foreach (var n in data.Npcs) _output.WriteLine($"  {n.ActorName}: dead={n.IsDead} state={n.State}");
        Assert.True(data.Npcs.Count >= 2);
        Assert.All(data.Npcs, n => Assert.False(string.IsNullOrEmpty(n.State)));
    }

    [Fact]
    public void ApplyNpcs_RoundTripsThroughSerializer()
    {
        var path = FacilitySave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        var first = data.Npcs[0];
        var updated = first with { IsDead = true };
        WorldSaveWriter.ApplyNpcs(data, new[] { updated });

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        Assert.True(reloaded.Npcs.First(n => n.Id == first.Id).IsDead);
    }

    [Fact]
    public void SyncFacilityFlags_AddsMissingTriggerFlags()
    {
        if (Fixtures.CascadeDir is null) return;
        var metadata = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        var facility = FacilitySave();
        if (!File.Exists(metadata) || facility is null) return;

        // Run against a temp copy so the fixture stays pristine.
        var tempDir = Path.Combine(Path.GetTempPath(), "abiotic-sync-test");
        Directory.CreateDirectory(tempDir);
        var tempMeta = Path.Combine(tempDir, "WorldSave_MetaData.sav");
        var tempFacility = Path.Combine(tempDir, "WorldSave_Facility.sav");
        File.Copy(metadata, tempMeta, overwrite: true);
        File.Copy(facility, tempFacility, overwrite: true);

        var (added, message) = StoryFlagSync.SyncFacilityFlags(tempMeta, "EndGame");
        _output.WriteLine($"added={added}: {message}");

        var reloaded = WorldSaveReader.ReadFromFile(tempFacility);
        foreach (var chapter in StoryProgressionCatalog.Chapters)
        {
            if (chapter.TriggerFlag is null) continue;
            Assert.Contains(reloaded.Flags, f => string.Equals(f, chapter.TriggerFlag, StringComparison.OrdinalIgnoreCase));
        }

        // Second run is a no-op.
        var (added2, _) = StoryFlagSync.SyncFacilityFlags(tempMeta, "EndGame");
        Assert.Equal(0, added2);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void AddFacilityFlags_AddsOnlyNewFlags_AndIsIdempotent()
    {
        if (Fixtures.CascadeDir is null) return;
        var metadata = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        var facility = FacilitySave();
        if (!File.Exists(metadata) || facility is null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "abiotic-traderflag-test");
        Directory.CreateDirectory(tempDir);
        var tempMeta = Path.Combine(tempDir, "WorldSave_MetaData.sav");
        var tempFacility = Path.Combine(tempDir, "WorldSave_Facility.sav");
        File.Copy(metadata, tempMeta, overwrite: true);
        File.Copy(facility, tempFacility, overwrite: true);

        // A flag the trader rework would write; unlikely to already exist.
        const string flag = "UnitTest_TraderStockUnlock_Probe";
        var existing = WorldSaveReader.ReadFromFile(tempFacility).Flags;
        var alreadyHad = existing.Contains(flag, StringComparer.OrdinalIgnoreCase);

        var (added, _) = StoryFlagSync.AddFacilityFlags(tempMeta, new[] { flag });
        Assert.Equal(alreadyHad ? 0 : 1, added);

        var reloaded = WorldSaveReader.ReadFromFile(tempFacility);
        Assert.Contains(reloaded.Flags, f => string.Equals(f, flag, StringComparison.OrdinalIgnoreCase));

        // Re-adding the same flag is a no-op.
        var (added2, _) = StoryFlagSync.AddFacilityFlags(tempMeta, new[] { flag });
        Assert.Equal(0, added2);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void RespawnTerminal_NearestTo_PicksClosestSector()
    {
        // A point sitting essentially on the Hydroplant terminal resolves to it, not
        // to any farther sector - the containment-tab region heuristic relies on this.
        var hydro = Core.PlayerSaves.RespawnTerminalCatalog.All
            .First(t => t.LocationName == "Hydroplant");
        var nearest = Core.PlayerSaves.RespawnTerminalCatalog.NearestTo(
            hydro.X + 50, hydro.Y - 40, hydro.Z + 10);
        Assert.Equal("Hydroplant", nearest.LocationName);
    }
}
