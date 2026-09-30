using System.Linq;
using System.Text;
using AbioticEditor.Core.Assets;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Whether device ids that a socket names but no save contains are fixed level equipment: an id that
/// appears in a level file belongs to a device the level places (and the plug is real); an id found
/// nowhere belongs to a device that is gone. Searches the raw bytes of every map for each id prefix
/// given in <c>ABIOTIC_MISSING_IDS</c> (space-separated). Output-only.
/// </summary>
public class PowerMissingDeviceProbe
{
    private readonly ITestOutputHelper _output;
    public PowerMissingDeviceProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Dump_WhereMissingDeviceIdsAppear()
    {
        var ids = (Environment.GetEnvironmentVariable("ABIOTIC_MISSING_IDS") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Positive control: the asset ids of level-placed deployables in the fixture (keyed by actor path)
        // must be found in the level files if the search works at all.
        var facility = System.IO.Path.Combine(Fixtures.ServerWorldsDir ?? "", "WorldSave_Facility.sav");
        if (System.IO.File.Exists(facility))
        {
            var data = AbioticEditor.Core.WorldSaves.WorldSaveReader.ReadFromFile(facility);
            foreach (var e in AbioticEditor.Core.WorldSaves.Features.WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Where(e => e.Key.Contains('/')).Take(400))
            {
                var change = e.Props.FirstOrDefault(t => t.Name?.Value?.StartsWith("ChangableData", StringComparison.Ordinal) == true)?.Property?.Value
                    as UeSaveGame.StructData.PropertiesStruct;
                var asset = change?.Properties.FirstOrDefault(t => t.Name?.Value?.StartsWith("AssetID", StringComparison.Ordinal) == true)?.Property?.Value?.ToString();
                if (asset is { Length: 32 }) { ids.Add("CONTROL:" + asset[..8]); if (ids.Count(i => i.StartsWith("CONTROL", StringComparison.Ordinal)) >= 3) break; }
            }
        }
        ids = ids.Select(i => i.Replace("CONTROL:", "", StringComparison.Ordinal)).ToList();
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null || ids.Count == 0) { _output.WriteLine("no game or no ids"); return; }
        var files = assets.AssetPaths.Where(p => p.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)
                                                 && (p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase))).ToList();
        _output.WriteLine($"searching {files.Count} map files for {ids.Count} ids");
        var hits = ids.ToDictionary(i => i, _ => new List<string>());
        foreach (var f in files)
        {
            var bytes = assets.ReadRawFile(f);
            if (bytes is null) continue;
            var text = Encoding.ASCII.GetString(bytes);
            var utf16 = Encoding.Unicode.GetString(bytes);
            foreach (var id in ids)
            {
                if (text.Contains(id, StringComparison.OrdinalIgnoreCase) || utf16.Contains(id, StringComparison.OrdinalIgnoreCase)) hits[id].Add(f);
            }
        }
        foreach (var (id, where) in hits) _output.WriteLine($"{id}: {(where.Count == 0 ? "nowhere in the level files" : string.Join(", ", where.Take(4)))}");
    }
}
