namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-recipes editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>PlayerRecipesTab.razor</c> uses, extracted from
/// <see cref="PlayerSaveSession"/>'s existing recipes slice, so that widget binds to either the
/// file-backed session or <c>LivePlayerRecipesSession</c> with no changes beyond its parameter's
/// declared type.
/// </summary>
public interface IPlayerRecipesSession
{
    IReadOnlyList<PlayerRecipeEdit> Recipes { get; }
    int UnlockedRecipeCount { get; }
    int RecipeCount { get; }

    /// <summary>False for the file session (edits stage until Save); true live (an unlock RPC
    /// fires immediately in the running game, the same as every other immediate-apply live area).</summary>
    bool AppliesImmediately { get; }

    /// <summary>False live: the running game has no function to re-lock a recipe once unlocked
    /// (see <c>LivePlayerRecipesChannel</c>'s remarks) - the tab disables un-checking an already
    /// unlocked row when this is false instead of silently no-opping the click.</summary>
    bool CanLock { get; }

    void EnsureRecipeRows(IEnumerable<string> ids);

    /// <summary>Unlocks (or, when <see cref="CanLock"/>, re-locks) one recipe.</summary>
    Task SetUnlockedAsync(string recipeId, bool unlocked);

    void MarkChanged();
}
