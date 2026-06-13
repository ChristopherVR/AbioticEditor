using System.IO;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Structure dump for the UNKWN-channel world-save properties the editor does not
/// model yet (editor-20260612.log review): LeyakContainmentIDs, TimeOfDay,
/// DayDiscovered, GlobalUnlocks, LastPlayed, ServerEntitlements, PetNPC.
/// </summary>
public class UnmodeledWorldPropsProbe
{
    private readonly ITestOutputHelper _output;

    public UnmodeledWorldPropsProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string[] Prefixes =
    {
        "LeyakContainmentIDs", "TimeOfDay", "DayDiscovered", "GlobalUnlocks",
        "LastPlayed", "ServerEntitlements", "PetNPC",
    };

    [Fact]
    public void Dump_UnmodeledTopLevelProps()
    {
        foreach (var dir in new[] { Fixtures.ServerWorldsDir, Fixtures.CascadeDir })
        {
            if (dir is null) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "WorldSave_*.sav", SearchOption.AllDirectories))
            {
                DumpFile(file);
            }
        }
    }

    private void DumpFile(string path)
    {
        using var fs = File.OpenRead(path);
        SaveGame save;
        try
        {
            save = SaveGame.LoadFrom(fs);
        }
        catch
        {
            return;
        }
        if (save.Properties is null) return;

        var hits = save.Properties
            .Where(t => Prefixes.Any(p => t.Name?.Value?.StartsWith(p) == true))
            .ToList();
        if (hits.Count == 0) return;

        _output.WriteLine($"==== {path}");
        foreach (var tag in hits)
        {
            Dump(tag.Name?.Value ?? "?", tag.Property, indent: 1);
        }
    }

    private void Dump(string name, FProperty? prop, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (prop)
        {
            case MapProperty map:
                _output.WriteLine($"{pad}{name} (Map, {map.Value?.Count ?? 0} entries; key={map.KeyType?.Name}, value={map.ValueType?.Name})");
                var shown = 0;
                foreach (var kvp in map.Value ?? [])
                {
                    _output.WriteLine($"{pad}  key: {kvp.Key.Value}");
                    Dump("value", kvp.Value as FProperty, indent + 2);
                    if (++shown >= 4) { _output.WriteLine($"{pad}  …"); break; }
                }
                break;
            case StructProperty sp when sp.Value is PropertiesStruct ps:
                _output.WriteLine($"{pad}{name} (Struct {sp.StructType?.Name})");
                foreach (var p in ps.Properties)
                {
                    Dump(p.Name?.Value ?? "?", p.Property, indent + 1);
                }
                break;
            case StructProperty sp:
                _output.WriteLine($"{pad}{name} (Struct {sp.StructType?.Name}) = {sp.Value}");
                break;
            case ArrayProperty ap:
                var arrayItems = ap.Value?.Cast<object?>().ToList() ?? new List<object?>();
                _output.WriteLine($"{pad}{name} (Array {ap.ItemType?.Name}, {arrayItems.Count} items): " +
                    string.Join(", ", arrayItems.Take(6).Select(v => v?.ToString())));
                break;
            default:
                _output.WriteLine($"{pad}{name} ({prop?.GetType().Name}) = {prop?.Value}");
                break;
        }
    }
}
