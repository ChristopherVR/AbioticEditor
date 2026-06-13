using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression: every real shield must be accepted by the SHIELD equipment slot
/// (user report: "can't drag and drop a shield").
/// </summary>
public class ShieldValidationTests
{
    private readonly ITestOutputHelper _output;
    public ShieldValidationTests(ITestOutputHelper output) { _output = output; }

    // InventorySlotViewModel.ValidateForRole delegates to EquipSlotTypes (Core), so the
    // exact production rule is testable here without referencing the App project.
    private static bool ShieldSlotAccepts(ItemCatalogEntry e)
        => EquipSlotTypes.Accepts(e, "SHIELD");

    [Fact]
    public void AllShields_FitTheShieldSlot()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        string[] shields =
        {
            "heatershield", "heatershield_U1", "heatershield_U2",
            "techshield", "shield_tech_U1",
            "shield_hardlight", "shield_hexwood",
            "kiteshield", "riotshield", "plasticshield",
        };
        foreach (var id in shields)
        {
            var entry = catalog.Find(id);
            Assert.NotNull(entry);
            Assert.True(ShieldSlotAccepts(entry!), $"{id} should fit the SHIELD slot");
            _output.WriteLine($"{id}: ok [{string.Join(" | ", entry!.Tags)}]");
        }
    }
}
