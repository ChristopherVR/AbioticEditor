using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.Steam;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class Round6ProbeTests
{
    private readonly ITestOutputHelper _output;

    public Round6ProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_RecipeCategoryValues()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var infos = RecipeCatalog.LoadInfosFrom(provider);
        foreach (var g in infos.GroupBy(i => $"{i.Source}/{i.Category}").OrderBy(g => g.Key))
        {
            _output.WriteLine($"{g.Key}: {g.Count()}  e.g. {g.First().Id}");
        }
    }

    [Fact]
    public void Dump_SchemaIconFields()
    {
        var steam = AfInstallLocator.FindSteamPath();
        if (steam is null) return;
        var schemaPath = System.IO.Path.Combine(steam, "appcache", "stats", $"UserGameStatsSchema_{SteamAchievements.AppId}.bin");
        if (!System.IO.File.Exists(schemaPath)) return;

        var root = BinaryKeyValues.Parse(System.IO.File.ReadAllBytes(schemaPath));
        var stats = root.FindPath(SteamAchievements.AppId.ToString(), "stats");
        var achStat = stats!.Children.First(s => s.Find("type")?.AsString() == "ACHIEVEMENTS");
        var bit = achStat.Find("bits")!.Children.First();
        void Dump(KvNode n, int d)
        {
            _output.WriteLine(new string(' ', d * 2) + n.Key + (n.Value is null ? "" : $" = {n.Value}"));
            foreach (var c in n.Children) Dump(c, d + 1);
        }
        Dump(bit, 0);
    }

    [Fact]
    public void Probe_PersonalTeleporterIcon()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        var candidates = catalog.Entries
            .Where(e => e.DisplayName.Contains("teleport", StringComparison.OrdinalIgnoreCase)
                     || e.Id.Contains("teleport", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var e in candidates)
        {
            _output.WriteLine($"{e.Id}: '{e.DisplayName}' icon={e.IconAssetPath}");
            if (e.IconAssetPath is null) continue;
            var path = provider.ExtractTextureByGameRef(e.IconAssetPath);
            _output.WriteLine($"  extract → {(path is null ? "NULL" : "ok " + path)}");
        }
    }
}
