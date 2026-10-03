using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AbioticEditor.Plugins.GameModels3D;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Tests;

public sealed class HostedSceneryCacheTests
{
    private const string Signature = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Filename = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.abm";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Model_provider_consumes_hosted_data_through_its_normal_cache()
    {
        const string classPath = "/Game/Hosted/Example.Example_C";
        var key = "classes-v4/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(classPath))) + ".json";
        var model = new SceneClassModel([], [0, 0, 0], [1, 2, 3]);
        using var fixture = new Fixture(cacheKey: key, payload: JsonSerializer.SerializeToUtf8Bytes(new { model }, Json));
        var provider = new PakSceneModelProvider(new TestHost(fixture.Directory), fixture.Cache);
        Assert.Equal(model.BoundsMax, provider.DescribeClass(classPath)!.BoundsMax);
        Assert.Equal(model.BoundsMax, provider.DescribeClass(classPath)!.BoundsMax);
        Assert.Equal(2, fixture.Requests);
    }

    [Fact]
    public void Verified_download_is_reused_without_another_request()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Cache.Fetch("meshes", Filename, fixture.Target));
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(fixture.Target));
        Assert.True(fixture.Cache.Fetch("meshes", Filename, fixture.Target));
        Assert.Equal(2, fixture.Requests);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("signature")]
    [InlineData("format")]
    [InlineData("missing")]
    public void Invalid_or_missing_hosted_data_leaves_local_baking_available(string fault)
    {
        using var fixture = new Fixture(fault);
        Assert.False(fixture.Cache.Fetch("meshes", Filename, fixture.Target));
        Assert.False(File.Exists(fixture.Target));
        Assert.Empty(Directory.GetFiles(fixture.Directory));
    }

    [Fact]
    public void Offline_requests_stop_after_one_failure_and_do_not_accept_paths()
    {
        using var fixture = new Fixture("offline");
        Assert.False(fixture.Cache.Fetch("../meshes", Filename, fixture.Target));
        Assert.Equal(0, fixture.Requests);
        Assert.False(fixture.Cache.Fetch("meshes", Filename, fixture.Target));
        Assert.False(fixture.Cache.Fetch("textures", Filename.Replace(".abm", ".png", StringComparison.Ordinal), fixture.Target));
        Assert.Equal(1, fixture.Requests);
    }

    [Fact]
    public void Signature_ignores_install_dates_but_changes_with_archive_indexes_and_mappings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var pak = Path.Combine(directory, "game.pak");
            var toc = Path.Combine(directory, "game.utoc");
            var mappings = Path.Combine(directory, "Mappings.usmap");
            File.WriteAllText(pak, "pak footer");
            File.WriteAllText(toc, "index one");
            File.WriteAllText(mappings, "mappings");
            var first = HostedSceneryCache.Signature(directory, mappings);
            // Shared golden identity with tools/test_scenery.py's synthetic archive set.
            Assert.Equal("961a189f71b4bfba8b91aa79936181742b1eb635f4c0beb3a3cbb2213c346bbd", first);
            File.SetLastWriteTimeUtc(pak, DateTime.UtcNow.AddDays(-10));
            Assert.Equal(first, HostedSceneryCache.Signature(directory, mappings));
            File.WriteAllText(toc, "index two");
            var changed = HostedSceneryCache.Signature(directory, mappings);
            Assert.NotEqual(first, changed);
            File.WriteAllText(mappings, "new mappings");
            Assert.NotEqual(changed, HostedSceneryCache.Signature(directory, mappings));
        }
        finally { System.IO.Directory.Delete(directory, recursive: true); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Target => Path.Combine(Directory, Filename);
        public byte[] Bytes { get; }
        public int Requests { get; private set; }
        public HostedSceneryCache Cache { get; }
        private readonly string? _fault;
        private readonly HttpClient _client;
        private readonly string _key;

        public Fixture(string? fault = null, string? cacheKey = null, byte[]? payload = null)
        {
            _fault = fault;
            _key = cacheKey ?? "meshes/" + Filename;
            Bytes = payload ?? Encoding.UTF8.GetBytes("a baked mesh");
            System.IO.Directory.CreateDirectory(Directory);
            _client = new HttpClient(this, disposeHandler: false);
            Cache = new HostedSceneryCache(Signature, _client, new Uri("https://example.test/scenery/v1/"));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (_fault == "offline") throw new HttpRequestException("Offline");
            if (_fault == "missing") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var manifest = new HostedSceneryCache.Manifest(_fault == "format" ? 2 : 1, _fault == "signature" ? "wrong" : Signature,
                new Dictionary<string, HostedSceneryCache.Entry>
                {
                    [_key] = new(_fault == "size" ? Bytes.Length + 1 : Bytes.Length,
                        _fault == "hash" ? Signature : Convert.ToHexStringLower(SHA256.HashData(Bytes))),
                });
            var data = request.RequestUri!.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal)
                ? JsonSerializer.SerializeToUtf8Bytes(manifest, Json) : Bytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.Dispose();
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TestHost(string directory) : IPluginHost, IPluginLog
    {
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(2, 25);
        public string HostKind => "test";
        public IPluginLog Log => this;
        public IHostUi Ui => NullHostUi.Instance;
        public string DataDirectory => directory;
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
