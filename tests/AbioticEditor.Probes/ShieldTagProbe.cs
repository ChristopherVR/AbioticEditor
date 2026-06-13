using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class ShieldTagProbe
{
    private readonly ITestOutputHelper _output;
    public ShieldTagProbe(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Dump_AllShieldLikeItems()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        foreach (var e in catalog.Entries.Where(e =>
            e.Id.Contains("shield", StringComparison.OrdinalIgnoreCase)
            || e.DisplayName.Contains("shield", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"ITEM {e.Id} '{e.DisplayName}' TAGS [{string.Join(" | ", e.Tags)}]");
        }
    }
}
