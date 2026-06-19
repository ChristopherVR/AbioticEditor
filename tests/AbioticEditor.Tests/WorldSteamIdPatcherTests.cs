using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="WorldSteamIdPatcher"/>: bed-claim rewrites must change ONLY the id
/// digits (same file length, still parseable) and follow the player whose SteamID moved.
/// </summary>
public class WorldSteamIdPatcherTests
{
    private const ulong KnownOwner = 76561197993781479; // claims beds in the fixture
    private const ulong NewOwner = 76561198999999999;

    [Fact]
    public void Rewrites_claims_in_place_and_save_still_parses()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);
            var originalLength = new FileInfo(copy).Length;

            var before = WorldSaveReader.ReadFromFile(copy).Deployables
                .Count(d => d.OwnerSteamId == KnownOwner);
            Assert.True(before > 0, "fixture should contain claims by the known owner");

            var patched = WorldSteamIdPatcher.PatchFile(copy, KnownOwner, NewOwner);
            Assert.Equal(before, patched);
            Assert.Equal(originalLength, new FileInfo(copy).Length);
            Assert.True(File.Exists(copy + ".bak"));

            var after = WorldSaveReader.ReadFromFile(copy).Deployables;
            Assert.Equal(0, after.Count(d => d.OwnerSteamId == KnownOwner));
            Assert.Equal(before, after.Count(d => d.OwnerSteamId == NewOwner));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Rewrites_to_a_non_numeric_owner_of_equal_length()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);
            var originalLength = new FileInfo(copy).Length;

            // 17-char non-Steam token, same length as the numeric owner so the in-place patch is safe.
            const string nonSteam = "msft-aaaaaaaaaaaa";
            Assert.Equal(17, nonSteam.Length);

            var before = WorldSaveReader.ReadFromFile(copy).Deployables
                .Count(d => d.OwnerSteamId == KnownOwner);
            Assert.True(before > 0);

            var patched = WorldSteamIdPatcher.PatchFile(
                copy, KnownOwner.ToString(System.Globalization.CultureInfo.InvariantCulture), nonSteam);
            Assert.Equal(before, patched);
            Assert.Equal(originalLength, new FileInfo(copy).Length);

            var after = WorldSaveReader.ReadFromFile(copy).Deployables;
            Assert.Equal(before, after.Count(d => d.OwnerId == nonSteam));
            // The new owner is non-numeric, so the Steam convenience reads null for those beds.
            Assert.Equal(0, after.Count(d => d.OwnerId == nonSteam && d.OwnerSteamId is not null));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Refuses_a_different_length_swap()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);

            // 17-digit Steam id -> a shorter token would shift FString length prefixes: refused.
            Assert.Throws<InvalidOperationException>(() => WorldSteamIdPatcher.PatchFile(
                copy, KnownOwner.ToString(System.Globalization.CultureInfo.InvariantCulture), "msft-1A2B3C"));
            Assert.False(File.Exists(copy + ".bak"), "a refused patch must not write anything");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void File_without_claims_is_untouched()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_MetaData.sav");
            File.Copy(source, copy);
            var stamp = File.GetLastWriteTimeUtc(copy);

            Assert.Equal(0, WorldSteamIdPatcher.PatchFile(copy, KnownOwner, NewOwner));
            Assert.False(File.Exists(copy + ".bak"));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PatchFolder_covers_every_world_save()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            foreach (var name in new[] { "WorldSave_Facility.sav", "WorldSave_MetaData.sav" })
            {
                File.Copy(Path.Combine(Fixtures.ServerWorldsDir!, name), Path.Combine(dir.FullName, name));
            }

            var total = WorldSteamIdPatcher.PatchFolder(dir.FullName, KnownOwner, NewOwner);
            Assert.True(total > 0);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
