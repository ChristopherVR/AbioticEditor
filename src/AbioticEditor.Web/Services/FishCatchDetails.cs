using System.Text;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Web port of the native FishBaitResolver: resolves, for each fish, the bait it unlocks
/// and the bait it needs to be caught, plus the plain-language catch-requirement lines
/// (location, story flags, bait, time of day) shown in the GATEPal fish reading pane.
/// </summary>
public sealed partial class FishCatchDetails
{
    private readonly HostLanguageService _language;
    private readonly ItemCatalogService _items;
    private readonly IReadOnlyList<RecipeInfo> _recipes;
    private readonly Dictionary<string, ItemCatalogEntry> _baitByTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemCatalogEntry> _familyBait = new(StringComparer.OrdinalIgnoreCase);

    public FishCatchDetails(
        HostLanguageService language, ItemCatalogService items,
        IReadOnlyList<FishDefinition> fish, IReadOnlyList<RecipeInfo> recipes)
    {
        _language = language;
        _items = items;
        _recipes = recipes;

        // Map every bait gameplay tag (Fishing.Bait.X) to its item.
        foreach (var entry in items.Entries)
        {
            foreach (var tag in entry.Tags)
            {
                if (tag.StartsWith("Fishing.Bait", StringComparison.OrdinalIgnoreCase)) _baitByTag[tag] = entry;
            }
        }

        // One bait per fish family (base name shared by the common fish + its rare variants):
        // prefer the recipe a member unlocks, else the bait a member requires.
        foreach (var group in fish.GroupBy(f => BaseKey(f.Id), StringComparer.OrdinalIgnoreCase))
        {
            var bait = group.Select(f => BaitFromRecipe(f.UnlockRecipeId)).FirstOrDefault(b => b is not null)
                    ?? group.Select(f => BaitFromTag(f.RequiredBaitTag)).FirstOrDefault(b => b is not null);
            if (bait is not null) _familyBait[group.Key] = bait;
        }
    }

    public FishCatchDetail Detail(FishDefinition fish)
    {
        var unlock = BaitFromRecipe(fish.UnlockRecipeId)
                     ?? (_familyBait.TryGetValue(BaseKey(fish.Id), out var family) ? family : null);
        var required = BaitFromTag(fish.RequiredBaitTag);

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(fish.Location))
            lines.Add(_language.Resource("PlayerCodex_FishCastWhere", fish.Location.ToLowerInvariant()));
        if (fish.RequiredWorldFlag is { } flag)
            lines.Add(_language.Resource("PlayerCodex_FishStoryProgress", Humanize(flag)));
        if (required is not null)
            lines.Add(_language.Resource("PlayerCodex_FishBaitUp", required.DisplayName));
        else if (fish.RequiresSpecialCatch)
            lines.Add(_language.Resource("PlayerCodex_FishNeedsSpecificBait"));
        if (TimeOfDayText(fish) is { } timeOfDay) lines.Add(timeOfDay);
        if (fish.RequiredDlcId is { } dlc)
            lines.Add(_language.Resource("PlayerCodex_FishRequiresDlc", Humanize(dlc)));
        return new FishCatchDetail(unlock, required, lines);
    }

    private ItemCatalogEntry? BaitFromRecipe(string? recipeId)
    {
        if (recipeId is null) return null;
        var recipe = _recipes.FirstOrDefault(r => string.Equals(r.Id, recipeId, StringComparison.OrdinalIgnoreCase));
        return recipe?.CreatesItemId is { } itemId ? _items.Find(itemId) : null;
    }

    private ItemCatalogEntry? BaitFromTag(string? tag)
        => tag is not null && _baitByTag.TryGetValue(tag, out var bait) ? bait : null;

    /// <summary>
    /// A specific time-of-day sentence from the four catch multipliers (0 = never then,
    /// more than 1 = best then). Null when the fish has no real preference.
    /// </summary>
    private string? TimeOfDayText(FishDefinition fish)
    {
        if (!fish.HasTimePreference) return null;
        var periods = new (string Name, double Mult)[]
        {
            (_language.Resource("PlayerCodex_FishTimeDawn"), fish.DawnMult),
            (_language.Resource("PlayerCodex_FishTimeMidday"), fish.NoonMult),
            (_language.Resource("PlayerCodex_FishTimeDusk"), fish.DuskMult),
            (_language.Resource("PlayerCodex_FishTimeNight"), fish.MidnightMult),
        };
        var open = periods.Where(p => p.Mult > 0).Select(p => p.Name).ToList();
        var best = periods.Where(p => p.Mult > 1).Select(p => p.Name).ToList();

        // Some periods are impossible (multiplier 0): say exactly when it CAN be caught.
        if (open.Count < periods.Length)
        {
            var when = open.Count == 0 ? _language.Resource("PlayerCodex_FishNeverBites") : Join(open);
            return best.Count > 0 && best.Count < open.Count
                ? _language.Resource("PlayerCodex_FishOnlyBitesBest", when, Join(best))
                : _language.Resource("PlayerCodex_FishOnlyBites", when);
        }
        // Otherwise it's catchable any time but favours certain periods.
        return best.Count > 0 ? _language.Resource("PlayerCodex_FishBitesBest", Join(best)) : null;
    }

    private string Join(List<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        2 => _language.Resource("PlayerCodex_FishTimeJoinAnd", parts[0], parts[1]),
        _ => _language.Resource("PlayerCodex_FishTimeJoinAnd", string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]),
    };

    /// <summary>Strips variant suffixes (_rare1, _AllDay, _torii) to the family base name.</summary>
    private static string BaseKey(string id) => VariantSuffix().Replace(id, string.Empty);

    [GeneratedRegex("_(rare\\d*|AllDay|torii)", RegexOptions.IgnoreCase)]
    private static partial Regex VariantSuffix();

    /// <summary>Turns a row id / flag (snake_case + CamelCase) into spaced Title Case.</summary>
    internal static string Humanize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var spaced = new StringBuilder(raw.Length + 8);
        var previous = '\0';
        foreach (var character in raw)
        {
            if (character == '_') { spaced.Append(' '); previous = ' '; continue; }
            if (char.IsUpper(character) && previous != '\0' && previous != ' ' && !char.IsUpper(previous)) spaced.Append(' ');
            spaced.Append(character);
            previous = character;
        }
        return spaced.ToString().Trim();
    }
}

/// <summary>The bait a fish unlocks, the bait it requires, and its catch-requirement lines.</summary>
public sealed record FishCatchDetail(
    ItemCatalogEntry? UnlockBait,
    ItemCatalogEntry? RequiredBait,
    IReadOnlyList<string> CatchLines);
