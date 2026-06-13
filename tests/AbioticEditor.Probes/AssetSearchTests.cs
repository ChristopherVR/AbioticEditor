using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Survey the AF paks for assets we'd want in the editor: logo, fonts, item icons.
/// Output is the test log - these aren't pass/fail probes, just discovery.
/// </summary>
public class AssetSearchTests
{
    private readonly ITestOutputHelper _output;

    public AssetSearchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Survey_FindLogoFontsAndIconCandidates()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return;

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var afFiles = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _output.WriteLine($"AbioticFactor/-namespaced files: {afFiles.Count} of {provider.Files.Count}");

        DumpMatches("FONTS (.ttf / .ufont / .uasset under Fonts/)", afFiles, p =>
            p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".ufont", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/Font", StringComparison.OrdinalIgnoreCase));

        DumpMatches("LOGO candidates", afFiles, p =>
            (p.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("title", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("splash", StringComparison.OrdinalIgnoreCase)) &&
            (p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
             p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
             p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)),
            limit: 40);

        DumpMatches("UI directory roots", afFiles, p =>
            p.Contains("/UI/", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/HUD/", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("/Menu", StringComparison.OrdinalIgnoreCase),
            limit: 0,
            summarizeDirs: true);

        DumpMatches("ICON candidates under AbioticFactor/", afFiles, p =>
            p.Contains("icon", StringComparison.OrdinalIgnoreCase) &&
            (p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
             p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)),
            limit: 40);

        DumpMatches("INVENTORY / ITEM paths", afFiles, p =>
            (p.Contains("/Item", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("/Inventory", StringComparison.OrdinalIgnoreCase)) &&
            p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase),
            limit: 40);

        DumpMatches("All PNGs and SVGs under AbioticFactor/", afFiles, p =>
            p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase),
            limit: 60);
    }

    private void DumpMatches(string label, IEnumerable<string> files, Func<string, bool> predicate, int limit = 25, bool summarizeDirs = false)
    {
        var matches = files.Where(predicate).ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== {label}  ({matches.Count} match(es)) ===");

        if (summarizeDirs)
        {
            var dirs = matches
                .Select(p => string.Join('/', p.Split('/').Take(4)))
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(20);
            foreach (var g in dirs)
            {
                _output.WriteLine($"  {g.Key}/...  ×{g.Count()}");
            }
            return;
        }

        foreach (var p in matches.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Take(limit))
        {
            _output.WriteLine($"  {p}");
        }
        if (matches.Count > limit)
        {
            _output.WriteLine($"  ... and {matches.Count - limit} more");
        }
    }
}
