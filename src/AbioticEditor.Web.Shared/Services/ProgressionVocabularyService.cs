using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Services;

/// <summary>Cached game-data vocabularies used by the player discovery actions.</summary>
public sealed class ProgressionVocabularyService
{
    private volatile Vocabulary? _vocabulary;
    public IReadOnlyList<string> GetItems() => Value.ItemEntries.Select(entry => entry.Id).ToArray();
    public IReadOnlyList<ItemCatalogEntry> GetItemEntries() => Value.ItemEntries;
    public IReadOnlyList<string> GetMaps() => Value.Maps;
    /// <summary>Full trait details (description, point cost) from CDT_AllTraits; empty without game data.</summary>
    public IReadOnlyDictionary<string, TraitDetail> GetTraitDetails() => Value.TraitDetails;
    public bool TryGetItemEntries(out IReadOnlyList<ItemCatalogEntry> entries)
    {
        if (_vocabulary is not { } loaded) { entries = Array.Empty<ItemCatalogEntry>(); return false; }
        entries = loaded.ItemEntries;
        return true;
    }
    public bool TryGet(out IReadOnlyList<string> items, out IReadOnlyList<string> maps)
    {
        if (_vocabulary is not { } loaded)
        {
            items = Array.Empty<string>();
            maps = Array.Empty<string>();
            return false;
        }
        items = loaded.ItemEntries.Select(entry => entry.Id).ToArray();
        maps = loaded.Maps;
        return true;
    }
    public void Reload() => _vocabulary = null;

    /// <summary>Loaded once under the shared pak gate; a failed (empty) load is retried on
    /// the next request instead of being cached for the whole session.</summary>
    private Vocabulary Value
    {
        get
        {
            if (_vocabulary is { } cached && !ReferenceEquals(cached, Empty)) return cached;
            lock (GameDataGate.Sync)
            {
                if (_vocabulary is { } raced && !ReferenceEquals(raced, Empty)) return raced;
                return (_vocabulary = Load())!;
            }
        }
    }

    private sealed record Vocabulary(
        IReadOnlyList<ItemCatalogEntry> ItemEntries,
        IReadOnlyList<string> Maps,
        IReadOnlyDictionary<string, TraitDetail> TraitDetails);

    private static readonly Vocabulary Empty = new(
        Array.Empty<ItemCatalogEntry>(), Array.Empty<string>(), new Dictionary<string, TraitDetail>(StringComparer.Ordinal));

    private static Vocabulary Load()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
            if (provider is { HasMappings: true })
            {
                // Skill milestones come straight from the game's own DT_Skills/DT_SkillPerks
                // tables when available (native GameDataServices does the same); the static
                // wiki fallback remains for offline use.
                SkillMilestoneCatalog.ApplyGameData(SkillMilestoneCatalog.LoadFrom(provider));
                return new Vocabulary(
                    ItemCatalog.LoadFrom(provider).Entries.ToArray(),
                    MapCatalog.LoadFrom(provider),
                    TraitCatalog.LoadDetailsFrom(provider));
            }
        }
        catch { /* fall through to the bundled dump */ }

        return FromRegistry();
    }

    /// <summary>
    /// The same vocabulary read from the game-data dump the editor ships with, for hosts that
    /// have no game install to mount - the browser build, and a desktop copy on a machine that
    /// does not own the game. Everything here is text, so it is complete apart from artwork.
    /// </summary>
    private static Vocabulary FromRegistry()
    {
        if (GameDataRegistry.LoadBundled() is not { } registry) return Empty;
        SkillMilestoneCatalog.ApplyGameData(registry.SkillMilestones);
        return new Vocabulary(
            registry.Items ?? Array.Empty<ItemCatalogEntry>(),
            registry.Maps ?? Array.Empty<string>(),
            registry.Traits ?? new Dictionary<string, TraitDetail>(StringComparer.Ordinal));
    }
}
