using System.IO;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression: AF delta-serializes ChangeableData, so a slot the game wrote sparsely
/// (e.g. an empty transmog slot carrying only AssetID_) has NO CurrentItemDurability_/
/// CurrentStack_/... tags. The writers must create the missing tags (FindOrCreate, with
/// the exact hash-suffixed blueprint names) instead of silently dropping the edit.
///
/// The checked-in fixtures predate sparse slot serialization (every ChangeableData is
/// fully populated), so the test manufactures a genuinely sparse save file first:
/// strip the value tags from an empty transmog slot, write, re-read - then prove an
/// item + durability edit on that sparse slot round-trips through the file.
/// </summary>
public class SparseSlotWriteTests
{
    private readonly ITestOutputHelper _output;
    public SparseSlotWriteTests(ITestOutputHelper output) { _output = output; }

    /// <summary>The value-bearing ChangeableData member prefixes PlayerSaveWriter.ApplySlot patches.</summary>
    private static readonly string[] ValueTagPrefixes =
    {
        "CurrentItemDurability_", "MaxItemDurability_", "CurrentStack_",
        "CurrentAmmoInMagazine_", "LiquidLevel_", "DynamicState_", "PlayerMadeString_",
    };

    [Fact]
    public void EmptyTransmogSlot_ItemAndDurabilityEdit_PersistsThroughFile()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-sparse-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);

            // ------------------------------------------------------------------
            // Step 1: manufacture a sparse save - strip every value tag from an
            // empty transmog slot's ChangeableData (the form newer game builds
            // write) and persist it.
            // ------------------------------------------------------------------
            var setup = PlayerSaveReader.ReadFromFile(tempPath);
            var index = setup.TransmogSlots.First(s => s.IsEmpty).Index;
            var changeable = TransmogChangeableData(setup, index);
            changeable.Properties = changeable.Properties
                .Where(t => !ValueTagPrefixes.Any(p => t.Name!.Value.StartsWith(p, StringComparison.Ordinal)))
                .ToList();
            _output.WriteLine($"sparse slot {index} now carries: [{string.Join(", ", changeable.Properties.Select(t => t.Name!.Value))}]");
            PlayerSaveWriter.WriteToFile(setup, tempPath);

            // ------------------------------------------------------------------
            // Step 2: load the sparse file fresh (like the editor would), verify
            // it really is sparse, then apply an item + durability edit.
            // ------------------------------------------------------------------
            var data = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.False(
                TransmogSlotHasTag(data, index, "CurrentItemDurability_"),
                "setup failed: durability tag still present");

            var updated = data.TransmogSlots
                .Select(s => s.Index == index
                    ? s with { ItemId = "armor_helmet_security", Count = 1, Durability = 77, MaxDurability = 100 }
                    : s)
                .ToList();
            PlayerSaveWriter.ApplyTransmogSlots(data, updated);
            PlayerSaveWriter.WriteToFile(data, tempPath);

            // ------------------------------------------------------------------
            // Step 3: the edit persisted - the missing tags were created with the
            // exact hash-suffixed names the reader (and the game) look up.
            // ------------------------------------------------------------------
            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            var slot = reloaded.TransmogSlots[index];
            Assert.Equal("armor_helmet_security", slot.ItemId);
            Assert.Equal(77, slot.Durability);
            Assert.Equal(100, slot.MaxDurability);
            Assert.Equal(1, slot.Count);

            // The created tags carry the canonical full names.
            Assert.True(TransmogSlotHasTag(reloaded, index, "CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8"));
            Assert.True(TransmogSlotHasTag(reloaded, index, "MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B"));
            Assert.True(TransmogSlotHasTag(reloaded, index, "CurrentStack_9_D443B69044D640B0989FD8A629801A49"));

            // The rest of the save survives the rewrite untouched.
            Assert.Equal(data.Raw.Properties!.Count, reloaded.Raw.Properties!.Count);
            Assert.Equal(
                data.Inventory.Equipment.Select(s => s.ItemId),
                reloaded.Inventory.Equipment.Select(s => s.ItemId));
            Assert.Equal(data.Skills.Select(s => s.Xp), reloaded.Skills.Select(s => s.Xp));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    /// <summary>Does the raw TransmogInventory_ slot's ChangeableData_ carry a tag with this prefix?</summary>
    private static bool TransmogSlotHasTag(PlayerSaveData data, int index, string prefix)
        => TransmogChangeableData(data, index).Properties
            .Any(t => t.Name!.Value.StartsWith(prefix, StringComparison.Ordinal));

    private static PropertiesStruct TransmogChangeableData(PlayerSaveData data, int index)
    {
        var root = (PropertiesStruct)((StructProperty)data.Raw.Properties!
            .First(t => t.Name!.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal)).Property!).Value!;
        var array = (ArrayProperty)root.Properties
            .First(t => t.Name!.Value.StartsWith("TransmogInventory_", StringComparison.Ordinal)).Property!;
        var slot = (PropertiesStruct)((StructProperty)array.Value!.GetValue(index)!).Value!;
        return (PropertiesStruct)((StructProperty)slot.Properties
            .First(t => t.Name!.Value.StartsWith("ChangeableData_", StringComparison.Ordinal)).Property!).Value!;
    }
}
