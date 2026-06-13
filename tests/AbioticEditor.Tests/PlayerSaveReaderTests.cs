using System.IO;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class PlayerSaveReaderTests
{
    private readonly ITestOutputHelper _output;

    public PlayerSaveReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string PlayerSavePath => Path.Combine(
        Fixtures.CascadeDir ?? string.Empty,
        "PlayerData",
        "Player_76561197993781479.sav");

    [Fact]
    public void ReadStats_MatchesJsonDump()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        Assert.True(File.Exists(PlayerSavePath), $"missing fixture: {PlayerSavePath}");

        var save = PlayerSaveReader.ReadFromFile(PlayerSavePath);

        // Values cross-checked against the earlier JSON dump
        Assert.Equal(54.20499963164332, save.Stats.Hunger, 6);
        Assert.Equal(54.43899987637997, save.Stats.Thirst, 6);
        Assert.Equal(100.0, save.Stats.Sanity, 6);
        Assert.Equal(35.383999943733215, save.Stats.Fatigue, 6);
        Assert.Equal(48.75, save.Stats.Continence, 6);
        Assert.Equal(114, save.Stats.Money);
    }

    [Fact]
    public void ReadInventories_ProducesExpectedSlotCounts()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(PlayerSavePath)) return;

        var save = PlayerSaveReader.ReadFromFile(PlayerSavePath);

        _output.WriteLine($"Equipment: {save.Inventory.Equipment.Count} slot(s)");
        _output.WriteLine($"Hotbar:    {save.Inventory.Hotbar.Count} slot(s)");
        _output.WriteLine($"Main:      {save.Inventory.Main.Count} slot(s)");

        Assert.NotEmpty(save.Inventory.Equipment);
        Assert.NotEmpty(save.Inventory.Hotbar);
        Assert.NotEmpty(save.Inventory.Main);

        // First equipment slot in the JSON dump is the chest armor
        var firstEquipment = save.Inventory.Equipment[0];
        Assert.Equal("armor_chest_groupe", firstEquipment.ItemId);
        Assert.Equal(1, firstEquipment.Count);
        Assert.Equal(25.0, firstEquipment.Durability, 3);
        Assert.Equal(25.0, firstEquipment.MaxDurability, 3);
    }

    [Fact]
    public void ReadWriteRoundTrip_ProducesIdenticalBytes()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(PlayerSavePath)) return;

        var original = File.ReadAllBytes(PlayerSavePath);
        var save = PlayerSaveReader.ReadFromFile(PlayerSavePath);

        using var ms = new MemoryStream();
        save.Raw.WriteTo(ms);
        var rewritten = ms.ToArray();

        Assert.Equal(original.Length, rewritten.Length);
        Assert.True(original.AsSpan().SequenceEqual(rewritten), "round-trip diverged");
    }

    [Fact]
    public void MutateStats_PersistsThroughWrite()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(PlayerSavePath)) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(PlayerSavePath, tempPath);

            var data = PlayerSaveReader.ReadFromFile(tempPath);
            var mutated = data.Stats with { Money = 99999, Hunger = 100.0 };
            PlayerSaveWriter.ApplyStats(data, mutated);
            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reread = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Equal(99999, reread.Stats.Money);
            Assert.Equal(100.0, reread.Stats.Hunger, 3);
            // Other untouched values should still match the original
            Assert.Equal(54.43899987637997, reread.Stats.Thirst, 6);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ReadInventories_DumpsFirstItemsForInspection()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(PlayerSavePath)) return;

        var save = PlayerSaveReader.ReadFromFile(PlayerSavePath);

        Print("Equipment", save.Inventory.Equipment);
        Print("Hotbar", save.Inventory.Hotbar);
        Print("Main", save.Inventory.Main);

        void Print(string label, IReadOnlyList<InventoryItemSlot> slots)
        {
            _output.WriteLine($"--- {label} ({slots.Count} slot(s)) ---");
            foreach (var s in slots.Take(20))
            {
                var item = s.IsEmpty ? "(empty)" : s.ItemId;
                _output.WriteLine($"  [{s.Index:D2}] {item,-32} x{s.Count}  dur={s.Durability:F1}/{s.MaxDurability:F1}  ammo={s.AmmoInMagazine}");
            }
        }
    }
}
