using System.IO;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>
/// Research: which game files mention the calculator-style keypad (SM_Calculator_01) and which
/// blueprint classes use it, so keypads built on it are recognised. Set KEYPAD_PROBE_OUT. Not part of
/// the normal test run.
/// </summary>
public sealed class KeypadProbe
{
    [Fact]
    public void Find_calculator_keypads()
    {
        var output = Environment.GetEnvironmentVariable("KEYPAD_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var lines = assets.AssetPaths.Where(p => p.Contains("alculator", StringComparison.OrdinalIgnoreCase) || p.Contains("Keypad", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        foreach (var bp in lines.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && p.Contains("/Blueprints/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(bp, out var pkg)) return 0;
                foreach (var e in pkg.GetExports())
                {
                    if (!e.Name.StartsWith("Default__", StringComparison.Ordinal) && !e.ExportType.Contains("BlueprintGeneratedClass", StringComparison.Ordinal)) continue;
                    var super = (e as CUE4Parse.UE4.Objects.UObject.UStruct)?.SuperStruct?.ResolvedObject?.GetPathName();
                    lines.Add($"  {bp}: {e.Name} ({e.ExportType}) super={super}");
                }
                return 0;
            });
        }
        File.WriteAllLines(output, lines);
    }
}
