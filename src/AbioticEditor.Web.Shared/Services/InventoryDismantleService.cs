using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>Builds and applies safe, previewable inventory dismantle plans from game data.</summary>
public sealed class InventoryDismantleService
{
    private readonly Func<DismantleVocabulary> _loader;
    private readonly object _sync = new();
    private DismantleVocabulary? _vocabulary;

    public InventoryDismantleService() : this(Load) { }
    public InventoryDismantleService(IReadOnlyList<RecipeInfo> recipes, ItemCatalog catalog)
        : this(() => new(recipes, catalog)) { }
    private InventoryDismantleService(Func<DismantleVocabulary> loader) => _loader = loader;

    private DismantleVocabulary Vocabulary
    {
        get
        {
            lock (_sync) { return _vocabulary ??= _loader(); }
        }
    }

    /// <summary>
    /// Drops the cached recipes so the next dismantle re-reads the game files. Without this,
    /// someone who started the editor before pointing it at their game install kept being told
    /// every single item had no recipe, even after fixing the path in Settings - the empty
    /// vocabulary from the first attempt was cached for the rest of the session.
    /// </summary>
    public void Reload()
    {
        lock (_sync) { _vocabulary = null; }
    }

    public bool TryPlan(PlayerInventorySlotEdit source, IReadOnlyList<PlayerInventorySlotEdit> destinations,
        bool reuseSource, out InventoryDismantlePlan? plan, out string message)
    {
        plan = null;
        if (source.IsEmpty || string.IsNullOrWhiteSpace(source.ItemId)) { message = "Select an occupied slot first."; return false; }
        var vocabulary = Vocabulary;
        // With no recipes at all the answer below would be "no recipe for this item" for every
        // item in the game, which is what it looked like to anyone whose install had not been
        // found. Name the real reason instead.
        if (vocabulary.Recipes.Count == 0)
        {
            message = "Dismantling needs the game's own recipe list, which the editor could not read. "
                + "Point it at your Abiotic Factor install in Settings, then try again.";
            return false;
        }
        var recipe = vocabulary.Recipes.FirstOrDefault(candidate => string.Equals(candidate.CreatesItemId, source.ItemId, StringComparison.OrdinalIgnoreCase) && candidate.IngredientList.Count > 0);
        if (recipe is null) { message = "No crafting recipe with a dismantle yield is available for this item."; return false; }
        var available = destinations.Where(edit => edit.IsEmpty && !ReferenceEquals(edit, source)).ToList();
        var targets = reuseSource ? new[] { source }.Concat(available).ToList() : available;
        if (targets.Count < recipe.IngredientList.Count) { message = $"Dismantling needs {recipe.IngredientList.Count} destination slots, but only {targets.Count} are available."; return false; }
        plan = new(source, recipe.IngredientList.Select((ingredient, index) => new DismantlePlacement(targets[index], ingredient, vocabulary.Items.Find(ingredient.ItemId))).ToArray(), reuseSource);
        message = string.Join(", ", plan.Placements.Select(placement => $"{placement.Ingredient.Count}x {placement.Entry?.DisplayName ?? placement.Ingredient.ItemId}"));
        return true;
    }

    public static void Apply(InventoryDismantlePlan plan)
    {
        foreach (var placement in plan.Placements)
        {
            var maxDurability = placement.Entry?.MaxDurability ?? 0;
            placement.Target.LoadFrom(new InventoryItemSlot(placement.Target.Index, placement.Ingredient.ItemId,
                placement.Ingredient.Count, maxDurability, maxDurability, 0, 0, null, false, null,
                Guid.NewGuid().ToString("N").ToUpperInvariant()));
        }
        if (!plan.ReusesSource)
            plan.Source.LoadFrom(new InventoryItemSlot(plan.Source.Index, PlayerSaveWriter.EmptySlotRowName,
                0, 0, 0, 0, 0, null, false, null, null));
    }

    private static DismantleVocabulary Load()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
            if (provider is not { HasMappings: true }) return DismantleVocabulary.Empty;
            return new(RecipeCatalog.LoadInfosFrom(provider), ItemCatalog.LoadFrom(provider));
        }
        catch (Exception exception)
        {
            // Swallowing this silently made a broken pak read indistinguishable from an item
            // that genuinely has no recipe. The feature still degrades to "unavailable", but
            // the reason is now on disk.
            EditorLog.Error("Dismantle", "Could not read the game's recipe list", exception);
            return DismantleVocabulary.Empty;
        }
    }

    private sealed record DismantleVocabulary(IReadOnlyList<RecipeInfo> Recipes, ItemCatalog Items)
    {
        public static DismantleVocabulary Empty { get; } = new([], ItemCatalog.FromRegistry([], new Dictionary<string, string>()));
    }
}

public sealed record InventoryDismantlePlan(PlayerInventorySlotEdit Source, IReadOnlyList<DismantlePlacement> Placements, bool ReusesSource);
public sealed record DismantlePlacement(PlayerInventorySlotEdit Target, RecipeIngredient Ingredient, ItemCatalogEntry? Entry);
