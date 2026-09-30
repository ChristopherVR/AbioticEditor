using System.Linq;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Power plug rules from the game's blueprints: cable length limits, which classes have sockets and
/// how many, and the functions that plug and unplug (names only; bytecode is not decompiled).
/// Output-only. Informs the offline power rerouting (research-power-network-links.md section 6).
/// </summary>
public class PowerCableRulesProbe
{
    private readonly ITestOutputHelper _output;
    public PowerCableRulesProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Dump_PowerBlueprintRules()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) { _output.WriteLine("no game"); return; }
        var names = assets.AssetPaths
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                        && (p.Contains("PowerSocket", StringComparison.OrdinalIgnoreCase) || p.Contains("PlugCord", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("/Deployed_PlugStrip", StringComparison.OrdinalIgnoreCase) || p.Contains("Deployed_CableReroute", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("AbioticDeployed_ParentBP", StringComparison.OrdinalIgnoreCase) || p.Contains("PowerCable", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("/Cable", StringComparison.OrdinalIgnoreCase)))
            .Take(40).ToList();
        foreach (var n in names) _output.WriteLine("ASSET " + n);

        assets.UseFileProvider(p =>
        {
            foreach (var path in names.Where(n => n.Contains("/Blueprints/", StringComparison.OrdinalIgnoreCase)))
            {
                if (!p.TryLoadPackage(path, out var pkg)) continue;
                foreach (var e in pkg.GetExports())
                {
                    var interesting = e.Properties.Where(pr =>
                        pr.Name.Text.Contains("Cable", StringComparison.OrdinalIgnoreCase) || pr.Name.Text.Contains("Length", StringComparison.OrdinalIgnoreCase)
                        || pr.Name.Text.Contains("Distance", StringComparison.OrdinalIgnoreCase) || pr.Name.Text.Contains("Range", StringComparison.OrdinalIgnoreCase)
                        || pr.Name.Text.Contains("Socket", StringComparison.OrdinalIgnoreCase) || pr.Name.Text.Contains("Plug", StringComparison.OrdinalIgnoreCase)
                        || pr.Name.Text.Contains("Power", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (interesting.Count == 0 && e is not UStruct) continue;
                    _output.WriteLine($"\n{path.Split('/')[^1]} :: {e.ExportType} {e.Name}");
                    foreach (var pr in interesting.Take(30)) _output.WriteLine($"   {pr.Name.Text} = {pr.Tag?.GenericValue}");
                    if (e is UStruct st)
                    {

                        var fn = pkg.GetExports().Where(x => x.ExportType == "Function").Select(x => x.Name)
                            .Where(t => t.Contains("Plug", StringComparison.OrdinalIgnoreCase) || t.Contains("Cable", StringComparison.OrdinalIgnoreCase)
                                        || t.Contains("Socket", StringComparison.OrdinalIgnoreCase) || t.Contains("Power", StringComparison.OrdinalIgnoreCase)
                                        || t.Contains("Distance", StringComparison.OrdinalIgnoreCase) || t.Contains("Length", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (fn.Count > 0) _output.WriteLine($"   functions: {string.Join(", ", fn)}");
                    }
                }
            }
            return 0;
        });
    }
}
