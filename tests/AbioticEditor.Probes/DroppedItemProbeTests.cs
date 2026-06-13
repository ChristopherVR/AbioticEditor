using System.IO;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class DroppedItemProbeTests
{
    private readonly ITestOutputHelper _output;

    public DroppedItemProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_NarrativeNpcEntries()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        var tag = save.Properties!.FirstOrDefault(t => t.Name?.Value?.StartsWith("NarrativeNPCMap") == true);
        if (tag?.Property is not MapProperty map) { _output.WriteLine("no NarrativeNPCMap"); return; }

        _output.WriteLine($"{map.Value!.Count} narrative NPCs");
        foreach (var kvp in map.Value)
        {
            _output.WriteLine($"key: {kvp.Key.Value}");
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                foreach (var p in ps.Properties)
                {
                    _output.WriteLine($"  {p.Name?.Value} ({p.Property?.GetType().Name}) = {p.Property?.Value}");
                }
            }
        }
    }

    [Fact]
    public void Dump_DeployableTransform()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        var tag = save.Properties!.First(t => t.Name?.Value?.StartsWith("DeployedObjectMap") == true);
        var map = (MapProperty)tag.Property!;

        var dumped = 0;
        foreach (var kvp in map.Value!)
        {
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
            var transform = ps.Properties.FirstOrDefault(p => p.Name?.Value?.StartsWith("Transform_") == true);
            if (transform?.Property is not StructProperty tsp) continue;
            _output.WriteLine($"Transform property type: {tsp.Value?.GetType().FullName}");
            if (tsp.Value is PropertiesStruct tps)
            {
                foreach (var p in tps.Properties)
                {
                    _output.WriteLine($"  {p.Name?.Value} ({p.Property?.GetType().Name}) = {p.Property?.Value} [{p.Property?.Value?.GetType().FullName}]");
                    if (p.Property is StructProperty inner)
                    {
                        _output.WriteLine($"    inner value: {inner.Value?.GetType().FullName} = {inner.Value}");
                    }
                }
            }
            else
            {
                _output.WriteLine($"  raw: {tsp.Value}");
            }
            if (++dumped >= 2) break;
        }
    }

    [Fact]
    public void Dump_DroppedItemEntryShape()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        var tag = save.Properties!.First(t => t.Name?.Value?.StartsWith("DroppedItemMap") == true);
        var map = (MapProperty)tag.Property!;
        _output.WriteLine($"{map.Value!.Count} dropped items");

        foreach (var kvp in map.Value.Take(3))
        {
            _output.WriteLine($"key: {kvp.Key.Value}");
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                foreach (var p in ps.Properties)
                {
                    _output.WriteLine($"  {p.Name?.Value} ({p.Property?.GetType().Name})");
                }
            }
        }
    }
}
