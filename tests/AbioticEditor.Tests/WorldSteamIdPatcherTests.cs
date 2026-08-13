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

    /// <summary>
    /// The Xbox-to-Steam case: the two ids are different lengths, so the FString length prefix in
    /// front of every claim changes and the save has to be re-serialized rather than byte-patched.
    /// </summary>
    [Theory]
    [InlineData("msft-1A2B3C")]           // shorter than a SteamID64
    [InlineData("2533274900397709")]      // an Xbox account id: one digit shorter
    [InlineData("6983760860664838809")]   // a 19-digit Game Pass id: longer
    public void Rewrites_across_different_length_ids(string newOwner)
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);

            var before = WorldSaveReader.ReadFromFile(copy).Deployables;
            var mine = before.Count(d => d.OwnerSteamId == KnownOwner);
            Assert.True(mine > 0, "fixture should contain claims by the known owner");
            var otherOwners = before.Count(d => d.OwnerId is not null && d.OwnerSteamId != KnownOwner);

            var patched = WorldSteamIdPatcher.PatchFile(
                copy, KnownOwner.ToString(System.Globalization.CultureInfo.InvariantCulture), newOwner);
            Assert.Equal(mine, patched);
            Assert.True(File.Exists(copy + ".bak"));

            // The re-serialized save still parses, the beds moved, and nobody else's claim did.
            var after = WorldSaveReader.ReadFromFile(copy).Deployables;
            Assert.Equal(0, after.Count(d => d.OwnerSteamId == KnownOwner));
            Assert.Equal(mine, after.Count(d => d.OwnerId == newOwner));
            Assert.Equal(otherOwners, after.Count(d => d.OwnerId is not null && d.OwnerId != newOwner));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The claimer's name has to come through the rewrite untouched. The fixture's own claim is the
    /// awkward case on purpose: the name is wrapped in invisible private-use glyphs (a Steam styling
    /// artifact), which forces the game to store the whole string as UTF-16 and which the display
    /// path deliberately strips - so a rewrite that went through the friendly name would quietly
    /// rename the player.
    /// </summary>
    [Fact]
    public void Different_length_rewrite_keeps_the_claimer_name_exactly()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);
            const string oldOwner = "76561197993781479";
            const string newOwner = "2533274900397709";

            var before = WorldSaveReader.ReadFromFile(copy).Deployables
                .Where(d => d.OwnerId == oldOwner)
                .ToDictionary(d => d.Id, d => d.CustomName!, StringComparer.Ordinal);
            Assert.NotEmpty(before);
            // Prove the fixture really is the awkward case rather than a plain ASCII name.
            Assert.Contains(before.Values, name => name.Any(c => c > 0x7F));

            Assert.Equal(before.Count, WorldSteamIdPatcher.PatchFile(copy, oldOwner, newOwner));

            var after = WorldSaveReader.ReadFromFile(copy).Deployables.ToDictionary(d => d.Id, StringComparer.Ordinal);
            foreach (var (id, originalText) in before)
            {
                // Everything from the separator onwards is byte-identical; only the id moved.
                Assert.Equal(newOwner + originalText[oldOwner.Length..], after[id].CustomName);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A world nobody in question ever claimed a bed in must come back untouched, even though the
    /// different-length route could have re-serialized it. Re-writing a save the editor had no
    /// reason to change is how a "harmless" conversion turns into a diff nobody can review.
    /// </summary>
    [Fact]
    public void Different_length_rewrite_leaves_an_unmatched_world_byte_identical()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);
            var stamp = File.GetLastWriteTimeUtc(copy);

            // A 16-digit id nobody in this world has, against a 17-digit one: different lengths.
            Assert.Equal(0, WorldSteamIdPatcher.PatchFile(copy, "1234567890123456", "76561198999999999"));
            Assert.False(File.Exists(copy + ".bak"));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>Re-homing to the id the player already has is nothing, not a full rewrite.</summary>
    [Fact]
    public void Rehoming_to_the_same_id_does_nothing()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("claim-patch");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);

            Assert.Equal(0, WorldSteamIdPatcher.PatchFile(copy, KnownOwner, KnownOwner));
            Assert.False(File.Exists(copy + ".bak"));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
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
