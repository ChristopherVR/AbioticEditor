using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class CatalogFreshnessTests
{
    [Fact]
    public void Fingerprint_tracks_containers_mods_mappings_registries_and_settings_without_reading_assets()
    {
        using var files = new CatalogFiles();
        var paks = files.Directory("Paks");
        var pak = files.Write("Paks/base.pak", "pak");
        var mod = files.Write("Paks/~mods/new.utoc", "toc");
        var mapping = files.Write("Mappings.usmap", "map");
        var registry = files.Write("registry.json", "{}");
        var settings = files.Write("mods-disabled.txt", "");
        string Capture(string context = "en|mods-enabled") => GameDataFingerprint.Capture(paks, [mapping, registry, settings], context);

        var original = Capture();
        Assert.Equal(original, Capture());
        files.Write("Paks/unrelated.txt", "ignored");
        Assert.Equal(original, Capture());
        File.AppendAllText(pak, "changed");
        Assert.NotEqual(original, Capture());
        var changed = Capture();
        files.Write("Paks/base.ucas", "container");
        Assert.NotEqual(changed, Capture());
        changed = Capture();
        File.Delete(mod);
        Assert.NotEqual(changed, Capture());
        changed = Capture();
        File.AppendAllText(mapping, "new mapping");
        Assert.NotEqual(changed, Capture());
        changed = Capture();
        File.AppendAllText(registry, "new registry");
        Assert.NotEqual(changed, Capture());
        changed = Capture();
        File.AppendAllText(settings, "mod-one");
        Assert.NotEqual(changed, Capture());
        Assert.NotEqual(Capture(), Capture("de|mods-disabled"));
    }

    [Fact]
    public void Invalid_override_falls_back_and_a_repaired_override_wins_without_restarting()
    {
        using var files = new CatalogFiles();
        var user = files.Path("user.json");
        var bundled = files.Path("bundled.json");
        new GameDataRegistry { SchemaVersion = GameDataRegistry.CurrentSchemaVersion + 1 }.Save(user);
        new GameDataRegistry { GameVersion = "bundled-version" }.Save(bundled);

        var fallback = GameDataRegistry.TryLoadFirst([user, bundled], out var path);
        Assert.Equal("bundled-version", fallback?.GameVersion);
        Assert.Equal(bundled, path);

        new GameDataRegistry { GameVersion = "repaired-user-version" }.Save(user);
        var repaired = GameDataRegistry.TryLoadFirst([user, bundled], out path);
        Assert.Equal("repaired-user-version", repaired?.GameVersion);
        Assert.Equal(user, path);

        File.WriteAllText(user, "invalid JSON");
        Assert.Equal("bundled-version", GameDataRegistry.TryLoadFirst([user, bundled], out _)?.GameVersion);
    }

    [Fact]
    public void Unchanged_healthy_inputs_reuse_definitions_but_record_the_latest_check()
    {
        var clock = new CatalogClock();
        var loads = 0;
        var service = new RecipeVocabularyService(clock, () => (1, null), (revision, _, at) =>
        {
            loads++;
            return Snapshot(revision, "one", at);
        }, () => { });
        using var items = new ItemCatalogService();
        var codex = new CodexVocabularyService();
        var first = service.EnsureCurrent(items, codex);
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = service.EnsureCurrent(items, codex);

        Assert.Equal(1, loads);
        Assert.Same(first.Recipes, second.Recipes);
        Assert.Same(first.Traders, second.Traders);
        Assert.Equal(first.Metadata.Revision, second.Metadata.Revision);
        Assert.True(second.Metadata.CheckedAt > first.Metadata.CheckedAt);
        Assert.Same(second.Codex, codex.Get());
        Assert.True(codex.TryGet(out _));
        Assert.True(service.TryGetRecipeInfos(out var recipes));
        Assert.Same(second.Recipes, recipes);
    }

    [Fact]
    public void Changed_revision_replaces_recipes_items_codex_variants_and_traders_as_one_snapshot()
    {
        var clock = new CatalogClock();
        long revision = 1;
        var service = new RecipeVocabularyService(clock, () => (revision, null),
            (current, _, at) => Snapshot(current, current == 1 ? "one" : "two", at), () => revision++);
        using var items = new ItemCatalogService();
        var codex = new CodexVocabularyService();
        service.EnsureCurrent(items, codex);
        Assert.NotNull(items.Find("output-one"));
        revision++;
        var next = service.EnsureCurrent(items, codex);

        Assert.Equal("recipe-two", Assert.Single(next.Recipes).Id);
        Assert.Null(items.Find("output-one"));
        Assert.Equal("Output two", items.Find("output-two")?.DisplayName);
        Assert.Equal("email-two", Assert.Single(codex.Get().Emails).Id);
        Assert.Equal("variant-two", Assert.Single(items.Variants.Entries).RowName);
        Assert.Equal("trader-two", Assert.Single(next.Traders).Id);
        Assert.Equal(3, next.Items.Count);
        Assert.Equal(2, items.Entries.Count);
        Assert.NotNull(items.Find("hidden-two"));
    }

    [Fact]
    public void Fallback_retries_after_one_minute_even_when_frequent_checks_update_the_check_time()
    {
        var clock = new CatalogClock();
        var loads = 0;
        long revision = 1;
        var service = new RecipeVocabularyService(clock, () => (revision, "installed-paks"), (current, _, at) =>
        {
            loads++;
            var snapshot = Snapshot(current, loads == 1 ? "fallback" : "installed", at);
            return snapshot with { Metadata = snapshot.Metadata with
            {
                Source = loads == 1 ? RecipeCatalogSource.BundledSnapshot : RecipeCatalogSource.InstalledGame,
                HasFallback = loads == 1,
            } };
        }, () => revision++);
        using var items = new ItemCatalogService();
        var codex = new CodexVocabularyService();
        var first = service.EnsureCurrent(items, codex);
        clock.Advance(TimeSpan.FromSeconds(30));
        service.EnsureCurrent(items, codex);
        clock.Advance(TimeSpan.FromSeconds(29));
        service.EnsureCurrent(items, codex);
        Assert.Equal(1, loads);
        clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = service.EnsureCurrent(items, codex);

        Assert.Equal(2, loads);
        Assert.True(recovered.Metadata.Revision > first.Metadata.Revision);
        Assert.False(recovered.Metadata.HasFallback);
        Assert.Equal(RecipeCatalogSource.InstalledGame, recovered.Metadata.Source);
        Assert.Equal("Output installed", items.Find("output-installed")?.DisplayName);
        Assert.Equal("variant-installed", Assert.Single(items.Variants.Entries).RowName);
        Assert.Equal("email-installed", Assert.Single(codex.Get().Emails).Id);
        Assert.Equal("trader-installed", Assert.Single(recovered.Traders).Id);
    }

    [Fact]
    public void Unavailable_catalog_recovery_advances_revision_and_updates_item_definitions_too()
    {
        var clock = new CatalogClock();
        var loads = 0;
        long revision = 1;
        var service = new RecipeVocabularyService(clock, () => (revision, null), (current, _, at) =>
        {
            loads++;
            return loads == 1
                ? new RecipeCatalogSnapshot([], [], CodexVocabulary.Empty,
                    new RecipeCatalogMetadata(current, RecipeCatalogSource.Unavailable, at, null, "en", false))
                : Snapshot(current, "recovered", at);
        }, () => revision++);
        using var items = new ItemCatalogService();
        var codex = new CodexVocabularyService();
        service.EnsureCurrent(items, codex);
        var recovered = service.EnsureCurrent(items, codex);

        Assert.Equal(2, loads);
        Assert.NotEmpty(recovered.Recipes);
        Assert.NotNull(items.Find("output-recovered"));
    }

    [Fact]
    public void Built_in_trader_fallback_is_not_presented_as_installed_acquisition_evidence()
    {
        Assert.Empty(RecipeVocabularyService.InstalledTraders(TraderCatalog.Fallback));
        TraderInfo[] installed = [new("installed-trader", null, [], [new("item", 1, null)], [])];
        Assert.Same(installed, RecipeVocabularyService.InstalledTraders(installed));
    }

    private static RecipeCatalogSnapshot Snapshot(long revision, string suffix, DateTimeOffset checkedAt)
        => new([new RecipeInfo("recipe-" + suffix, "output-" + suffix, 1, null, "Crafting", [new("ingredient-" + suffix, 1)])],
            [Item("output-" + suffix, "Output " + suffix), Item("ingredient-" + suffix, "Ingredient " + suffix), Item("hidden-" + suffix, "DEPRECATED")],
            new CodexVocabulary([new("email-" + suffix, "Subject", [], [], [])], [], [], []),
            new RecipeCatalogMetadata(revision, RecipeCatalogSource.BundledSnapshot, checkedAt, "snapshot-version", "en", false))
            {
                ItemVariants = [new("variant-" + suffix, "Variant", null, null)],
                Traders = [new("trader-" + suffix, null, [], [new("ingredient-" + suffix, 1, null)], [])],
            };

    private static ItemCatalogEntry Item(string id, string name) => new(id, name, null, null, 1, 0, false, 0, []);

    private sealed class CatalogClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }

    private sealed class CatalogFiles : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-catalog-test", Guid.NewGuid().ToString("N"));
        public CatalogFiles() => System.IO.Directory.CreateDirectory(_root);
        public string Path(string relative) => System.IO.Path.Combine(_root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public string Directory(string relative)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
        public string Write(string relative, string content)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() => System.IO.Directory.Delete(_root, recursive: true);
    }
}
