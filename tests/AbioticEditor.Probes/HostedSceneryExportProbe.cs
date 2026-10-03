using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.GameModels3D;
using Xunit.Abstractions;
using AbioticEditor.Plugins.Scene;
using System.Net;

namespace AbioticEditor.Tests;

/// <summary>Explicit maintainer preparation; never runs during the ordinary assertion suite.</summary>
public sealed class HostedSceneryExportProbe(ITestOutputHelper output)
{
    [Fact]
    public void Pages_export_renders_without_local_extraction()
    {
        if (Environment.GetEnvironmentVariable("ABIOTIC_SCENERY_VERIFY_ROOT") is not { Length: > 0 } root) return;
        var signature = Path.GetFileName(root);
        var data = Path.Combine(Path.GetTempPath(), "scenery-verify-" + Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new ExportFiles(root, signature));
        var previous = PluginHostEnvironment.GameAssets;
        PluginHostEnvironment.GameAssets = static () => throw new InvalidOperationException("Unexpected local game extraction.");
        try
        {
            var provider = new PakSceneModelProvider(new ExportHost(output, data),
                new HostedSceneryCache(signature, client, new Uri("https://scenery.test/v1/")));
            foreach (var region in new[] { "Facility_Office1", "Facility_Dam", "V_Alps", "V_ISLAND", "V_Winter" })
            {
                var query = new SceneLevelQuery(region, [-1000000, -1000000, -1000000], [1000000, 1000000, 1000000], 2000);
                provider.DescribeLevel(query);
                provider.WaitForLevels(TimeSpan.FromSeconds(30));
                var slice = provider.DescribeLevel(query);
                Assert.NotNull(slice);
                Assert.Equal(0, slice.PendingMaps);
                Assert.NotEmpty(slice.Batches);
                foreach (var batch in slice.Batches.Take(64))
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

    [Fact]
    public void Prepare_all_levels_for_Pages()
    {
        if (Environment.GetEnvironmentVariable("ABIOTIC_SCENERY_PREPARE") != "1") return;
        using var assets = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall(includeMods: false)
            ?? throw new InvalidOperationException("No installed game found.");
        PluginHostEnvironment.GameAssets = () => assets;
        try
        {
            output.WriteLine("Portable scenery signature: " + HostedSceneryCache.Signature(
                AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory(),
                AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings()));
            var host = new ExportHost(output);
            var provider = new PakSceneModelProvider(host);
            provider.PrepareHostedScenery(message => { output.WriteLine(message); Console.WriteLine(message); }, CancellationToken.None);
        }
        finally { PluginHostEnvironment.GameAssets = static () => null; }
    }

    private sealed class ExportFiles(string root, string signature) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath[("/v1/" + signature + "/").Length..];
            var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            return Task.FromResult(File.Exists(file)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
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
