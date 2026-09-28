using AbioticEditor.Core.Items;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>What an entitlement token is, as far as the editor can tell.</summary>
public enum EntitlementTokenKind
{
    /// <summary>A store/ownership grant (<c>EarlyAccess</c>, <c>SupportersEdition</c>).</summary>
    Ownership,

    /// <summary>A recipe row id (matched in the recipe catalog, or shaped like one).</summary>
    Recipe,

    /// <summary>Anything else; shown raw and left untouched.</summary>
    Unknown,
}

/// <summary>One entitlement token held by one player.</summary>
/// <param name="Token">The token exactly as stored.</param>
/// <param name="Kind">How the editor classified it.</param>
/// <param name="Label">A friendly name (ownership label, or the crafted item's display name) or null when it cannot be resolved.</param>
/// <param name="ResolvedInCatalog">True when a recipe token matched a row of the recipe catalog.</param>
/// <param name="InWorldRecipeUnlocks">For recipe tokens: whether the same id is in the world's <c>GlobalRecipesUnlocked</c>. Null for other kinds.</param>
public sealed record EntitlementToken(
    string Token,
    EntitlementTokenKind Kind,
    string? Label,
    bool ResolvedInCatalog,
    bool? InWorldRecipeUnlocks);

/// <summary>All tokens held by one player in one entitlement map.</summary>
/// <param name="Source">The metadata map name (<c>ServerEntitlements</c> or <c>UserEntitlements</c>).</param>
/// <param name="SteamId">The map key (a SteamID64).</param>
/// <param name="PersonaName">The Steam persona name when known.</param>
/// <param name="Tokens">The player's tokens, in saved order.</param>
public sealed record PlayerEntitlements(
    string Source,
    string SteamId,
    string? PersonaName,
    IReadOnlyList<EntitlementToken> Tokens)
{
    /// <summary>The recipe-kind tokens.</summary>
    public IEnumerable<EntitlementToken> RecipeTokens => Tokens.Where(t => t.Kind == EntitlementTokenKind.Recipe);
}

/// <summary>
/// Read-only per-player view of the metadata save's entitlement maps, with recipe-shaped tokens
/// resolved against the recipe catalog when one is available.
/// </summary>
/// <remarks>
/// Fixture finding (see docs/research/world-and-placed-object-state.md): in every fixture the only
/// per-player map is <c>ServerEntitlements</c>, and it holds only ownership tokens. <c>UserEntitlements</c>
/// appears solely as the struct TYPE of those values; no fixture carries a per-player recipe-token
/// map. This report handles a real <c>UserEntitlements</c> map and recipe tokens anyway, so a save
/// from a build that writes them is shown rather than ignored, but nothing here writes.
/// Without game data (empty catalog) recipe-shaped tokens are still classified by their prefix and
/// shown raw.
/// </remarks>
public static class PlayerEntitlementReport
{
    /// <summary>The metadata map names that carry per-player token lists.</summary>
    public static readonly IReadOnlyList<string> MapNames = ["ServerEntitlements", "UserEntitlements"];

    private static readonly string[] RecipePrefixes = ["recipe_", "srecipe_", "frecipe_", "trecipe_", "crecipe_"];

    /// <summary>True when <paramref name="token"/> is spelled like a recipe row id.</summary>
    public static bool LooksLikeRecipe(string token)
        => RecipePrefixes.Any(p => token.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classifies one token.
    /// </summary>
    /// <param name="token">The stored token.</param>
    /// <param name="recipesById">Recipe catalog rows by id (case-insensitive), or null when unavailable.</param>
    /// <param name="itemName">Resolves a crafted item id to a display name, or null.</param>
    /// <param name="worldRecipes">The world's unlocked recipe ids, or null when not known.</param>
    public static EntitlementToken Classify(
        string token,
        IReadOnlyDictionary<string, RecipeInfo>? recipesById,
        Func<string, string?>? itemName,
        IReadOnlySet<string>? worldRecipes)
    {
        ArgumentNullException.ThrowIfNull(token);

        var known = Features.ServerEntitlementsFeature.KnownLabel(token);
        if (known is not null)
        {
            return new EntitlementToken(token, EntitlementTokenKind.Ownership, known, false, null);
        }

        RecipeInfo? info = null;
        var inCatalog = recipesById is not null && recipesById.TryGetValue(token, out info);
        if (inCatalog || LooksLikeRecipe(token))
        {
            string? label = null;
            if (info?.CreatesItemId is { } created)
            {
                label = itemName?.Invoke(created) ?? created;
            }
            return new EntitlementToken(
                token, EntitlementTokenKind.Recipe, label, inCatalog,
                worldRecipes?.Contains(token));
        }

        return new EntitlementToken(token, EntitlementTokenKind.Unknown, null, false, null);
    }

    /// <summary>
    /// Builds the report for a metadata save: one row per player per map, in map then saved order.
    /// Empty when the save has no entitlement map (region saves, or a fresh world).
    /// </summary>
    /// <param name="metadata">A metadata save (<c>WorldSave_MetaData.sav</c>).</param>
    /// <param name="recipes">The recipe catalog, or null/empty when game data is absent.</param>
    /// <param name="itemName">Optional item display-name resolver.</param>
    /// <param name="personaFor">Optional SteamID64 to Steam persona name lookup.</param>
    public static IReadOnlyList<PlayerEntitlements> Build(
        WorldSaveData metadata,
        IReadOnlyList<RecipeInfo>? recipes = null,
        Func<string, string?>? itemName = null,
        Func<string, string?>? personaFor = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var byId = recipes is { Count: > 0 }
            ? recipes.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : null;
        var world = new HashSet<string>(metadata.GlobalRecipes, StringComparer.OrdinalIgnoreCase);

        var result = new List<PlayerEntitlements>();
        foreach (var mapName in MapNames)
        {
            var pairs = WorldSaveReader.GetMapPairs(metadata.Raw.Properties, mapName);
            if (pairs is null) continue;

            foreach (var kv in pairs)
            {
                var steamId = WorldSaveReader.ExtractMapKeyString(kv.Key);
                if (steamId is null) continue;

                var tokens = ReadTokens(kv.Value)
                    .Select(t => Classify(t, byId, itemName, world))
                    .ToList();
                result.Add(new PlayerEntitlements(mapName, steamId, personaFor?.Invoke(steamId), tokens));
            }
        }
        return result;
    }

    /// <summary>Every string of every string-array member of an entry struct (the members are named <c>Entitlements</c> in the fixtures).</summary>
    private static IEnumerable<string> ReadTokens(FProperty value)
    {
        if (value is not StructProperty { Value: PropertiesStruct ps }) yield break;
        foreach (var tag in ps.Properties)
        {
            if (tag.Property is not ArrayProperty { Value: { } arr }) continue;
            for (var i = 0; i < arr.Length; i++)
            {
                var s = arr.GetValue(i) switch
                {
                    UeSaveGame.FString fs => fs.Value,
                    string raw => raw,
                    var v => v?.ToString(),
                };
                if (!string.IsNullOrEmpty(s)) yield return s;
            }
        }
    }
}
