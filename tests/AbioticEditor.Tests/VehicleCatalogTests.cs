using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Guards the vehicle appearance lookup: the first wiki-image candidate for each shipped
/// vehicle is the exact File name the wiki actually hosts (the old guesses 404'd, so no
/// vehicle ever showed an image). Names verified against abioticfactor.wiki.gg.
/// </summary>
public class VehicleCatalogTests
{
    [Theory]
    [InlineData("ABF_Vehicle_Forklift", "Vehicle_-_Forklift.png")]
    [InlineData("ABF_Vehicle_SecurityCart", "Vehicle_-_GATE_Security_Cart.png")]
    [InlineData("ABF_Vehicle_SUV", "Vehicle_-_GATE_SUV.png")]
    [InlineData("ABF_Vehicle_Sleigh", "Vehicle_-_Sleigh.png")]
    [InlineData("ABF_Vehicle_VOTV_ATV", "Vehicle_-_ATV.png")]
    [InlineData("ABF_Vehicle_Tram", "Vehicle_-_Tram.png")]
    public void FirstWikiCandidate_IsTheRealFileName(string shortClass, string expected)
    {
        var candidates = VehicleCatalog.WikiImageCandidates(shortClass);
        Assert.NotEmpty(candidates);
        Assert.Equal(expected, candidates[0]);
    }

    [Fact]
    public void FullClassPath_ResolvesSameAsShortName()
    {
        var candidates = VehicleCatalog.WikiImageCandidates(
            "/Game/Blueprints/Vehicles/ABF_Vehicle_Forklift.ABF_Vehicle_Forklift_C");
        Assert.Equal("Vehicle_-_Forklift.png", candidates[0]);
    }

    [Fact]
    public void UnknownVehicle_StillOffersGuesses_NotEmpty()
    {
        // A vehicle we don't curate must not crash and should fall back to the wiki naming
        // convention so it has a chance to resolve.
        var candidates = VehicleCatalog.WikiImageCandidates("ABF_Vehicle_Hovercraft");
        Assert.Contains("Vehicle_-_Hovercraft.png", candidates);
    }

    [Theory]
    [InlineData("ABF_Vehicle_Sleigh")]
    [InlineData("ABF_Vehicle_Tram")]
    [InlineData("ABF_Vehicle_Minecart")]
    public void DecorativeVehicles_AreNotEditable(string shortClass)
        => Assert.False(VehicleCatalog.IsEditable(shortClass));

    [Theory]
    [InlineData("ABF_Vehicle_Forklift")]
    [InlineData("ABF_Vehicle_SUV")]
    [InlineData("ABF_Vehicle_VOTV_ATV")]
    [InlineData("ABF_Vehicle_SomethingNew")] // unknown classes default to editable
    public void DrivableAndUnknownVehicles_AreEditable(string shortClass)
        => Assert.True(VehicleCatalog.IsEditable(shortClass));
}
