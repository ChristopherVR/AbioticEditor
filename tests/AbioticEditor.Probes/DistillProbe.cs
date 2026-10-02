using System.IO;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>Research: which game files mention distilling. Set DISTILL_PROBE_OUT to run.</summary>
public sealed class DistillProbe
{
    [Fact]
    public void Dump_distill_assets()
    {
        var output = Environment.GetEnvironmentVariable("DISTILL_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var provider = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(provider);
        var lines = provider!.AssetPaths.Where(p => p.Contains("istill", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        var rows = AbioticEditor.Core.Items.DistillationCatalog.Describe(provider);
        lines.Add($"--- rows: {rows.Count}");
        lines.AddRange(rows.Select(r => r.Row + " = " + r.Fields));
        File.WriteAllLines(output, lines);
        File.WriteAllText(output + ".json", System.Text.Json.JsonSerializer.Serialize(AbioticEditor.Core.Items.DistillationCatalog.LoadFrom(provider)));
    }
}
