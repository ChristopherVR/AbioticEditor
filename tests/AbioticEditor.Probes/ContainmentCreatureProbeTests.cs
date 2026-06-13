using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Discovery probe for the containment-tab detail view: which pak textures show what a
/// containable creature (Leyak, Krasue) LOOKS like, and whether the creature row names
/// from LeyakContainmentIDs map onto compendium rows or NPC data tables.
/// </summary>
public class ContainmentCreatureProbeTests
{
    private readonly ITestOutputHelper _output;

    public ContainmentCreatureProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_CreatureTextureCandidates()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return;

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.Initialize();
        provider.Mount();

        string[] creatures = ["Leyak", "Krasue", "Pest", "Wraith"];
        foreach (var creature in creatures)
        {
            _output.WriteLine($"=== {creature} ===");
            var hits = provider.Files.Keys
                .Where(p => p.Contains(creature, StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                            && (p.Contains("/T_", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("Texture", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("/UI/", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("Compendium", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("Portrait", StringComparison.OrdinalIgnoreCase)
                                || p.Contains("Icon", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Length)
                .Take(25)
                .ToList();
            foreach (var h in hits) _output.WriteLine($"  {h}");
            if (hits.Count == 0) _output.WriteLine("  (no texture/UI candidates)");
        }

        _output.WriteLine("=== Compendium-named textures ===");
        foreach (var h in provider.Files.Keys
                     .Where(p => p.Contains("T_Compendium", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p)
                     .Take(60))
        {
            _output.WriteLine($"  {h}");
        }
    }
}
