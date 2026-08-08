using System;
using System.IO;
using AbioticEditor.Core.Assets;

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
