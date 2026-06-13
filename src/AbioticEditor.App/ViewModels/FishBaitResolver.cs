using System.Text;
using System.Text.RegularExpressions;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Resolves, for each fish, the bait it unlocks and the bait it needs to be caught, plus
/// the plain-language list of catch requirements (location, story flag, bait, time of day).
/// Bait resolution uses both the row's <c>RecipeToUnlock</c> and the family's
/// <c>Fishing.Bait.*</c> tag, so fish without an explicit recipe (e.g. Gem Crab) still show
/// their associated bait.
/// </summary>
public sealed partial class FishBaitResolver
{
    private readonly ItemCatalog? _items;
    private readonly Dictionary<string, ItemCatalogEntry> _baitByTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemCatalogEntry> _familyBait = new(StringComparer.OrdinalIgnoreCase);

    public FishBaitResolver(IReadOnlyList<FishDefinition> fish, ItemCatalog? items)
    {
        _items = items;

        // Map every bait gameplay tag (Fishing.Bait.X) to its item.
        if (items is not null)
        {
            foreach (var e in items.Entries)
            {
                foreach (var t in e.Tags)
                {
                    if (t.StartsWith("Fishing.Bait", StringComparison.OrdinalIgnoreCase))
                    {
                        _baitByTag[t] = e;
                    }
                }
            }
        }

        // One bait per fish family (base name shared by the common fish + its rare variants):
        // prefer the recipe a member unlocks, else the bait a member requires.
        foreach (var grp in fish.GroupBy(f => BaseKey(f.Id), StringComparer.OrdinalIgnoreCase))
        {
            var bait = grp.Select(f => BaitFromRecipe(f.UnlockRecipeId)).FirstOrDefault(b => b is not null)
                    ?? grp.Select(f => BaitFromTag(f.RequiredBaitTag)).FirstOrDefault(b => b is not null);
            if (bait is not null) _familyBait[grp.Key] = bait;
        }
    }

    public FishDetail Detail(FishDefinition f)
    {
        var unlock = BaitFromRecipe(f.UnlockRecipeId)
                     ?? (_familyBait.TryGetValue(BaseKey(f.Id), out var fam) ? fam : null);
        var required = BaitFromTag(f.RequiredBaitTag);

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Location))
        {
            lines.Add($"Cast where there's {f.Location.ToLowerInvariant()}.");
        }
        if (f.RequiredWorldFlag is { } flag)
        {
            lines.Add($"Story progress: needs \"{Humanize(flag)}\" reached first.");
        }
        if (required is not null)
        {
            lines.Add($"Bait up with {required.DisplayName} (shown below) before casting.");
        }
        else if (f.RequiresSpecialCatch)
        {
            lines.Add("Needs a specific bait equipped to bite.");
        }
        if (TimeOfDayText(f) is { } tod)
        {
            lines.Add(tod);
        }
        if (f.RequiredDlcId is { } dlc)
        {
            lines.Add($"Requires DLC: {Humanize(dlc)}.");
        }
        return new FishDetail(unlock, required, lines);
    }

    private ItemCatalogEntry? BaitFromRecipe(string? recipeId)
    {
        if (recipeId is null) return null;
        var recipe = GameDataServices.AllRecipeInfos
            .FirstOrDefault(r => string.Equals(r.Id, recipeId, StringComparison.OrdinalIgnoreCase));
        return recipe?.CreatesItemId is { } itemId ? _items?.Find(itemId) : null;
    }

    private ItemCatalogEntry? BaitFromTag(string? tag)
        => tag is not null && _baitByTag.TryGetValue(tag, out var bait) ? bait : null;

    /// <summary>
    /// A specific time-of-day sentence from the four catch multipliers (0 = never then,
    /// &gt;1 = best then). Null when the fish has no real preference (all neutral).
    /// </summary>
    private static string? TimeOfDayText(FishDefinition f)
    {
        if (!f.HasTimePreference) return null;
        var periods = new (string Name, double Mult)[]
        {
            ("dawn", f.DawnMult), ("midday", f.NoonMult), ("dusk", f.DuskMult), ("night", f.MidnightMult),
        };
        var open = periods.Where(p => p.Mult > 0).Select(p => p.Name).ToList();
        var best = periods.Where(p => p.Mult > 1).Select(p => p.Name).ToList();

        // Some periods are impossible (multiplier 0): say exactly when it CAN be caught.
        if (open.Count < periods.Length)
        {
            var when = open.Count == 0 ? "(never bites — check the wiki)" : Join(open);
            return best.Count > 0 && best.Count < open.Count
                ? $"Only bites at {when} (best at {Join(best)})."
                : $"Only bites at {when}.";
        }
        // Otherwise it's catchable any time but favours certain periods.
        return best.Count > 0 ? $"Bites best at {Join(best)}." : null;
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + $" and {parts[^1]}",
    };

    /// <summary>Strips variant suffixes (<c>_rare1</c>, <c>_AllDay</c>, <c>_torii</c>) to the family base name.</summary>
    private static string BaseKey(string id) => VariantSuffix().Replace(id, string.Empty);

    [GeneratedRegex("_(rare\\d*|AllDay|torii)", RegexOptions.IgnoreCase)]
    private static partial Regex VariantSuffix();

    /// <summary>Turns a row id / flag (snake_case + CamelCase) into spaced Title Case.</summary>
    private static string Humanize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var spaced = new StringBuilder(raw.Length + 8);
        var prev = '\0';
        foreach (var c in raw)
        {
            if (c == '_') { spaced.Append(' '); prev = ' '; continue; }
            if (char.IsUpper(c) && prev != '\0' && prev != ' ' && !char.IsUpper(prev)) spaced.Append(' ');
            spaced.Append(c);
            prev = c;
        }
        return spaced.ToString().Trim();
    }
}

/// <summary>The bait a fish unlocks, the bait it requires, and its catch-requirement lines.</summary>
public sealed record FishDetail(
    ItemCatalogEntry? UnlockBait,
    ItemCatalogEntry? RequiredBait,
    IReadOnlyList<string> CatchLines);
