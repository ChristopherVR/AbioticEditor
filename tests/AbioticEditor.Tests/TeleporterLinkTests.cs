using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Documents the discovered Personal Teleporter sync format: the item's
/// <c>PlayerMadeString_</c> carries the target crafting bench's DeployedObjectMap GUID
/// with a trailing comma. AssetID_ is the item's own instance GUID - not the link.
/// </summary>
public class TeleporterLinkTests
{
    private readonly ITestOutputHelper _output;

    public TeleporterLinkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TeleporterLink_PointsAtACraftingBenchDeployable()
    {
        if (Fixtures.CascadeDir is null) return;
        var facilityPath = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        if (!File.Exists(facilityPath)) return;

        var world = WorldSaveReader.ReadFromFile(facilityPath);
        var deployablesById = world.Deployables.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        var checkedLinks = 0;
        foreach (var playerSave in Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav"))
        {
            var data = PlayerSaveReader.ReadFromFile(playerSave);
            foreach (var slot in data.Inventory.Hotbar.Concat(data.Inventory.Main).Concat(data.Inventory.Equipment))
            {
                if (slot.ItemId?.Equals("personalteleporter", StringComparison.OrdinalIgnoreCase) != true) continue;
                if (string.IsNullOrEmpty(slot.PlayerMadeString)) continue;

                var target = slot.PlayerMadeString.TrimEnd(',');
                Assert.True(deployablesById.TryGetValue(target, out var deployable),
                    $"link target {target} should be a deployable in the Facility save");
                _output.WriteLine($"{Path.GetFileName(playerSave)}: teleporter → {deployable!.ClassName} at ({deployable.X:F0}, {deployable.Y:F0})");
                Assert.True(deployable.IsCraftingBench, "teleporter targets should be crafting benches");
                checkedLinks++;
            }
        }
        _output.WriteLine($"{checkedLinks} synced teleporter(s) verified");
        Assert.True(checkedLinks >= 2);
    }
}
