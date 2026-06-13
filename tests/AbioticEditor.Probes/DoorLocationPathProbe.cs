using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class DoorLocationPathProbe
{
    private readonly ITestOutputHelper _output;

    public DoorLocationPathProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_MapPackagePaths_ForSaveDoorMaps()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) { _output.WriteLine("no install"); return; }
        if (Fixtures.ServerWorldsDir is null) { _output.WriteLine("no fixture"); return; }

        var facility = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_Facility.sav");
        var maps = WorldSaveReader.ReadFromFile(facility).Doors
            .Select(d => DoorIdParser.Parse(d.Id).Map)
            .Where(m => m.Length > 0)
            .GroupBy(m => m, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .Select(g => $"{g.Key} ({g.Count()} doors)")
            .ToList();
        _output.WriteLine("save door maps: " + string.Join(", ", maps));

        var umaps = provider.AssetPaths
            .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _output.WriteLine($"total umaps in paks: {umaps.Count}");
        foreach (var p in umaps.Where(p => p.Contains("Office", StringComparison.OrdinalIgnoreCase)).Take(20))
        {
            _output.WriteLine("  " + p);
        }
        _output.WriteLine("--- first 20 umaps overall:");
        foreach (var p in umaps.Take(20))
        {
            _output.WriteLine("  " + p);
        }
    }
}
