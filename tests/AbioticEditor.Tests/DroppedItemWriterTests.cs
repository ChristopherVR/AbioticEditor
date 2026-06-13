using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public sealed class DroppedItemWriterTests
{
    /// <summary>
    /// Adding a ground item by cloning an existing entry must produce a save that re-reads
    /// cleanly with exactly one more dropped item, carrying the new id, slot, and location.
    /// (Structural round-trip; proves the synthesized entry is well-formed.)
    /// </summary>
    [Fact]
    public void AddDroppedItem_ClonesEntry_AndRoundTrips()
    {
        if (Fixtures.CascadeDir is null) return;

        // Find a world save that actually has dropped items to clone from.
        var source = Directory
            .EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav", SearchOption.AllDirectories)
            .Select(p => (Path: p, Data: SafeRead(p)))
            .FirstOrDefault(t => t.Data is { } d && d.DroppedItems.Count > 0);
        if (source.Data is null) return;

        var data = WorldSaveReader.ReadFromFile(source.Path);
        var before = data.DroppedItems.Count;
        var template = data.DroppedItems[0].Slot; // reuse a real slot as the dropped item

        var newId = WorldSaveWriter.AddDroppedItem(data, template, 111.0, 222.0, 333.0, noDespawn: true);
        Assert.NotNull(newId);

        var tmp = Path.Combine(Path.GetTempPath(), "abiotic-drop-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            WorldSaveWriter.WriteToFile(data, tmp);
            var reread = WorldSaveReader.ReadFromFile(tmp);

            Assert.Equal(before + 1, reread.DroppedItems.Count);
            var added = reread.DroppedItems.FirstOrDefault(d => d.Id == newId);
            Assert.NotNull(added);
            Assert.Equal(111.0, added!.X, 1);
            Assert.Equal(222.0, added.Y, 1);
            Assert.Equal(333.0, added.Z, 1);
            Assert.False(added.Slot.IsEmpty);
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
            try { File.Delete(tmp + ".bak"); } catch (IOException) { }
        }
    }

    private static WorldSaveData? SafeRead(string path)
    {
        try { return WorldSaveReader.ReadFromFile(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException) { return null; }
    }
}
