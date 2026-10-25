using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>Caches optional narrative tables from the local game install for the Razor codex.</summary>
public sealed class CodexVocabularyService
{
    private Lazy<CodexVocabulary> _vocabulary = new(Load);
    public CodexVocabulary Get() => _vocabulary.Value;
    public bool TryGet(out CodexVocabulary vocabulary)
    {
        var lazy = _vocabulary;
        if (!lazy.IsValueCreated) { vocabulary = CodexVocabulary.Empty; return false; }
        vocabulary = lazy.Value;
        return true;
    }
    public void Reload() => Interlocked.Exchange(ref _vocabulary, new Lazy<CodexVocabulary>(Load));

    internal void ApplyRecipeSnapshot(RecipeCatalogSnapshot snapshot)
    {
        var loaded = new Lazy<CodexVocabulary>(() => snapshot.Codex);
        _ = loaded.Value;
        Interlocked.Exchange(ref _vocabulary, loaded);
    }

    internal static CodexVocabulary LoadFromProvider(GameAssetProvider provider)
        => new(CodexCatalog.LoadEmails(provider), CodexCatalog.LoadJournals(provider),
            CodexCatalog.LoadCompendium(provider), CodexCatalog.LoadFish(provider));

    internal static CodexVocabulary FromRegistry(GameDataRegistry? registry)
        => registry is null ? CodexVocabulary.Empty : new CodexVocabulary(
            registry.Emails ?? [], registry.Journals ?? [], registry.Compendium ?? [], registry.Fish ?? []);

    private static CodexVocabulary Load()
    {
        try
        {
            var provider = GameDataGate.CreateProvider();
            if (provider is { HasMappings: true })
            {
                var live = LoadFromProvider(provider);
                if (live.Emails.Count > 0 || live.Compendium.Count > 0) return live;
            }
        }
        catch { /* fall through to the bundled dump */ }

        return FromRegistry(GameDataRegistry.LoadBundled());
    }
}
