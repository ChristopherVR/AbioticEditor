using AbioticEditor.Core.Assets;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class BodyDiagramProbe
{
    private readonly ITestOutputHelper _output;
    public BodyDiagramProbe(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Find_BodyDiagramAndCharacterSilhouettes()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        var keys = provider.AssetPaths.ToList();

        _output.WriteLine("--- body_diagram_* ---");
        foreach (var k in keys.Where(k => k.Contains("body_diagram", StringComparison.OrdinalIgnoreCase)).Take(30))
            _output.WriteLine($"  {k}");

        _output.WriteLine("");
        _output.WriteLine("--- player silhouette / paper-doll / character pane ---");
        foreach (var k in keys.Where(k =>
            k.EndsWith(".uasset") &&
            (k.Contains("silhouette", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("character_pane", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("PaperDoll", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("CharacterPane", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("playerbody", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("EquipmentSlot", StringComparison.OrdinalIgnoreCase))).Take(30))
            _output.WriteLine($"  {k}");

        _output.WriteLine("");
        _output.WriteLine("--- InventoryCharacterPane related ---");
        foreach (var k in keys.Where(k => k.Contains("InventoryCharacterPane", StringComparison.OrdinalIgnoreCase) || k.Contains("CharacterRT", StringComparison.OrdinalIgnoreCase)).Take(30))
            _output.WriteLine($"  {k}");
    }
}
