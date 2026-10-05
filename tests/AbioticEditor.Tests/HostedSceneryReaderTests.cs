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
            // The desktop never downloads scenery: its cache is filled here with exactly the files the
            // browser fetched, so any file the desktop would need beyond those shows up as a difference.
            var provider = new PakSceneModelProvider(new TestHost(directory));
            site.CopyRequestedTo(provider.CacheRoot);
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

    private const string Workbench = "/Game/Blueprints/DeployedObjects/Furniture/Deployed_CraftingBench_Default.Deployed_CraftingBench_Default_C";

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Placed_objects_are_drawn_from_hosted_models(bool packed)
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        site.ServeClasses = packed;
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var answer = await reader.DescribeClassesAsync([Workbench, Workbench + "#lamp=0", "/Game/Blueprints/DeployedObjects/Furniture/Deployed_NoSuchThing.Deployed_NoSuchThing_C"]);
        Assert.NotEmpty(answer[Workbench]!.Parts);
        Assert.NotEmpty(answer[Workbench + "#lamp=0"]!.Parts);
        Assert.False(answer.ContainsKey("/Game/Blueprints/DeployedObjects/Furniture/Deployed_NoSuchThing.Deployed_NoSuchThing_C"));
        foreach (var part in answer[Workbench]!.Parts)
        {
            Assert.StartsWith(site.Build.AbsoluteUri, part.Mesh, StringComparison.Ordinal);
            Assert.True(site.Has(part.Mesh[site.Build.AbsoluteUri.Length..]), part.Mesh);
            foreach (var texture in part.Materials.Select(m => m.Texture).OfType<string>())
                Assert.True(site.Has(texture[site.Build.AbsoluteUri.Length..]), texture);
        }
        // One download answers every class when the build has classes.json; otherwise each class is its own file.
        Assert.Contains("classes.json", site.Requested);
        Assert.Equal(!packed, site.Requested.Any(p => p.StartsWith("classes-v4/", StringComparison.Ordinal)));
    }

    [SkippableFact]
    public async Task A_garden_plot_shows_the_crops_it_holds_and_a_barrel_its_fill()
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        const string Plot = "/Game/Blueprints/DeployedObjects/Farming/GardenPlot_Medium.GardenPlot_Medium_C";
        const string Barrel = "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LiquidContainer_Barrel.Deployed_LiquidContainer_Barrel_C";
        var answer = await reader.DescribeClassesAsync([
            Plot, Plot + "#crops=1.Plant_Corn.4,2.Plant_Corn.0",
            Barrel + "#liquid=0", Barrel + "#liquid=1000", Barrel + "#liquid=10000"]);

        var bare = answer[Plot]!;
        var planted = answer[Plot + "#crops=1.Plant_Corn.4,2.Plant_Corn.0"]!;
        Assert.DoesNotContain(bare.Parts, p => p.Name?.StartsWith("Plot", StringComparison.Ordinal) == true && p.Name.Contains("Plant_", StringComparison.Ordinal));
        var grown = planted.Parts.Where(p => p.Name?.StartsWith("Plot2/Plant_Corn", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(4, grown.Count); // the plant and its three ears, as the desktop draws it
        Assert.Contains(planted.Parts, p => p.Name == "Plot3/Plant_Corn");
        foreach (var part in grown) Assert.True(site.Has(part.Mesh[site.Build.AbsoluteUri.Length..]), part.Mesh);

        Assert.DoesNotContain(answer[Barrel + "#liquid=0"]!.Parts, p => p.Name == "WaterLevel");
        var low = answer[Barrel + "#liquid=1000"]!.Parts.Single(p => p.Name == "WaterLevel").Matrix[13];
        var full = answer[Barrel + "#liquid=10000"]!.Parts.Single(p => p.Name == "WaterLevel").Matrix[13];
        // The surface runs from 1 cm to 97 cm of the barrel (viewer Y is up, in metres), as the desktop's own test checks.
        Assert.InRange(full, 0.95f, 0.99f);
        Assert.InRange(low, 0.09f, 0.12f);
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
    }

    [SkippableFact]
    public async Task A_first_look_at_an_area_takes_a_handful_of_requests_and_says_how_far_it_got()
    {
        using var site = HostedSite.Open();
        Skip.If(site is null, "the prepared scenery is not in this checkout");
        var reader = new HostedSceneryReader(new HttpClient(site), Root);
        var slice = await reader.DescribeLevelAsync(new SceneLevelQuery("Facility_Office1", [-40, -20, -40], [40, 20, 40], 25000), TimeSpan.FromMinutes(2));
        Assert.NotEmpty(slice!.Batches);
        // index, world, descriptions and the level files: no request per mesh description or material.
        var asked = site.Requested.Distinct(StringComparer.Ordinal).ToList();
        Assert.Contains("descriptions.json", asked);
        Assert.DoesNotContain(asked, p => p.StartsWith("meshinfo/", StringComparison.Ordinal) || p.StartsWith("materials-", StringComparison.Ordinal));
        Assert.True(asked.Count < 20, string.Join(", ", asked));
        var progress = reader.Progress();
        Assert.True(progress[1] > 0);
        Assert.Equal(progress[1], progress[0]); // everything asked for has arrived
    }

    [SkippableFact]
    public async Task The_descriptions_pack_answers_exactly_as_the_separate_files_do()
    {
        using var packed = HostedSite.Open();
        Skip.If(packed is null, "the prepared scenery is not in this checkout");
        using var separate = HostedSite.Open()!;
        separate.ServeDescriptions = false;
        var query = new SceneLevelQuery("Facility_Office1", [-40, -20, -40], [40, 20, 40], 25000);
        var fromPack = await new HostedSceneryReader(new HttpClient(packed), Root).DescribeLevelAsync(query, TimeSpan.FromMinutes(2));
        var fromFiles = await new HostedSceneryReader(new HttpClient(separate), Root).DescribeLevelAsync(query, TimeSpan.FromMinutes(2));
        Assert.Equal(Comparable(fromFiles, separate.Build), Comparable(fromPack, packed.Build));
        Assert.Contains(separate.Requested, p => p.StartsWith("meshinfo/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_is_downloaded_until_the_player_agrees()
    {
        // The answer is a preference; without a browser store installed it is a per-user file.
        var consent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "hosted-scenery.txt");
        var had = File.Exists(consent) ? File.ReadAllText(consent) : null;
        try
        {
            if (had is not null) File.Delete(consent);
            using var handler = new NotFound();
            var reader = new HostedSceneryReader(new HttpClient(handler), Root);
            Assert.False(reader.Allowed);
            Assert.False((await reader.Status()).Available);
            Assert.Null(await reader.DescribeLevel(new SceneLevelQuery("Facility_Office1", [0, 0, 0], [1, 1, 1], 10)));
            Assert.Equal(0, handler.Requests); // not even the build index

            reader.SetAllowed(true);
            Assert.True(reader.Allowed);
            await reader.Status();
            Assert.Equal(1, handler.Requests); // now it looks for scenery
            reader.SetAllowed(false);
            Assert.False(reader.Allowed);
        }
        finally
        {
            if (had is null) { if (File.Exists(consent)) File.Delete(consent); }
            else File.WriteAllText(consent, had);
        }
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

        private HostedSite(string directory)
        {
            Signature = Path.GetFileName(directory);
            Build = new Uri(Root, Signature + "/");
            _packs = Directory.GetFiles(directory, "part-*.zip").Order(StringComparer.Ordinal).Select(ZipFile.OpenRead).ToList();
            _files = _packs.SelectMany(p => p.Entries).ToDictionary(e => e.FullName, StringComparer.Ordinal);
        }

        public string Signature { get; }

        /// <summary>Serve the build's descriptions.json (as tools/scenery.py assemble publishes it).</summary>
        public bool ServeDescriptions { get; set; } = true;

        /// <summary>Serve the build's classes.json (the placed objects' models, as tools/scenery.py assemble publishes it).</summary>
        public bool ServeClasses { get; set; } = true;

        private byte[]? _descriptions;
        private byte[]? _classes;

        private byte[] Descriptions() => Pack(ref _descriptions, "meshinfo", "materials-v5", "texture-alpha-v1", "terrain-materials-v1");

        private byte[] Classes() => Pack(ref _classes, "classes-v4", "liquids-v1");

        private byte[] Pack(ref byte[]? cached, params string[] folders)
        {
            lock (_packs)
            {
                if (cached is not null) return cached;
                var pack = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var (key, entry) in _files)
                {
                    if (!folders.Contains(key[..key.IndexOf('/', StringComparison.Ordinal)], StringComparer.Ordinal)) continue;
                    using var stream = entry.Open();
                    pack[key] = JsonDocument.Parse(stream).RootElement.Clone();
                }
                return cached = JsonSerializer.SerializeToUtf8Bytes(pack);
            }
        }

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

        /// <summary>Writes every file asked for so far into a provider's cache folder, under its cache name.</summary>
        public void CopyRequestedTo(string cacheRoot)
        {
            // descriptions.json stands for every small description file in it.
            var asked = Requested.Distinct(StringComparer.Ordinal).ToList();
            if (asked.Contains("descriptions.json", StringComparer.Ordinal))
                asked.AddRange(_files.Keys.Where(k => k.StartsWith("meshinfo/", StringComparison.Ordinal) || k.StartsWith("materials-v5/", StringComparison.Ordinal)
                    || k.StartsWith("texture-alpha-v1/", StringComparison.Ordinal) || k.StartsWith("terrain-materials-v1/", StringComparison.Ordinal)));
            foreach (var published in asked)
            {
                var key = published.EndsWith(".ali", StringComparison.Ordinal) ? published[..^4] + ".bin" : published;
                if (!_files.TryGetValue(key, out var entry)) continue;
                var target = Path.Combine(cacheRoot, key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                lock (_packs) entry.ExtractToFile(target, overwrite: true);
            }
        }

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
                // No manifest.json: Pages does not publish it (the browser needs none).
                if (key == "descriptions.json") data = ServeDescriptions ? Descriptions() : null;
                else if (key == "classes.json") data = ServeClasses ? Classes() : null;
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
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
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
