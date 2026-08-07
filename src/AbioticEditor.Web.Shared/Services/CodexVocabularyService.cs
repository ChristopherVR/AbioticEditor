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

    private static CodexVocabulary Load()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
            if (provider is { HasMappings: true })
            {
                var live = new CodexVocabulary(
                    CodexCatalog.LoadEmails(provider),
                    CodexCatalog.LoadJournals(provider),
                    CodexCatalog.LoadCompendium(provider),
                    CodexCatalog.LoadFish(provider));
                if (live.Emails.Count > 0 || live.Compendium.Count > 0) return live;
            }
        }
        catch { /* fall through to the bundled dump */ }

        if (GameDataRegistry.LoadBundled() is not { } registry) return CodexVocabulary.Empty;
        return new CodexVocabulary(
            registry.Emails ?? Array.Empty<EmailEntry>(),
            registry.Journals ?? Array.Empty<JournalEntry>(),
            registry.Compendium ?? Array.Empty<CompendiumEntry>(),
            registry.Fish ?? Array.Empty<FishDefinition>());
    }
}
