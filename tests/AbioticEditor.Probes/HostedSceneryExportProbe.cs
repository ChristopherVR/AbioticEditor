using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.GameModels3D;
using Xunit.Abstractions;
using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Tests;

/// <summary>Explicit maintainer preparation; never runs during the ordinary assertion suite.</summary>
public sealed class HostedSceneryExportProbe(ITestOutputHelper output)
{
    /// <summary>
    /// An assembled build (<c>ABIOTIC_SCENERY_VERIFY_ROOT</c>, a <c>scenery/v1/&lt;signature&gt;</c> folder)
    /// draws every checked area with no game files at all, as the browser editor must.
    /// </summary>
    [Fact]
    public void Pages_export_renders_without_local_extraction()
    {
        if (Environment.GetEnvironmentVariable("ABIOTIC_SCENERY_VERIFY_ROOT") is not { Length: > 0 } root) return;
        var data = Path.Combine(Path.GetTempPath(), "scenery-verify-" + Guid.NewGuid().ToString("N"));
        var previous = PluginHostEnvironment.GameAssets;
        PluginHostEnvironment.GameAssets = static () => throw new InvalidOperationException("Unexpected local game extraction.");
        try
        {
            var provider = new PakSceneModelProvider(new ExportHost(output, data));
            // The published files are the provider's own cache, level indexes renamed .bin to .ali.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var key = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!key.Contains('/', StringComparison.Ordinal)) continue; // index.json and the like
                if (key.EndsWith(".ali", StringComparison.Ordinal)) key = key[..^4] + ".bin";
                var target = Path.Combine(provider.CacheRoot, key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var region in new[] { "Facility_Office1", "Facility_Dam", "V_Alps", "V_ISLAND", "V_Winter", "V_FOG" })
            {
                var query = new SceneLevelQuery(region, [-1000000, -1000000, -1000000], [1000000, 1000000, 1000000], 2000);
                provider.DescribeLevel(query);
                provider.WaitForLevels(TimeSpan.FromSeconds(30));
                var slice = provider.DescribeLevel(query);
                Assert.NotNull(slice);
                Assert.Equal(0, slice.PendingMaps);
                Assert.NotEmpty(slice.Batches);
                foreach (var batch in slice.Batches)
                {
                    Assert.NotNull(provider.OpenAsset(batch.Mesh));
                    foreach (var material in batch.Materials)
                    {
                        if (material.Texture is { } texture) Assert.NotNull(provider.OpenAsset(texture));
                        foreach (var layer in material.Layers ?? [])
                            if (layer.Texture is { } layerTexture) Assert.NotNull(provider.OpenAsset(layerTexture));
                    }
                }
                output.WriteLine($"{region}: {slice.Batches.Count} batches, {slice.TotalInBox} instances and {slice.Lights.Count} lights without local extraction.");
            }
        }
        finally
        {
            PluginHostEnvironment.GameAssets = previous;
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }

    /// <summary>
    /// Prepares every level and what it draws into this install's cache (<c>ABIOTIC_SCENERY_PREPARE=1</c>),
    /// with CUE4Parse's native decoder loaded from <c>ABIOTIC_NATIVES_DIR</c> so characters keep their poses.
    /// </summary>
    [Fact]
    public void Prepare_all_levels_for_Pages()
    {
        if (Environment.GetEnvironmentVariable("ABIOTIC_SCENERY_PREPARE") != "1") return;
        if (!NativeDecoder.TryLoad(Environment.GetEnvironmentVariable("ABIOTIC_NATIVES_DIR")))
            throw new InvalidOperationException("Set ABIOTIC_NATIVES_DIR to the folder holding CUE4Parse-Natives (see assets/scenery/README.md).");
        using var assets = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall(includeMods: false)
            ?? throw new InvalidOperationException("No installed game found.");
        PluginHostEnvironment.GameAssets = () => assets;
        try
        {
            var host = new ExportHost(output);
            var provider = new PakSceneModelProvider(host);
            output.WriteLine("Cache: " + provider.CacheRoot);
            provider.PrepareHostedScenery(message => { output.WriteLine(message); Console.WriteLine(message); }, CancellationToken.None);
        }
        finally { PluginHostEnvironment.GameAssets = static () => null; }
    }

    /// <summary>
    /// Prepares how every placeable object looks into this install's cache (<c>ABIOTIC_SCENERY_PREPARE=1</c>),
    /// then <c>python tools/scenery.py export --extend</c> adds it to the hosted build. Needs no native decoder.
    /// </summary>
    [Fact]
    public void Prepare_object_models_for_Pages()
    {
        if (Environment.GetEnvironmentVariable("ABIOTIC_SCENERY_PREPARE") != "1") return;
        using var assets = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall(includeMods: false)
            ?? throw new InvalidOperationException("No installed game found.");
        PluginHostEnvironment.GameAssets = () => assets;
        try
        {
            var provider = new PakSceneModelProvider(new ExportHost(output));
            output.WriteLine("Cache: " + provider.CacheRoot);
            provider.PrepareHostedClasses(message => { output.WriteLine(message); Console.WriteLine(message); }, CancellationToken.None);
        }
        finally { PluginHostEnvironment.GameAssets = static () => null; }
    }

    private sealed class ExportHost(ITestOutputHelper output, string? directory = null) : IPluginHost, IPluginLog
    {
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(2, 25);
        public string HostKind => "scenery-export";
        public IPluginLog Log => this;
        public IHostUi Ui => NullHostUi.Instance;
        public string DataDirectory => directory ?? PluginPaths.DataDirectoryFor("com.abioticeditor.game-models-3d");
        public void Info(string message) => output.WriteLine(message);
        public void Warn(string message) => output.WriteLine(message);
        public void Error(string message, Exception? exception = null) => output.WriteLine(message);
    }
}
