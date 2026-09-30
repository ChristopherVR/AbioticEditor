using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Services;

/// <summary>Loads the game's buff table once, on demand and off the render path (never when a save is selected).</summary>
public sealed class BuffVocabularyService
{
    private Lazy<BuffCatalog> _catalog = new(Load);
    public BuffCatalog Get() => _catalog.Value;
    public bool TryGet(out BuffCatalog catalog)
    {
        var lazy = _catalog;
        if (!lazy.IsValueCreated) { catalog = BuffCatalog.Empty; return false; }
        catalog = lazy.Value;
        return true;
    }

    private static BuffCatalog Load()
    {
        try
        {
            // The gate owns the shared provider; it must not be disposed here.
            var provider = GameDataGate.CreateProvider();
            return provider is { HasMappings: true } ? BuffCatalog.LoadFrom(provider) : BuffCatalog.Empty;
        }
        catch (Exception)
        {
            return BuffCatalog.Empty;
        }
    }
}
