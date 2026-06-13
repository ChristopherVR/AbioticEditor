using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.WorldSaves;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Tests;

public class SaveCompatibilityTests
{
    // ---------- fixture saves: known classes at known-good versions -> no warning ----------

    [Fact]
    public void FixturePlayerSave_HasKnownVersion_AndNoWarning()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        var data = PlayerSaveReader.ReadFromFile(path);

        Assert.True(data.HasKnownSaveClass);
        Assert.Equal(SaveCompatibility.KnownGoodCharacterVersion, data.AbfVersion);
        Assert.Null(SaveCompatibility.WarningFor(data.Raw));
    }

    [Fact]
    public void FixtureWorldSaves_HaveKnownVersion_AndNoWarning()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        foreach (var name in new[] { "WorldSave_MetaData.sav", "WorldSave_Facility_Office1.sav" })
        {
            var data = WorldSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, name));
            Assert.True(data.HasKnownSaveClass);
            Assert.Equal(SaveCompatibility.KnownGoodWorldVersion, data.AbfVersion);
            Assert.Null(SaveCompatibility.WarningFor(data.Raw));
        }
    }

    // ---------- synthetic versions ----------

    [Fact]
    public void BumpedVersion_ProducesWarning()
    {
        var warning = SaveCompatibility.Compute(
            abfVersion: SaveCompatibility.KnownGoodWorldVersion + 1,
            knownGoodVersion: SaveCompatibility.KnownGoodWorldVersion,
            hasRegisteredSaveClass: true,
            saveClassName: "/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C");

        Assert.NotNull(warning);
        Assert.Contains($"version {SaveCompatibility.KnownGoodWorldVersion + 1}", warning);
        Assert.Contains($"built against version {SaveCompatibility.KnownGoodWorldVersion}", warning);
        Assert.Contains(".bak", warning);
    }

    [Fact]
    public void SameOrOlderVersion_ProducesNoWarning()
    {
        Assert.Null(SaveCompatibility.Compute(3, 3, true, "whatever"));
        Assert.Null(SaveCompatibility.Compute(1, 3, true, "whatever"));
        // No version readable but the class is registered -> benign.
        Assert.Null(SaveCompatibility.Compute(null, 3, true, "whatever"));
    }

    [Fact]
    public void UnknownSaveClass_ProducesWarning()
    {
        var warning = SaveCompatibility.Compute(
            abfVersion: null,
            knownGoodVersion: null,
            hasRegisteredSaveClass: false,
            saveClassName: "/Game/Blueprints/Saves/Abiotic_BrandNewSave.Abiotic_BrandNewSave_C");

        Assert.NotNull(warning);
        Assert.Contains("Abiotic_BrandNewSave_C", warning);
        Assert.Contains("not one this editor recognizes", warning);
    }
}
