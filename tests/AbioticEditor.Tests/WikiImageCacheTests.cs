using System.IO;
using System.Text;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// WikiImageCache behavior with a fake downloader (no network is ever touched):
/// path safety, download-once caching, magic-byte format validation, and the
/// failure paths that must yield null instead of throwing.
/// </summary>
public sealed class WikiImageCacheTests : IDisposable
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];

    private static readonly byte[] JpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private static readonly byte[] WebpBytes =
        Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 4, 0, 0, 0 })
            .Concat(Encoding.ASCII.GetBytes("WEBPVP8 ")).ToArray();

    private static readonly byte[] GifBytes = Encoding.ASCII.GetBytes("GIF89a trailer");

    private static readonly byte[] HtmlBytes =
        Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>File not found</body></html>");

    private readonly string _tempDir;

    public WikiImageCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Path.GetRandomFileName());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only; never fail a test over a locked temp file.
        }
    }

    [Fact]
    public async Task Downloads_Caches_And_NeverRedownloads()
    {
        var calls = 0;
        var cache = new WikiImageCache(_tempDir, _ =>
        {
            calls++;
            return Task.FromResult<byte[]?>(PngBytes);
        });

        var first = await cache.GetAsync("Itemicon_antefish.png");
        var second = await cache.GetAsync("Itemicon_antefish.png");

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, calls);
        Assert.True(File.Exists(first));
        Assert.Equal(".png", Path.GetExtension(first));

        // A brand-new instance over the same directory hits the disk cache: still
        // no second download.
        var failingCache = new WikiImageCache(
            _tempDir, _ => throw new InvalidOperationException("must not download"));
        var third = await failingCache.GetAsync("Itemicon_antefish.png");
        Assert.Equal(first, third);
    }

    [Fact]
    public async Task FilePrefix_Spaces_And_Case_ShareOneCacheEntry()
    {
        var calls = 0;
        var cache = new WikiImageCache(_tempDir, _ =>
        {
            calls++;
            return Task.FromResult<byte[]?>(PngBytes);
        });

        var a = await cache.GetAsync("File:Item Icon - Gem Crab.png");
        var b = await cache.GetAsync("Item_Icon_-_Gem_Crab.png");

        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("..\\..\\evil.png")]
    [InlineData("../../evil.png")]
    [InlineData("C:\\Windows\\system32\\evil.png")]
    [InlineData("a/b/c.png")]
    [InlineData("con.png")]
    public async Task HostileNames_StayInsideTheCacheDirectory(string name)
    {
        var cache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(PngBytes));

        var path = await cache.GetAsync(name);

        Assert.NotNull(path);
        var fullCacheDir = Path.GetFullPath(_tempDir) + Path.DirectorySeparatorChar;
        Assert.StartsWith(fullCacheDir, Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SafeName_ReplacesSeparatorsAndStripsExtension()
    {
        Assert.Equal("Itemicon_antefish", WikiImageCache.SafeNameFor("File:Itemicon antefish.png"));
        Assert.Equal("a_b_c", WikiImageCache.SafeNameFor("a/b\\c.webp"));
        Assert.Equal("_", WikiImageCache.SafeNameFor("..."));
    }

    [Fact]
    public async Task Extension_FollowsActualImageFormat_NotTheRequestedName()
    {
        // The wiki may serve WebP/JPEG even for a name ending in .png.
        var cache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(WebpBytes));
        var path = await cache.GetAsync("SomeFish.png");
        Assert.NotNull(path);
        Assert.Equal(".webp", Path.GetExtension(path));

        var jpegCache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(JpegBytes));
        var jpeg = await jpegCache.GetAsync("Other.png");
        Assert.Equal(".jpg", Path.GetExtension(jpeg));

        var gifCache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(GifBytes));
        var gif = await gifCache.GetAsync("Anim.png");
        Assert.Equal(".gif", Path.GetExtension(gif));
    }

    [Fact]
    public async Task NonImageResponse_ReturnsNull_AndCachesNothing()
    {
        // A missing file typically comes back as an HTML error page.
        var cache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(HtmlBytes));

        var path = await cache.GetAsync("Nope.png");

        Assert.Null(path);
        Assert.False(Directory.Exists(_tempDir) && Directory.EnumerateFiles(_tempDir).Any());
    }

    [Fact]
    public async Task NotFound_And_Offline_ReturnNull()
    {
        var notFound = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(null));
        Assert.Null(await notFound.GetAsync("Missing.png"));

        var offline = new WikiImageCache(
            _tempDir, _ => throw new HttpRequestException("no network"));
        Assert.Null(await offline.GetAsync("Offline.png"));
    }

    [Fact]
    public async Task FailedLookup_IsRetriedOnNextRequest_NotNegativelyCached()
    {
        var calls = 0;
        var cache = new WikiImageCache(_tempDir, _ =>
        {
            calls++;
            return Task.FromResult<byte[]?>(calls == 1 ? null : PngBytes);
        });

        Assert.Null(await cache.GetAsync("Flaky.png"));
        Assert.NotNull(await cache.GetAsync("Flaky.png"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetFirst_ReturnsTheFirstCandidateThatResolves()
    {
        var cache = new WikiImageCache(_tempDir, url =>
            Task.FromResult<byte[]?>(url.Contains("Second", StringComparison.Ordinal) ? PngBytes : null));

        var path = await cache.GetFirstAsync(["First.png", "Second.png", "Third.png"]);

        Assert.NotNull(path);
        Assert.Contains("Second", path, StringComparison.Ordinal);

        Assert.Null(await cache.GetFirstAsync(["First.png", "Third.png"]));
    }

    [Fact]
    public async Task DownloadUrl_UsesSpecialFilePath_WithEscapedName()
    {
        string? requested = null;
        var cache = new WikiImageCache(_tempDir, url =>
        {
            requested = url;
            return Task.FromResult<byte[]?>(PngBytes);
        });

        await cache.GetAsync("Item Icon - Gem Crab.png");

        Assert.Equal(
            "https://abioticfactor.wiki.gg/wiki/Special:FilePath/Item_Icon_-_Gem_Crab.png",
            requested);
    }

    [Fact]
    public async Task BlankName_ReturnsNull()
    {
        var cache = new WikiImageCache(_tempDir, _ => Task.FromResult<byte[]?>(PngBytes));
        Assert.Null(await cache.GetAsync(""));
        Assert.Null(await cache.GetAsync("   "));
    }

    // ---------- the offline bundled fallback ----------

    [Fact]
    public async Task WikiUnavailable_ServesBundledFallback_WithoutCachingIt()
    {
        var bundleDir = Path.Combine(_tempDir, "bundle");
        Directory.CreateDirectory(bundleDir);
        // The bundle ships SafeNameFor(name) + the real extension, like the cache writes.
        var bundled = Path.Combine(bundleDir, WikiImageCache.SafeNameFor("Itemicon_antefish.png") + ".png");
        await File.WriteAllBytesAsync(bundled, PngBytes);

        var cacheDir = Path.Combine(_tempDir, "cache");
        var offline = new WikiImageCache(
            cacheDir, _ => throw new HttpRequestException("no network"), bundleDir);

        var path = await offline.GetAsync("Itemicon_antefish.png");

        // Falls back to the bundled copy...
        Assert.Equal(bundled, path);
        // ...but does NOT copy it into the per-machine cache, so a later online lookup
        // still re-fetches the (possibly updated) wiki image.
        Assert.False(Directory.Exists(cacheDir) && Directory.EnumerateFiles(cacheDir).Any());
    }

    [Fact]
    public async Task LiveWiki_WinsOverBundledFallback()
    {
        var bundleDir = Path.Combine(_tempDir, "bundle");
        Directory.CreateDirectory(bundleDir);
        await File.WriteAllBytesAsync(
            Path.Combine(bundleDir, WikiImageCache.SafeNameFor("Antefish.png") + ".png"), PngBytes);

        var cacheDir = Path.Combine(_tempDir, "cache");
        var cache = new WikiImageCache(cacheDir, _ => Task.FromResult<byte[]?>(JpegBytes), bundleDir);

        var path = await cache.GetAsync("Antefish.png");

        // The live download wins: result lives in the cache dir, not the bundle.
        Assert.NotNull(path);
        Assert.StartsWith(Path.GetFullPath(cacheDir), Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".jpg", Path.GetExtension(path));
    }

    [Fact]
    public async Task NoBundleAndNoWiki_ReturnsNull()
    {
        var cache = new WikiImageCache(
            _tempDir,
            _ => Task.FromResult<byte[]?>(null),
            Path.Combine(_tempDir, "empty-bundle"));

        Assert.Null(await cache.GetAsync("Missing.png"));
    }

    [Fact]
    public void Manifest_CoversEveryCatalog_AndIsDeduped()
    {
        var all = WikiImageManifest.AllFiles;

        // Non-empty, ordered, and free of case-insensitive duplicates.
        Assert.NotEmpty(all);
        Assert.Equal(
            all.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            all.Count);

        // Every catalog's verified names are present.
        Assert.Contains("Itemicon_antefish.png", all, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Vehicle_-_Forklift.png", all, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Outlet.png", all, StringComparer.OrdinalIgnoreCase);
    }

    // ---------- the curated wiki-file catalogs ----------

    [Fact]
    public void FishCatalog_RowIdWins_RareVariantsGetTheirOwnArt()
    {
        // Base species: id and display name agree; the id mapping is first.
        Assert.Equal(
            "Itemicon_antefish.png",
            FishWikiImages.CandidatesFor("Antefish", "Antefish")[0]);

        // Rare variants reuse the base display name in-game, so the ROW ID must
        // resolve the rare-specific wiki art.
        Assert.Equal(
            "Itemicon_antefish_rare.png",
            FishWikiImages.CandidatesFor("Antefish_rare1", "Antefish")[0]);
        Assert.Equal(
            "Itemicon_eel_goldentail.png",
            FishWikiImages.CandidatesFor("Eel_rare3", "Gutfish Eel")[0]);
        Assert.Equal(
            "Itemicon_portalfish_rare_torii.png",
            FishWikiImages.CandidatesFor("Portalfish_rare_torii", "Portal Fish")[0]);

        // Internal names differ from display names (Fogfish = Chordfish).
        Assert.Equal(
            "Itemicon_FogFish.png",
            FishWikiImages.CandidatesFor("Fogfish", "Chordfish")[0]);

        // Display-name fallback is case-insensitive and used when the id is unknown.
        Assert.Equal(
            "Item Icon - Gem Crab.png",
            Assert.Single(FishWikiImages.CandidatesFor("SomeNewId", "gem crab")));

        // Unknown (future) fish get best-effort guesses in both wiki naming styles.
        var guesses = FishWikiImages.CandidatesFor("VortexHerring", "Vortex Herring");
        Assert.Equal(2, guesses.Count);
        Assert.Equal("Item Icon - Vortex Herring.png", guesses[0]);
        Assert.Equal("Itemicon_Vortex_Herring.png", guesses[1]);

        Assert.Empty(FishWikiImages.CandidatesFor(null, null));
        Assert.Empty(FishWikiImages.CandidatesFor("UnknownId", "  "));

        // Every curated entry is a .png wiki file name.
        Assert.All(FishWikiImages.KnownRows.Values,
            f => Assert.EndsWith(".png", f, StringComparison.Ordinal));
        Assert.All(FishWikiImages.KnownFish.Values,
            f => Assert.EndsWith(".png", f, StringComparison.Ordinal));
        Assert.Equal(30, FishWikiImages.KnownRows.Count);
        Assert.Equal(31, FishWikiImages.KnownFish.Count);
    }

    [Fact]
    public void DoorCatalog_OnlyMapsKnownDoorClasses()
    {
        // Honest subset: the wiki pictures almost no door types (see catalog docs).
        Assert.Equal("Vehicle - Tram.png", DoorWikiImageCatalog.WikiFileFor("TramRailDoor_C"));
        Assert.Null(DoorWikiImageCatalog.WikiFileFor("SecurityDoor_C"));
        Assert.Null(DoorWikiImageCatalog.WikiFileFor(null));

        // Every mapped class must exist in the door class catalog (no orphans).
        Assert.All(DoorWikiImageCatalog.MappedClasses.Keys,
            cls => Assert.True(DoorClassCatalog.KnownClasses.ContainsKey(cls),
                $"{cls} is not a known door class"));
    }
}
