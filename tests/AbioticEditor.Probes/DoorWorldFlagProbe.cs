using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe: sweeps every cooked sub-level for placed door actors that carry a
/// per-instance story gate (WorldFlagToUnlock / WorldFlagToRemainOpen), which is the only
/// place the game says WHICH story flag opens a given door. DoorClassCatalog only knows a
/// lock kind per blueprint class, which is why it over-reports "story controlled".
/// </summary>
public class DoorWorldFlagProbe
{
    private readonly ITestOutputHelper _output;

    public DoorWorldFlagProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Every cooked sub-level under Content/Maps, so no gated door is missed.</summary>
    private static IReadOnlyList<string> AllMaps(GameAssetProvider provider) => provider.AssetPaths
        .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)
                 && p.StartsWith("AbioticFactor/Content/Maps/", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFileNameWithoutExtension)
        .Where(n => !string.IsNullOrEmpty(n) && !n.Contains("_BuiltData", StringComparison.OrdinalIgnoreCase))
        .Select(n => n!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

    [Fact]
    public void Probe_DumpDoorWorldFlagGates()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var maps = AllMaps(provider);
        _output.WriteLine($"=== scanning {maps.Count} cooked sub-levels");

        var hits = 0;
        foreach (var mapName in maps)
        {
            foreach (var (actor, gate) in DoorGateResolver.ForMap(provider, mapName))
            {
                hits++;
                _output.WriteLine($"  {mapName}\t{actor}\tunlock={gate.UnlockFlag ?? "-"}\tremainOpen={gate.RemainOpenFlag ?? "-"}");
            }
        }
        _output.WriteLine($"=== {hits} door instance(s) carry a world-flag gate");
    }
}
