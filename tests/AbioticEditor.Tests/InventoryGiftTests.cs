using System.IO;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// The cross-save "send a container item to a player" helper: an item dropped into a player save
/// lands in the first free backpack slot and survives a write/read round-trip, and an empty item or
/// a full inventory is reported rather than silently lost.
/// </summary>
public class InventoryGiftTests
{
    private static string? PlayerFixture()
    {
        if (Fixtures.CascadeDir is null) return null;
        var dir = Path.Combine(Fixtures.CascadeDir, "PlayerData");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "Player_*.sav").OrderBy(p => p).FirstOrDefault();
    }

    private static InventoryItemSlot SampleItem(string assetId) =>
        new(Index: 0, ItemId: "glowshard", Count: 3, Durability: 0, MaxDurability: 0,
            AmmoInMagazine: 0, LiquidLevel: 0, LiquidType: null, DynamicState: false,
            PlayerMadeString: null, AssetId: assetId);

    [Fact]
    public void GiveToPlayer_PlacesItemInFreeBackpackSlot_AndPersists()
    {
        var fixture = PlayerFixture();
        if (fixture is null) return; // fixtures absent: skip gracefully

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-gift-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);
            var player = PlayerSaveReader.ReadFromFile(tempPath);

            var freeBefore = player.Inventory.Main.FirstOrDefault(s => s.IsEmpty);
            if (freeBefore is null) return; // fixture backpack is full; nothing to prove here

            var assetId = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var result = InventoryGift.GiveToPlayer(player, SampleItem(assetId));

            Assert.True(result.Ok, result.Message);
            Assert.Contains("backpack", result.Where);

            PlayerSaveWriter.WriteToFile(player, tempPath);
            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);

            var landed = reloaded.Inventory.Main.FirstOrDefault(s => s.ItemId == "glowshard" && s.AssetId == assetId);
            Assert.NotNull(landed);
            Assert.Equal(3, landed!.Count);

            // The gifted item must also be marked discovered, or the game shows it as an unknown
            // "???" the player never picked up.
            Assert.Contains("glowshard", reloaded.ItemsPickedUp);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void GiveToPlayer_MintsAssetId_WhenItemHasNone()
    {
        var fixture = PlayerFixture();
        if (fixture is null) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-gift-id-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);
            var player = PlayerSaveReader.ReadFromFile(tempPath);
            if (player.Inventory.Main.All(s => !s.IsEmpty)) return;

            // An item carried in with a blank AssetID must get a fresh one, or the game would
            // allocate the slot but never render it.
            var result = InventoryGift.GiveToPlayer(player, SampleItem(assetId: string.Empty));
            Assert.True(result.Ok, result.Message);

            PlayerSaveWriter.WriteToFile(player, tempPath);
            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            var landed = reloaded.Inventory.Main.FirstOrDefault(s => s.ItemId == "glowshard");
            Assert.NotNull(landed);
            Assert.False(string.IsNullOrEmpty(landed!.AssetId));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void GiveToPlayer_RejectsAnEmptyItem()
    {
        var fixture = PlayerFixture();
        if (fixture is null) return;

        var player = PlayerSaveReader.ReadFromFile(fixture);
        var empty = new InventoryItemSlot(0, "Empty", 0, 0, 0, 0, 0, null, false, null, null);
        var result = InventoryGift.GiveToPlayer(player, empty);
        Assert.False(result.Ok);
    }
}
