using System;
using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Guards the game data that actually ships in <c>assets/</c>, read exactly the way the browser
/// build reads it (whole file in memory, no game install anywhere).
/// </summary>
/// <remarks>
/// This exists because the browser build once shipped with only the item table filled in. Nothing
/// threw: recipes, the codex, traders, traits and the appearance options were simply empty, and
/// the editor looked like it had loaded fine while showing almost nothing. A green test suite and
/// a page that renders are not evidence the data arrived - only asserting on the shipped file is.
/// </remarks>
public sealed class BundledGameDataTests
{
    private static string AssetsDirectory => Path.Combine(UiSource.RepositoryRoot, "assets");

    [Fact]
    public void ShippedRegistry_CarriesEveryCatalogTheEditorReads()
    {
        var path = Path.Combine(AssetsDirectory, "registry", GameDataRegistry.RegistryFileName);
        Assert.True(File.Exists(path), $"The bundled registry is missing: {path}");

        // TryRead, not TryLoad: this is the browser's path, where the file arrives as bytes over
        // HTTP and there is no file system to open.
        var registry = GameDataRegistry.TryRead(File.ReadAllBytes(path));
        Assert.NotNull(registry);
        Assert.Equal(GameDataRegistry.CurrentSchemaVersion, registry!.SchemaVersion);

        AssertHasEnough(registry);
    }

    /// <summary>
    /// Lower bounds, well under the real counts, so a genuinely truncated dump fails while a game
    /// patch that adds or retires a few rows does not. Applied to every language's dump, since a
    /// per-language build could fail for one culture alone.
    /// </summary>
    private static void AssertHasEnough(GameDataRegistry registry)
    {
        AssertHas(registry.Items?.Count, 1_000, "items");
        AssertHas(registry.ItemTableRefs?.Count, 1_000, "item table references");
        AssertHas(registry.Recipes?.Count, 300, "recipes");
        AssertHas(registry.ItemUpgrades?.Count, 40, "item upgrades");
        AssertHas(registry.Maps?.Count, 5, "maps");
        AssertHas(registry.Skills?.Count, 10, "skills");
        AssertHas(registry.SkillMilestones?.Count, 10, "skill perks");
        AssertHas(registry.Traits?.Count, 20, "traits");
        AssertHas(registry.Customization?.Count, 5, "appearance option tables");
        AssertHas(registry.Emails?.Count, 100, "emails");
        AssertHas(registry.Journals?.Count, 50, "journal entries");
        AssertHas(registry.Compendium?.Count, 100, "compendium entries");
        AssertHas(registry.Fish?.Count, 20, "fish");
        AssertHas(registry.Traders?.Count, 5, "traders");
        AssertHas(registry.SectorMaps?.Count, 5, "sector maps");

        static void AssertHas(int? actual, int atLeast, string what)
            => Assert.True(actual >= atLeast,
                $"The bundled registry carries {actual?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no"} {what}; expected at least {atLeast}. "
                + "Re-run the CLI's dump-registry against a game install and copy the result to assets/registry/.");
    }

    [Fact]
    public void ShippedRegistry_ResolvesRealNamesWithNoGameInstalled()
    {
        var registry = GameDataRegistry.TryRead(
            File.ReadAllBytes(Path.Combine(AssetsDirectory, "registry", GameDataRegistry.RegistryFileName)));
        Assert.NotNull(registry);

        // A recipe that names a real crafted item is the thing the browser could not do before:
        // it proves the ids in one catalog line up with another's, not just that rows exist.
        var withIngredients = Assert.Single(
            registry!.Recipes!, recipe => recipe.Id.Equals("recipe_bandage", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(withIngredients.CreatesItemId));
        Assert.NotEmpty(withIngredients.IngredientList);

        var crafted = Assert.Single(
            registry.Items!, item => item.Id.Equals(withIngredients.CreatesItemId, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(crafted.DisplayName));
        Assert.NotEqual(crafted.Id, crafted.DisplayName);
    }

    /// <summary>
    /// Every language the game ships must have its own dump, and each must actually be in that
    /// language.
    /// </summary>
    /// <remarks>
    /// The browser build has no game to read, so a missing language file silently means English
    /// names for that player. Sizes alone would not catch a bug that wrote ten copies of English,
    /// so this compares the actual text.
    /// </remarks>
    [Theory]
    [InlineData("de")]
    [InlineData("es-419")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void ShippedRegistry_HasATranslatedDumpFor(string culture)
    {
        var path = Path.Combine(AssetsDirectory, "registry", GameDataRegistry.FileNameFor(culture));
        Assert.True(File.Exists(path),
            $"No game data shipped for '{culture}'. Re-run the CLI's dump-registry --all-cultures.");

        var localized = GameDataRegistry.TryRead(File.ReadAllBytes(path));
        Assert.NotNull(localized);
        Assert.Equal(culture, localized!.Culture);
        AssertHasEnough(localized);

        // The same item, in two languages, must not read the same - otherwise the "translated"
        // dump is just another copy of English and nobody would notice until a player complained.
        var fallback = GameDataRegistry.TryRead(
            File.ReadAllBytes(Path.Combine(AssetsDirectory, "registry", GameDataRegistry.RegistryFileName)));
        var translated = NameOf(localized, "scrap_metal");
        var original = NameOf(fallback!, "scrap_metal");
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.NotEqual(original, translated);
    }

    /// <summary>
    /// The hardcoded culture list must match the files that actually ship. It exists so the
    /// browser asks for exactly one file instead of probing and taking a 404 on every page load,
    /// which only works while the two agree.
    /// </summary>
    [Fact]
    public void BundledCultures_MatchTheFilesOnDisk()
    {
        var onDisk = Directory.EnumerateFiles(Path.Combine(AssetsDirectory, "registry"), "registry.*.json")
            .Select(Path.GetFileName)
            .Select(name => name!["registry.".Length..^".json".Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            GameDataRegistry.BundledCultures.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            onDisk);
    }

    [Theory]
    [InlineData("de-DE", "de")]        // regional variant of a shipped language
    [InlineData("de", "de")]
    [InlineData("pt-BR", "pt-BR")]     // exact match wins over the language fallback
    [InlineData("pt-PT", "pt-BR")]     // the game ships no European Portuguese; Brazilian is closer than English
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("nl-NL", null)]        // not shipped at all -> the default dump
    [InlineData("", null)]
    public void BestCultureFor_PicksTheClosestShippedLanguage(string requested, string? expected)
        => Assert.Equal(expected, GameDataRegistry.BestCultureFor(requested));

    private static string? NameOf(GameDataRegistry registry, string itemId)
        => registry.Items?.FirstOrDefault(item =>
            item.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase))?.DisplayName;

    [Fact]
    public void ShippedArt_ManifestMatchesThePicturesBesideIt()
    {
        var artDirectory = Path.Combine(AssetsDirectory, "art");
        var manifestPath = Path.Combine(artDirectory, BundledArt.ManifestFileName);
        Assert.True(File.Exists(manifestPath), $"The bundled art manifest is missing: {manifestPath}");

        var manifest = BundledArt.TryRead(File.ReadAllBytes(manifestPath));
        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest!.Refs);

        // Every listed picture must actually be there. A manifest that promises a file which was
        // not committed is worse than no manifest: the screen draws a broken image instead of
        // falling back to its symbol.
        foreach (var gameRef in manifest.Refs)
        {
            var file = Path.Combine(artDirectory, BundledArt.FileNameFor(gameRef));
            Assert.True(File.Exists(file), $"The art manifest lists '{gameRef}' but {file} is not there.");
        }

        // The logo the shell draws on every screen, which is the most visible one to lose.
        Assert.True(manifest.Has("AbioticFactor/Content/Textures/GUI/Inventory/T_ABF_Logo_1024"));
    }

    /// <summary>
    /// Every shipped item picture is named in lower case, and so is the URL the browser asks for.
    /// </summary>
    /// <remarks>
    /// The dump names each file after the game's own data-table row, but a save spells the same
    /// item differently - the row is <c>bandage</c> where the save says <c>Bandage</c>. Windows,
    /// where the pictures are dumped and where the desktop app reads them, treats those as one
    /// name; the web server that serves the browser build does not, so those items answered 404
    /// and drew a "?" tile. One agreed spelling on both sides is the whole fix, and File.Exists
    /// cannot guard it (it is case-insensitive on Windows) - the names have to be compared here.
    ///
    /// <para><b>This can pass on Windows and still fail in CI, and that is not a flaky test.</b>
    /// It reads the working tree, which on a case-insensitive file system can be perfectly
    /// lower-case while git's own index still holds the old capitalised paths - renaming a file
    /// only in case is exactly the change Windows hides. It has happened once already: the
    /// rename was made, the working tree looked right, and the commit carried the old names, so
    /// the published site kept 404ing. A Linux checkout materialises whatever git actually has,
    /// which is why CI is the thing that catches it. If this fails there and passes here, look
    /// at <c>git ls-files assets/icons</c>, not at the folder.</para>
    /// </remarks>
    [Fact]
    public void ShippedItemIcons_AreNamedInOneCaseTheBrowserCanAskFor()
    {
        var iconDirectory = Path.Combine(AssetsDirectory, "icons");
        Assert.True(Directory.Exists(iconDirectory), $"The bundled item icons are missing: {iconDirectory}");

        var mixedCase = Directory.EnumerateFiles(iconDirectory, "*.png")
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, name!.ToLowerInvariant(), StringComparison.Ordinal))
            .Take(10)
            .ToArray();

        Assert.True(mixedCase.Length == 0,
            "These shipped item icons are not named in lower case, so the browser build 404s on them: "
            + string.Join(", ", mixedCase)
            + ". Re-run the CLI's dump-icons (it lower-cases) and commit the result.");

        // The URL side of the same agreement, checked with an id spelled the way a save spells it.
        using var catalog = new ItemCatalogService(files: new NoLocalPathsFileSystem());
        Assert.Equal("icons/bandage.png", catalog.IconUrl("Bandage"));
    }

    /// <summary>A browser-shaped host: no local paths, so the bundled pictures are used.</summary>
    private sealed class NoLocalPathsFileSystem : AbioticEditor.Web.Services.ISaveFileSystem
    {
        public bool HasLocalPaths => false;
        public bool CanWrite => false;
        public Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<AbioticEditor.Web.Services.SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AbioticEditor.Web.Services.SaveFileEntry>>([]);
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
        public Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<byte>());
        public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void ShippedWikiImages_CoverEveryNameTheEditorAsksFor()
    {
        var wikiDirectory = Path.Combine(AssetsDirectory, "wiki");
        Assert.True(Directory.Exists(wikiDirectory), $"The bundled wiki images are missing: {wikiDirectory}");

        // The browser cannot download these on demand, so anything the manifest claims must ship.
        // Names are compared through SafeNameFor, which is what both the on-disk cache and the
        // browser's URL use: a wiki File: name carries spaces and punctuation that a file name
        // cannot ("Item Icon - Gem Crab.png" is stored as "Item_Icon_-_Gem_Crab.png").
        foreach (var fileName in WikiImageManifest.AllFiles)
        {
            var stored = Path.Combine(wikiDirectory, WikiImageCache.SafeNameFor(fileName) + ".png");
            Assert.True(
                File.Exists(stored),
                $"'{fileName}' is a verified wiki image but {stored} is not there. "
                + "Re-run the CLI's download-wiki-images and commit the result.");
        }
    }
}
