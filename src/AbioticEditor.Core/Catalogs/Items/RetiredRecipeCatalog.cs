namespace AbioticEditor.Core.Items;

/// <summary>
/// Recipe ids the base game wrote into saves in earlier versions but no longer defines. The game
/// never prunes a learned-recipe list, so long-running unmodded worlds keep them forever; they are
/// harmless and must not be mistaken for mod content.
/// </summary>
public static class RetiredRecipeCatalog
{
    // Present in game-written saves of unmodded worlds, absent from every recipe table of game
    // 1.5.0.31459 and named by no other game asset. Recipes whose output item still exists (the
    // drills, the construction gauntlet, ...) need no entry: IsRetired recognises them by shape.
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "recipe_calz_peccary",
        "recipe_carbstick",
        "recipe_pistol_flashlight", // the item is now pistol_security_flashlight
        "recipe_sundae",
    };

    /// <summary>The listed retired ids.</summary>
    public static IReadOnlyCollection<string> KnownIds => Known;

    /// <summary>
    /// True when <paramref name="recipeId"/> is a base-game recipe the game has since dropped: a
    /// listed retired id, or <c>recipe_&lt;item&gt;</c> for an item the game still has (such
    /// recipes were replaced by item upgrades or loot, and the game's own unlock links can still
    /// name them).
    /// </summary>
    public static bool IsRetired(string recipeId, IReadOnlySet<string>? itemIds)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return false;
        if (Known.Contains(recipeId)) return true;
        return itemIds is not null
            && recipeId.StartsWith("recipe_", StringComparison.OrdinalIgnoreCase)
            && itemIds.Contains(recipeId["recipe_".Length..]);
    }
}
