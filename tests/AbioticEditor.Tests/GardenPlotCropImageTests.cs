using System.Reflection;
using AbioticEditor.Core.Assets;
using AbioticEditor.Web.Components.World;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 88: a garden plot's crop image never showed at all, for a reason the previous round's
/// spot-selection fix did not touch - the raw row the live crop field reports (e.g.
/// <c>Plant_Corn</c>) is a planting-state row from <c>ItemTable_Global</c>, not the pictured
/// seed/produce item a player would recognise. The bundled registry's own <c>Plant_Corn</c> entry
/// has a blank <c>DisplayName</c> and an <c>icon_missingitem</c> placeholder icon, so it was never
/// going to show a real picture no matter which spot was selected. <see
/// cref="WorldFeaturesTab"/>'s private <c>CropToPicturedItemId</c> table maps each of
/// <c>DeployedCareFeatures.cs</c>'s own <c>CropRows</c> to the real, pictured item id - reached
/// here by reflection (not duplicated as a second copy of the table in this test) so a future edit
/// to that table is checked against the real bundled registry automatically, the same guarantee
/// <see cref="BundledGameDataTests"/> already gives the registry's own shape.
/// </summary>
public sealed class GardenPlotCropImageTests
{
    private static System.Collections.Generic.IReadOnlyDictionary<string, string> CropToPicturedItemId()
    {
        var field = typeof(WorldFeaturesTab).GetField("CropToPicturedItemId", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("WorldFeaturesTab.CropToPicturedItemId was not found - has it been renamed?");
        return (System.Collections.Generic.IReadOnlyDictionary<string, string>)field.GetValue(null)!;
    }

    [Fact]
    public void CropToPicturedItemId_MapsEveryCropRowToAPicturedRegistryItem()
    {
        var registry = GameDataRegistry.LoadBundled();
        Assert.NotNull(registry);
        var byId = registry!.Items!.ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (var (cropRow, picturedId) in CropToPicturedItemId())
        {
            Assert.True(byId.TryGetValue(picturedId, out var entry),
                $"{cropRow} maps to \"{picturedId}\", which is not a real bundled item id.");
            Assert.False(string.IsNullOrWhiteSpace(entry!.DisplayName),
                $"{cropRow} maps to \"{picturedId}\", which has no DisplayName - it would still fail IsBrowsable and show no picture.");
            Assert.False(string.IsNullOrWhiteSpace(entry.IconAssetPath) || entry.IconAssetPath!.Contains("missingitem", StringComparison.OrdinalIgnoreCase),
                $"{cropRow} maps to \"{picturedId}\", which has no real icon asset (\"{entry.IconAssetPath}\") - the same problem this fix exists to avoid.");
        }
    }

    [Fact]
    public void PlantRow_ItselfWouldNeverHaveShownAPicture()
    {
        // Pins the actual root cause, not just the fix: confirms Plant_Corn genuinely is
        // unbrowsable in the shipped registry, so a future registry update that fixes this
        // upstream (the game start giving these rows real artwork) is visible here rather than
        // this test silently asserting a problem that no longer exists.
        var registry = GameDataRegistry.LoadBundled();
        Assert.NotNull(registry);
        var plantCorn = registry!.Items!.FirstOrDefault(item => item.Id == "Plant_Corn");
        Assert.NotNull(plantCorn);
        Assert.True(string.IsNullOrWhiteSpace(plantCorn!.DisplayName),
            "Plant_Corn now has a real DisplayName - CropToPicturedItemId's translation may no longer be needed for it.");
    }
}
