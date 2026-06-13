using System.IO;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="PlayerSaveFactory"/> + the clone path of <see cref="PlayerSaveIdentity"/>:
/// the "add player" flow either clones an existing player to a new SteamID (keeping their
/// progress) or fabricates a fresh blank character from a save's structure.
/// </summary>
public class PlayerSaveFactoryTests
{
    private readonly ITestOutputHelper _output;
    public PlayerSaveFactoryTests(ITestOutputHelper output) { _output = output; }

    private static string FixturePlayer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");
        return fixture;
    }

    [Fact]
    public void BuildBlankTemplate_WipesProgress_AndReparses()
    {
        var bytes = PlayerSaveFactory.BuildBlankTemplate(FixturePlayer());
        var data = PlayerSaveReader.ReadFromStream(new MemoryStream(bytes));

        // Vitals full, wallet empty, every list cleared.
        Assert.Equal(0, data.Stats.Money);
        Assert.Equal(100, data.Stats.Hunger);
        Assert.All(data.Skills, s => Assert.Equal(0f, s.Xp));
        Assert.Empty(data.Recipes);
        Assert.Empty(data.Traits);
        Assert.Empty(data.Journals);
        Assert.Empty(data.EmailsRead);
        Assert.Empty(data.MapsUnlocked);

        // Bags emptied (every slot reads back as the Empty sentinel).
        Assert.All(data.Inventory.Equipment, s => Assert.True(s.IsEmpty));
        Assert.All(data.Inventory.Hotbar, s => Assert.True(s.IsEmpty));
        Assert.All(data.Inventory.Main, s => Assert.True(s.IsEmpty));

        // Skill array kept its positional shape - the game looks skills up by index.
        Assert.NotEmpty(data.Skills);

        // The template carries no owner identifier (creation stamps it per SteamID).
        var identifier = PlayerSaveIdentity.GetSaveIdentifier(data.Raw);
        Assert.True(string.IsNullOrEmpty(identifier), $"template should have no owner, got '{identifier}'");
    }

    [Fact]
    public void CreateFromTemplate_WritesNewOwnedPlayer()
    {
        var template = PlayerSaveFactory.BuildBlankTemplate(FixturePlayer());
        const ulong newId = 76561198000000123UL;
        var dir = Path.Combine(Path.GetTempPath(), $"abf-newplayer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = PlayerSaveFactory.CreateFromTemplate(template, dir, newId);
            Assert.Equal(Path.Combine(dir, $"Player_{newId}.sav"), path);

            var data = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal(newId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlayerSaveIdentity.GetSaveIdentifier(data.Raw));
            Assert.Equal(0, data.Stats.Money);

            // Refuses to overwrite an existing player file.
            Assert.Throws<IOException>(() => PlayerSaveFactory.CreateFromTemplate(template, dir, newId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CloneToNewId_KeepsSource_AndCopiesProgress()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"abf-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "Player_76561197993781479.sav");
            File.Copy(FixturePlayer(), source);
            var original = PlayerSaveReader.ReadFromFile(source);

            const ulong newId = 76561198000000124UL;
            var clonePath = PlayerSaveIdentity.CloneToNewId(source, newId);

            Assert.True(File.Exists(source), "clone must keep the source file");
            Assert.Equal(Path.Combine(dir, $"Player_{newId}.sav"), clonePath);

            var clone = PlayerSaveReader.ReadFromFile(clonePath);
            // Identity is the new id...
            Assert.Equal(newId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlayerSaveIdentity.GetSaveIdentifier(clone.Raw));
            // ...but the character's progress is the source's (a true copy).
            Assert.Equal(original.Stats.Money, clone.Stats.Money);
            Assert.Equal(original.Recipes.Count, clone.Recipes.Count);

            Assert.Throws<IOException>(() => PlayerSaveIdentity.CloneToNewId(source, newId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
