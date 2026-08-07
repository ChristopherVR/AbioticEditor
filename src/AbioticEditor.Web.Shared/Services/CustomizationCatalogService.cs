using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Supplies the appearance editor's per-field option lists (head/hair/color rows, each with
/// its display name, optional swatch color, and optional preview texture) from the same
/// <c>DT_Customization_*</c> tables the retired native app read. Mounts the game install lazily
/// and only once; when it can't be mounted, every field just falls back to its raw save value.
/// </summary>
public sealed class CustomizationCatalogService : IDisposable
{
    private readonly Lazy<GameAssetProvider?> _provider = new(CreateProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>>> _options;

    public CustomizationCatalogService()
    {
        _options = new(LoadOptions, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>All known rows of a <c>DT_Customization_*</c> table, or empty when unavailable.</summary>
    public IReadOnlyList<CustomizationOption> OptionsFor(string tableName)
        => _options.Value.TryGetValue(tableName, out var options) ? options : Array.Empty<CustomizationOption>();

    private IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>> LoadOptions()
    {
        try
        {
            var provider = _provider.Value;
            if (provider is { HasMappings: true })
            {
                var live = CustomizationCatalog.LoadFrom(provider);
                if (live.Count > 0) return live;
            }
        }
        catch { /* fall through to the bundled dump */ }

        return GameDataRegistry.LoadBundled()?.Customization
            ?? new Dictionary<string, IReadOnlyList<CustomizationOption>>();
    }

    private static GameAssetProvider? CreateProvider()
    {
        try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
        catch { return null; }
    }

    public void Dispose() { if (_provider.IsValueCreated) _provider.Value?.Dispose(); }
}
