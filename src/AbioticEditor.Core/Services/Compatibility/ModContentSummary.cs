using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.Compatibility;

/// <summary>The kinds of game content a mod can bring, in the words a player uses.</summary>
public enum ModContentKind
{
    Traders,
    TraderStock,
    Jobs,
    Traits,
    Items,
    Recipes,
    Effects,
    Creatures,
    Cosmetics,
    Other,

    /// <summary>Rows that only back up other content (dialogue, spawn rules, job banners, textures); not listed on their own.</summary>
    Supporting,
}

/// <summary>How many things of one kind a mod adds, and how many base-game ones it changes.</summary>
public sealed record ModContentCount(ModContentKind Kind, int Added, int Changed);

/// <summary>
/// What one loaded mod brings, grouped into plain categories: "adds 1 trader and 6 items,
/// changes 1 trader's stock". Built from the rows the game itself merges, so it describes what
/// the mod actually does in game rather than what its name suggests.
/// </summary>
/// <param name="ReplacedGameFiles">Base-game files the mod replaces outright (older pak-style changes).</param>
public sealed record ModContentSummary(string ModKey, string ModName, IReadOnlyList<ModContentCount> Counts, int ReplacedGameFiles)
{
    /// <summary>True when the editor found nothing it can describe (for example a mod that only changes code or visuals).</summary>
    public bool IsEmpty => Counts.Count == 0 && ReplacedGameFiles == 0;

    /// <summary>One summary per loaded mod, in load order, from a mounted provider.</summary>
    public static IReadOnlyList<ModContentSummary> For(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return Build(provider.ModContributions(), provider.ReplacedGameFileCounts());
    }

    /// <summary>Builds summaries from contribution rows (exposed for tests).</summary>
    public static IReadOnlyList<ModContentSummary> Build(
        IEnumerable<ModRowContribution> contributions, IReadOnlyDictionary<string, int> replacedFiles)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(replacedFiles);
        var byMod = contributions.GroupBy(row => row.Origin.ModKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                // An item row lands in both its own table and the global item table; count each once.
                var rows = group.Select(row => (Kind: KindOf(row.Table), row.RowId, row.Origin.Change))
                    .Distinct().ToArray();
                var counts = rows.GroupBy(row => row.Kind)
                    .Select(kind => new ModContentCount(kind.Key,
                        kind.Count(row => row.Change == ModChange.Added),
                        kind.Count(row => row.Change == ModChange.Changed)))
                    .OrderBy(count => count.Kind).ToArray();
                return new ModContentSummary(group.Key, group.First().Origin.ModName, counts,
                    replacedFiles.GetValueOrDefault(group.Key));
            }).ToList();
        // A mod that only replaces game files still deserves a line.
        foreach (var (key, files) in replacedFiles)
            if (!byMod.Any(mod => mod.ModKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
                byMod.Add(new ModContentSummary(key, key, [], files));
        return byMod;
    }

    /// <summary>Maps a target data table to the kind of content its rows are.</summary>
    public static ModContentKind KindOf(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var name = table[(table.LastIndexOf('/') + 1)..];
        if (name.Equals("DT_NPC_Traders", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Traders;
        if (name.Equals("DT_NPC_TraderItems", StringComparison.OrdinalIgnoreCase)) return ModContentKind.TraderStock;
        if (name.Equals("DT_PhDs", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Jobs;
        if (name.Equals("DT_Traits", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Traits;
        if (name.StartsWith("ItemTable_", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Items;
        if (name.Contains("Recipe", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Recipes;
        if (name.Equals("DT_BuffsDebuffs", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Effects;
        if (name.Equals("DT_Pets", StringComparison.OrdinalIgnoreCase) || name.Equals("DT_NPCList", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("DT_PetMutation", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Creatures;
        if (name.Contains("Customization", StringComparison.OrdinalIgnoreCase)) return ModContentKind.Cosmetics;
        if (name is "DT_NPC_Conversations" or "DT_ClassManager" or "DT_JobBanners" or "DT_TextureVariants" or "DT_Projectiles")
            return ModContentKind.Supporting;
        return ModContentKind.Other;
    }
}
