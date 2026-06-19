using System.IO;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="PlayerSaveIdentity.ChangeSteamId"/>: the steamid64 lives in the file name
/// AND the top-level <c>SaveIdentifier</c> StrProperty (research-slot-types.md, Q3);
/// re-homing a save must rewrite both.
/// </summary>
public class PlayerSaveIdentityTests
{
    private readonly ITestOutputHelper _output;
    public PlayerSaveIdentityTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void ChangeSteamId_RenamesFile_RewritesSaveIdentifier_AndStillParses()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        const ulong newId = 76561198000000001UL;
        var tempDir = Path.Combine(Path.GetTempPath(), $"abf-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Player_76561197993781479.sav");
            File.Copy(fixture, sourcePath);

            var newPath = PlayerSaveIdentity.ChangeSteamId(sourcePath, newId);
            _output.WriteLine($"new path: {newPath}");

            // New file next to the source, original gone, backup kept.
            Assert.Equal(Path.Combine(tempDir, $"Player_{newId}.sav"), newPath);
            Assert.True(File.Exists(newPath), "re-homed save missing");
            Assert.False(File.Exists(sourcePath), "original file should be deleted");
            Assert.True(File.Exists(sourcePath + ".bak"), "backup of the original should be kept");

            // The result still parses, and SaveIdentifier now carries the new id.
            var reloaded = PlayerSaveReader.ReadFromFile(newPath);
            var identifier = reloaded.Raw.Properties!
                .FirstOrDefault(t => t.Name?.Value == "SaveIdentifier")?.Property?.Value?.ToString();
            Assert.Equal(newId.ToString(System.Globalization.CultureInfo.InvariantCulture), identifier);

            // Sanity: real character payload survived (inventories still read).
            Assert.NotEmpty(reloaded.Inventory.Equipment);

            // A second change onto an occupied id must refuse rather than clobber.
            File.Copy(fixture, sourcePath);
            Assert.Throws<IOException>(() => PlayerSaveIdentity.ChangeSteamId(sourcePath, newId));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ChangeSteamId_AcceptsANonSteamOwnerId()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        const string newId = "msft-1A2B3C"; // a non-numeric (Game Pass-style) owner id
        var tempDir = Path.Combine(Path.GetTempPath(), $"abf-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Player_76561197993781479.sav");
            File.Copy(fixture, sourcePath);

            var newPath = PlayerSaveIdentity.ChangeSteamId(sourcePath, newId);

            // The file name and the internal SaveIdentifier both carry the opaque id, and it round-trips.
            Assert.Equal(Path.Combine(tempDir, $"Player_{newId}.sav"), newPath);
            Assert.True(File.Exists(newPath));
            var reloaded = PlayerSaveReader.ReadFromFile(newPath);
            Assert.Equal(newId, PlayerSaveIdentity.GetSaveIdentifier(reloaded.Raw));
            Assert.NotEmpty(reloaded.Inventory.Equipment);

            // An unsafe id (path separator) is rejected before any file is touched.
            File.Copy(fixture, sourcePath);
            Assert.Throws<ArgumentException>(() => PlayerSaveIdentity.ChangeSteamId(sourcePath, "bad/id"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
