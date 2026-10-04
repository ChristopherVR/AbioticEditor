using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.GameModels3D;
using AbioticEditor.Plugins.Scene;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The browser editor's scenery (<see cref="HostedSceneryReader"/>) against the desktop Game Models
/// provider, both served the real prepared scenery committed under <c>assets/scenery</c>.
/// </summary>
public sealed class HostedSceneryReaderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Uri Root = new("https://example.test/scenery/v1/");

    [SkippableTheory]
    [InlineData("Facility_Office1")]
    [InlineData("Facility")]
    public async Task The_browser_draws_the_same_level_the_desktop_draws_from_the_same_scenery(string region)
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var query = new SceneLevelQuery(region, [-60, -30, -60], [60, 30, 60], 3000);
        var browser = await reader.DescribeLevelAsync(query, TimeSpan.FromMinutes(2));
        Assert.NotNull(browser);
        Assert.Equal(0, browser.PendingMaps);
        Assert.NotEmpty(browser.Batches);

        // The same query with doors held open and a few actors excluded, which both must honour alike.
        var doors = browser.Actors.Where(a => a.Contains("Door", StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        var opened = query with
        {
            OpenDoors = doors.Select((d, i) => i % 2 == 0 ? d : d + "|in").ToList(),
            ExcludeActors = browser.Actors.Skip(1).Take(3).Select(a => a[(a.IndexOf(':') + 1)..]).ToList(),
        };
        var browserOpened = await reader.DescribeLevelAsync(opened, TimeSpan.FromMinutes(2));

        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new PakSceneModelProvider(new TestHost(directory), new HostedSceneryCache(site.Signature, new HttpClient(site), Root));
            var desktop = DescribeSettled(provider, query);
            var desktopOpened = DescribeSettled(provider, opened);
            Assert.Equal(Comparable(desktop, site.Build), Comparable(browser, site.Build));
            Assert.Equal(Comparable(desktopOpened, site.Build), Comparable(browserOpened, site.Build));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Hosted_meshes_and_textures_are_addressed_as_files_the_website_has()
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var slice = await reader.DescribeLevelAsync(new SceneLevelQuery("Facility_Office1", [-40, -20, -40], [40, 20, 40], 2000), TimeSpan.FromMinutes(2));
        Assert.NotNull(slice);
        var urls = slice.Batches.Select(b => b.Mesh)
            .Concat(slice.Batches.SelectMany(b => b.Materials).SelectMany(m => (m.Layers ?? []).Select(l => l.Texture).Append(m.Texture)).OfType<string>())
            .Distinct().ToList();
        Assert.Contains(urls, u => u.Contains("/textures/", StringComparison.Ordinal));
        foreach (var url in urls)
        {
            Assert.StartsWith(site.Build.AbsoluteUri, url, StringComparison.Ordinal);
            Assert.True(site.Has(url[site.Build.AbsoluteUri.Length..]), url);
        }
    }

    [SkippableFact]
    public async Task Level_files_are_fetched_under_a_name_download_managers_leave_alone()
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var slice = await reader.DescribeLevelAsync(new SceneLevelQuery("Facility_Office1", [-20, -10, -20], [20, 10, 20], 500), TimeSpan.FromMinutes(2));
        Assert.NotEmpty(slice!.Batches);
        Assert.Contains(site.Requested, p => p.StartsWith("levels/", StringComparison.Ordinal) && p.EndsWith(".ali", StringComparison.Ordinal));
        Assert.DoesNotContain(site.Requested, p => p.EndsWith(".bin", StringComparison.Ordinal) || p.EndsWith(".zip", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task A_captured_level_file_is_left_out_and_not_asked_for_again()
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        site.Captured = ".ali";
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var query = new SceneLevelQuery("Facility_Office1", [-20, -10, -20], [20, 10, 20], 500);
        var first = await reader.DescribeLevelAsync(query, TimeSpan.FromMinutes(2));
        var asked = site.Requested.Count(p => p.EndsWith(".ali", StringComparison.Ordinal));
        var second = await reader.DescribeLevelAsync(query, TimeSpan.FromMinutes(2));
        Assert.NotNull(first);
        Assert.Empty(first.Batches);
        Assert.Equal(0, first.PendingMaps);
        Assert.Empty(second!.Batches);
        Assert.True(asked > 0);
        // Asking again would put another "save this file?" prompt in front of the player.
        Assert.Equal(asked, site.Requested.Count(p => p.EndsWith(".ali", StringComparison.Ordinal)));
    }

    [Fact]
    public void Level_indexes_are_published_as_ali_and_everything_else_keeps_its_name()
    {
        Assert.Equal("levels/abc.ali", HostedSceneryReader.PublishedPath("levels/abc.bin"));
        Assert.Equal("meshes/abc.abm", HostedSceneryReader.PublishedPath("meshes/abc.abm"));
        Assert.Equal(HostedSceneryReader.PublishedPath("levels/abc.bin"), HostedSceneryCache.PublishedPath("levels/abc.bin"));
        Assert.Equal(HostedSceneryReader.PublishedPath("textures/abc.png"), HostedSceneryCache.PublishedPath("textures/abc.png"));
    }

    [Fact]
    public async Task Without_hosted_scenery_the_browser_keeps_its_boxes()
    {
        using var handler = new NotFound();
        var reader = new HostedSceneryReader(new HttpClient(handler), Root);
        Assert.False((await reader.Status()).Available);
        Assert.Null(await reader.DescribeLevel(new SceneLevelQuery("Facility_Office1", [0, 0, 0], [1, 1, 1], 10)));
        Assert.Empty(await reader.DescribeClasses(["/Game/Example.Example_C"]));
    }

    /// <summary>The desktop answer once its background level reading has finished.</summary>
    private static SceneLevelSlice? DescribeSettled(PakSceneModelProvider provider, SceneLevelQuery query)
    {
        var slice = provider.DescribeLevel(query);
        for (var tries = 0; slice is { PendingMaps: > 0 } && tries < 10; tries++)
        {
            provider.WaitForLevels(TimeSpan.FromMinutes(2));
            slice = provider.DescribeLevel(query);
        }
        return slice;
    }

    /// <summary>A slice as JSON, with the desktop's asset ids turned into the hosted files' addresses.</summary>
    private static string Comparable(SceneLevelSlice? slice, Uri build)
    {
        if (slice is null) return "null";
        string Texture(string? id) => id is null || id.StartsWith("https:", StringComparison.Ordinal) ? id! : HostedSceneryReader.TextureUrl(build, id);
        var hosted = slice with
        {
            Batches = slice.Batches.Select(b => b with
            {
                Mesh = b.Mesh.StartsWith("https:", StringComparison.Ordinal) ? b.Mesh : HostedSceneryReader.MeshUrl(build, b.Mesh),
                Materials = b.Materials.Select(m => m with
                {
                    Texture = m.Texture is null ? null : Texture(m.Texture),
                    Layers = m.Layers?.Select(l => l with { Texture = l.Texture is null ? null : Texture(l.Texture) }).ToList(),
                }).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(hosted, Json);
    }

    /// <summary>The website's <c>/scenery/v1/</c>, served from the committed source packs.</summary>
    private sealed class HostedSite : HttpMessageHandler
    {
        private readonly List<ZipArchive> _packs;
        private readonly Dictionary<string, ZipArchiveEntry> _files;
        private readonly byte[] _manifest;

        private HostedSite(string directory)
        {
            Signature = Path.GetFileName(directory);
            Build = new Uri(Root, Signature + "/");
            _manifest = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
            _packs = Directory.GetFiles(directory, "part-*.zip").Order(StringComparer.Ordinal).Select(ZipFile.OpenRead).ToList();
            _files = _packs.SelectMany(p => p.Entries).ToDictionary(e => e.FullName, StringComparer.Ordinal);
        }

        public string Signature { get; }

        /// <summary>Addresses ending in this are taken by a (pretend) download manager.</summary>
        public string? Captured { get; set; }

        public Uri Build { get; }

        public static HostedSite? Open()
        {
            var source = Path.Combine(UiSource.RepositoryRoot, "assets", "scenery");
            var build = Directory.Exists(source)
                ? Directory.GetDirectories(source).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")) && Directory.GetFiles(d, "part-*.zip").Length > 0)
                : null;
            return build is null ? null : new HostedSite(build);
        }

        public bool Has(string key) => _files.ContainsKey(key);

        /// <summary>Every published path asked for, in order.</summary>
        public System.Collections.Concurrent.ConcurrentQueue<string> Requested { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsoluteUri;
            // A download manager taking the request for itself leaves the page an empty "204 No Content".
            if (Captured is { } captured && path.EndsWith(captured, StringComparison.Ordinal))
            {
                Requested.Enqueue(path[Build.AbsoluteUri.Length..]);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) });
            }
            byte[]? data = null;
            if (path == new Uri(Root, "index.json").AbsoluteUri)
                data = Encoding.UTF8.GetBytes($$"""{"format":1,"latest":"{{Signature}}","builds":["{{Signature}}"]}""");
            else if (path.StartsWith(Build.AbsoluteUri, StringComparison.Ordinal))
            {
                // As published on Pages: level indexes only under their .ali name (see scenery.py published_name).
                var published = path[Build.AbsoluteUri.Length..];
                var key = published.EndsWith(".ali", StringComparison.Ordinal) ? published[..^4] + ".bin"
                    : published.EndsWith(".bin", StringComparison.Ordinal) ? "" : published;
                Requested.Enqueue(published);
                if (key == "manifest.json") data = _manifest;
                else if (_files.TryGetValue(key, out var entry))
                {
                    lock (_packs)
                    {
                        using var stream = entry.Open();
                        using var copy = new MemoryStream();
                        stream.CopyTo(copy);
                        data = copy.ToArray();
                    }
                }
            }
            return Task.FromResult(data is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var pack in _packs) pack.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class TestHost(string directory) : IPluginHost, IPluginLog
    {
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(2, 26);
        public string HostKind => "test";
        public IPluginLog Log => this;
        public IHostUi Ui => NullHostUi.Instance;
        public string DataDirectory => directory;
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
