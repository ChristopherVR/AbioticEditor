using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Services;

/// <summary>Loads the optional game-data item-upgrade graph once for staged slot editing.</summary>
public sealed class ItemUpgradeVocabularyService
{
    private Lazy<ItemUpgradeCatalog> _catalog = new(Load);
    public ItemUpgradeCatalog Get() => _catalog.Value;
    public bool TryGet(out ItemUpgradeCatalog catalog)
    {
        var lazy = _catalog;
        if (!lazy.IsValueCreated) { catalog = ItemUpgradeCatalog.Empty; return false; }
        catalog = lazy.Value;
        return true;
    }
    public void Reload() => Interlocked.Exchange(ref _catalog, new Lazy<ItemUpgradeCatalog>(Load));
    private static ItemUpgradeCatalog Load()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
            if (provider is { HasMappings: true })
            {
                var live = ItemUpgradeCatalog.LoadFrom(provider);
                if (live.Count > 0) return live;
            }
        }
        catch { /* fall through to the bundled dump */ }

        return GameDataRegistry.LoadBundled()?.ItemUpgrades is { Count: > 0 } upgrades
            ? ItemUpgradeCatalog.FromRegistry(upgrades)
            : ItemUpgradeCatalog.Empty;
    }
}
