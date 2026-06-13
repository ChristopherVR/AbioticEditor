using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>Bed-claim reassignment through <see cref="WorldSaveWriter.ApplyDeployableCustomText"/>.</summary>
public class BedClaimTests
{
    [Fact]
    public void Reassigning_a_claimed_bed_round_trips()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("bed-claim");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);

            var data = WorldSaveReader.ReadFromFile(copy);
            var bed = data.Deployables.First(d => d.IsBed && !d.IsPetBed && d.OwnerSteamId is not null);
            const ulong newOwner = 76561198999999999;
            Assert.NotEqual(newOwner, bed.OwnerSteamId);

            var ok = WorldSaveWriter.ApplyDeployableCustomText(
                data, bed.Id, $"{newOwner}{WorldDeployable.ClaimSeparator}TestPilot");
            Assert.True(ok);
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Deployables.First(d => d.Id == bed.Id);
            Assert.Equal(newOwner, reloaded.OwnerSteamId);
            Assert.Equal("TestPilot", reloaded.OwnerName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_deployable_returns_false()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var data = WorldSaveReader.ReadFromFile(
            Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav"));
        Assert.False(WorldSaveWriter.ApplyDeployableCustomText(data, "NOPE", "x"));
    }
}
