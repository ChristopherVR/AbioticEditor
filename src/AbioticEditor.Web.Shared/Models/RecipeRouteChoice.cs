namespace AbioticEditor.Web.Models;

/// <summary>A selected alternative at one occurrence in a crafting guide.</summary>
public sealed record RecipeRouteChoice(string Path, string RecipeId);

/// <summary>The exact producing route, or a material without a recipe, selected for inspection.</summary>
public sealed record RecipeCraftingSelection(string ItemId, string? RecipeId);
