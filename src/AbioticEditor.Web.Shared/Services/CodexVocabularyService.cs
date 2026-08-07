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
            return provider is { HasMappings: true }
                ? new(CodexCatalog.LoadEmails(provider), CodexCatalog.LoadJournals(provider), CodexCatalog.LoadCompendium(provider), CodexCatalog.LoadFish(provider))
                : CodexVocabulary.Empty;
        }
        catch { return CodexVocabulary.Empty; }
    }
}
