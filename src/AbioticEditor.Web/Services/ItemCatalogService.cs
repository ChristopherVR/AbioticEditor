using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Supplies the Razor inventory palette from the same generated game-data registry and
/// live texture extractor used by the retired native slot sidebar. Registry metadata is
/// available offline; icon extraction is attempted only when the local game data exists.
/// </summary>
public sealed class ItemCatalogService : IDisposable
{
    private readonly IReadOnlyList<ItemCatalogEntry> _entries;
    private readonly Dictionary<string, ItemCatalogEntry> _byId;
    private readonly Lazy<GameAssetProvider?> _provider = new(CreateProvider, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ConcurrentDictionary<string, Task<string?>> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Caps how many icons are decoded at once. Opening a catalog category asks for ~72 icons
    /// in the same instant, and on the first run of an install none of them are on disk yet.
    /// The provider decodes textures under its own lock, so those requests were serialized
    /// anyway - but each one still parked a thread-pool thread waiting its turn, which starved
    /// the render loop and made the whole editor feel frozen while a category loaded. Letting a
    /// couple through at a time costs nothing in throughput and leaves the UI responsive.
    /// </summary>
    private static readonly SemaphoreSlim IconDecodeGate = new(2, 2);

    public ItemCatalogService(ProgressionVocabularyService? liveVocabulary = null)
    {
        var registry = GameDataRegistry.LoadBundled();
        var merged = (registry?.Items ?? []).ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        // The slot editor is always present in the desktop shell. Do not make resolving it
        // mount and scan the installed game paks before a save can open. Bundled registry
        // data is immediately usable; merge live data only when another explicit workflow
        // has already loaded it.
        if (liveVocabulary is not null && liveVocabulary.TryGetItemEntries(out var liveEntries))
        {
            foreach (var entry in liveEntries) merged[entry.Id] = entry;
        }
        _entries = merged.Values
            .Where(IsBrowsable)
            .OrderBy(entry => string.Equals(entry.DisplayName, entry.Id, StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _byId = _entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ItemCatalogEntry> Entries => _entries;
    public ItemCatalogEntry? Find(string? itemId) => itemId is not null && _byId.TryGetValue(itemId, out var entry) ? entry : null;
    public string IconUrl(string itemId) => $"/item-icons/{Uri.EscapeDataString(itemId)}";

    public Task<string?> GetIconPathAsync(string itemId)
        => _icons.GetOrAdd(itemId, static (id, service) => service.ExtractIconAsync(id), this);

    private async Task<string?> ExtractIconAsync(string itemId)
    {
        if (Find(itemId) is not { IconAssetPath: { Length: > 0 } } entry) return null;
        await IconDecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var provider = _provider.Value;
                    if (provider is not { HasMappings: true }) return null;
                    var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
                    return raw is null ? null : IconColorizer.Colorize(raw, entry);
                }
                catch { return null; }
            }).ConfigureAwait(false);
        }
        finally
        {
            IconDecodeGate.Release();
        }
    }

    public static string CategoryOf(ItemCatalogEntry entry)
    {
        bool Tag(string prefix) => entry.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        bool IdHas(params string[] hints) => hints.Any(hint => entry.Id.Contains(hint, StringComparison.OrdinalIgnoreCase));

        if (Tag("Item.Ammo") || Tag("Item.Weapon") || IdHas("weapon_", "ammo_", "grenade", "frag", "_gun", "magnum", "shotgun", "rifle", "crossbow", "launcher")) return "weapons";
        if (Tag("Item.Gear") || IdHas("armor", "helmet", "backpack_", "trinket", "suit_", "goggles", "headlamp", "watch_", "shield")) return "armor";
        if (IdHas("bandage", "medkit", "syringe", "splint", "pills", "firstaid", "antidote", "vaccine")) return "medical";
        if (Tag("Item.Food") || IdHas("food_", "soup_", "drink", "coffee", "tea_", "snack", "fish_")) return "food";
        if (IdHas("seed", "fertilizer", "gardenplot", "wateringcan", "scarecrow", "plant")) return "farming";
        if (IdHas("trap_", "_trap", "turret", "barricade", "tripwire", "mine_", "noisemaker", "spikes")) return "defense";
        if (IdHas("battery", "powercell", "brick_power", "lamp", "light_", "flashlight", "glowstick", "generator", "solar", "cable")) return "power";
        if (IdHas("bench", "furniture", "chair", "table", "bed_", "shelf", "crate", "couch", "locker", "freezer", "fridge", "stove", "oven", "sink_", "toilet")) return "furniture";
        if (IdHas("tool", "screwdriver", "wrench", "hammer", "drill", "vacuum", "fishingrod", "keypadhacker", "scanner", "extinguisher")) return "tools";
        if (IdHas("scrap_", "gib_", "essence", "crystal", "alloy", "ore_", "ingot", "plastic", "cloth", "tech_", "circuitboard", "harddrive", "casefan", "powersupply", "glue", "tape", "paper", "rubberband", "spring", "gear_", "coil", "wire", "lens", "diode", "carbon", "gem", "silver", "gold")) return "resources";
        return "other";
    }

    private static bool IsBrowsable(ItemCatalogEntry entry)
        => !string.IsNullOrWhiteSpace(entry.DisplayName)
           && entry.DisplayName != "?"
           && !entry.DisplayName.Contains("DEPRECATED", StringComparison.OrdinalIgnoreCase)
           && !entry.DisplayName.Contains("DONOTUSE", StringComparison.OrdinalIgnoreCase);

    private static GameAssetProvider? CreateProvider()
    {
        try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
        catch { return null; }
    }

    public void Dispose() { if (_provider.IsValueCreated) _provider.Value?.Dispose(); }
}
