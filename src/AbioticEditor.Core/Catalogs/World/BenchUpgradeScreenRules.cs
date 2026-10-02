namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Which bench upgrades the game offers on which bench, as its own bench upgrade screen
/// (<c>W_BenchUpgradeScreen</c>) decides it. The screen has one entry per upgrade row it can
/// show; its event graph then checks whether the bench is one particular class (the Chef
/// Station, <c>Deployed_Bench_CookingStation_C</c>, in the current game) and collapses the
/// entries that do not belong. Read from the game files by
/// <c>GameAssetProvider.GetBenchUpgradeScreenRules</c>; nothing here names a bench or a row.
/// </summary>
/// <param name="Rows">Every upgrade row the screen has an entry for. Table rows without an entry
/// (an unfinished upgrade, say) are never offered.</param>
/// <param name="FamilyClass">The class the screen tests the bench against, or null when no such
/// test was found (then every bench gets every row).</param>
/// <param name="HiddenForFamily">Rows the screen collapses on a bench of <paramref name="FamilyClass"/>.</param>
/// <param name="HiddenForOthers">Rows the screen collapses on every other bench.</param>
public sealed record BenchUpgradeScreenRules(
    IReadOnlyList<string> Rows,
    string? FamilyClass,
    IReadOnlyList<string> HiddenForFamily,
    IReadOnlyList<string> HiddenForOthers)
{
    /// <summary>
    /// The rows offered on a bench whose class chain (the class first, then its parents) is
    /// <paramref name="classChain"/>, in the screen's order.
    /// </summary>
    public IReadOnlyList<string> RowsFor(IEnumerable<string> classChain)
    {
        ArgumentNullException.ThrowIfNull(classChain);
        var isFamily = FamilyClass is not null
            && classChain.Any(c => string.Equals(c, FamilyClass, StringComparison.OrdinalIgnoreCase));
        var hidden = isFamily ? HiddenForFamily : HiddenForOthers;
        return Rows.Where(r => !hidden.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The catalog entries to list for a bench: the rows the screen offers it, in catalog order,
    /// plus any row already installed on it (so an installed upgrade can always be removed).
    /// </summary>
    public IReadOnlyList<BenchUpgrade> UpgradesFor(IEnumerable<string> classChain, IEnumerable<string> installedRows)
    {
        ArgumentNullException.ThrowIfNull(installedRows);
        var offered = RowsFor(classChain);
        // A tag already shown through an offered row (the Chef Station's own Item Transporter
        // installs the same tag as the crafting bench's) is not listed a second time.
        var offeredTags = offered.Select(BenchUpgradeCatalog.TagRowOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = installedRows.Where(r => !offeredTags.Contains(r)).ToList();
        return BenchUpgradeCatalog.All
            .Where(u => offered.Contains(u.Row, StringComparer.OrdinalIgnoreCase)
                || installed.Contains(u.Row, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
