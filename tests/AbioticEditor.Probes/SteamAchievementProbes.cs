using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Steam;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probes for the local Steam appcache stats files (discovery + schema dump). The real
/// parsing assertion lives in AbioticEditor.Tests/SteamAchievementTests.cs.
/// </summary>
public class SteamAchievementProbes
{
    private readonly ITestOutputHelper _output;

    public SteamAchievementProbes(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_LocalSteamStatsFiles()
    {
        var steam = AfInstallLocator.FindSteamPath();
        _output.WriteLine($"Steam path: {steam ?? "(none)"}");
        if (steam is null) return;

        var statsDir = System.IO.Path.Combine(steam, "appcache", "stats");
        if (!System.IO.Directory.Exists(statsDir)) { _output.WriteLine("no stats dir"); return; }
        foreach (var f in System.IO.Directory.EnumerateFiles(statsDir, $"*{SteamAchievements.AppId}*"))
        {
            _output.WriteLine($"  {System.IO.Path.GetFileName(f)} ({new System.IO.FileInfo(f).Length:N0} bytes)");
        }
    }

    [Fact]
    public void Dump_SchemaTree()
    {
        var steam = AfInstallLocator.FindSteamPath();
        if (steam is null) return;
        var schemaPath = System.IO.Path.Combine(steam, "appcache", "stats", $"UserGameStatsSchema_{SteamAchievements.AppId}.bin");
        if (!System.IO.File.Exists(schemaPath)) return;

        var root = BinaryKeyValues.Parse(System.IO.File.ReadAllBytes(schemaPath));
        Dump(root, 0, 4);

        var statsPath = System.IO.Directory.EnumerateFiles(
            System.IO.Path.Combine(steam, "appcache", "stats"), $"UserGameStats_*_{SteamAchievements.AppId}.bin").FirstOrDefault();
        if (statsPath is not null)
        {
            _output.WriteLine("--- user stats ---");
            var stats = BinaryKeyValues.Parse(System.IO.File.ReadAllBytes(statsPath));
            Dump(stats, 0, 6);
        }
    }

    private void Dump(KvNode node, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        var pad = new string(' ', depth * 2);
        _output.WriteLine($"{pad}{node.Key}" + (node.Value is null ? $"  ({node.Children.Count} children)" : $" = {node.Value}"));
        foreach (var c in node.Children.Take(depth == 0 ? 50 : 8))
        {
            Dump(c, depth + 1, maxDepth);
        }
    }
}
