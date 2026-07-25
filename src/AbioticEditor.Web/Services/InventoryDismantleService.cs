using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>Builds and applies safe, previewable inventory dismantle plans from game data.</summary>
public sealed class InventoryDismantleService
{
    private readonly Lazy<DismantleVocabulary> _vocabulary;

    public InventoryDismantleService() : this(new Lazy<DismantleVocabulary>(Load)) { }
    public InventoryDismantleService(IReadOnlyList<RecipeInfo> recipes, ItemCatalog catalog)
        : this(new Lazy<DismantleVocabulary>(() => new(recipes, catalog))) { }
    private InventoryDismantleService(Lazy<DismantleVocabulary> vocabulary) => _vocabulary = vocabulary;

    public bool TryPlan(PlayerInventorySlotEdit source, IReadOnlyList<PlayerInventorySlotEdit> destinations,
        bool reuseSource, out InventoryDismantlePlan? plan, out string message)
    {
        plan = null;
        if (source.IsEmpty || string.IsNullOrWhiteSpace(source.ItemId)) { message = "Select an occupied slot first."; return false; }
        var vocabulary = _vocabulary.Value;
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
        catch { return DismantleVocabulary.Empty; }
    }

    private sealed record DismantleVocabulary(IReadOnlyList<RecipeInfo> Recipes, ItemCatalog Items)
    {
        public static DismantleVocabulary Empty { get; } = new([], ItemCatalog.FromRegistry([], new Dictionary<string, string>()));
    }
}

public sealed record InventoryDismantlePlan(PlayerInventorySlotEdit Source, IReadOnlyList<DismantlePlacement> Placements, bool ReusesSource);
public sealed record DismantlePlacement(PlayerInventorySlotEdit Target, RecipeIngredient Ingredient, ItemCatalogEntry? Entry);
