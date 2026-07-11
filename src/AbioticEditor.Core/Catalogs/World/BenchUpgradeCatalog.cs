using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One installable bench upgrade module (a row of the game's <c>DT_BenchUpgrades</c>).</summary>
public sealed record BenchUpgrade(string Row, string DisplayName)
{
    /// <summary>The full gameplay tag the game stores on a bench, e.g. <c>BenchUpgrade.TougherBench</c>.</summary>
    public string Tag => BenchUpgradeCatalog.TagPrefix + Row;
}

/// <summary>
/// The bench upgrade modules the game lets a player install on a crafting / cooking / ammo bench.
/// In the save these are <b>not</b> a numeric tier - each installed module is a gameplay tag
/// (<c>BenchUpgrade.&lt;Row&gt;</c>) inside the bench deployable's
/// <c>ChangableData_.GameplayTags_</c> container (note the in-game misspelling "Changable").
///
/// <para>Read/write helpers here operate on a single <c>DeployedObjectMap</c> entry's struct
/// property list and mutate the gameplay-tag list in place, so the writer re-serializes the rest
/// of the save byte-perfect. Untouched tag strings are preserved exactly (some saves store the
/// bare row, others the fully-qualified tag), so we only ever append the canonical form for a
/// newly-installed module and remove by matching the row.</para>
/// </summary>
public static class BenchUpgradeCatalog
{
    /// <summary>The gameplay-tag namespace prefix the game uses for bench upgrades.</summary>
    public const string TagPrefix = "BenchUpgrade.";

    /// <summary>Leaf prefix of the per-deployable changeable-data struct (note the misspelling).</summary>
    private const string ChangableDataPrefix = "ChangableData_";

    /// <summary>Leaf prefix of the gameplay-tag container inside the changeable-data struct.</summary>
    private const string GameplayTagsPrefix = "GameplayTags_";

    /// <summary>The 11 known upgrade modules (rows of <c>DT_BenchUpgrades</c>), with display names.</summary>
    public static IReadOnlyList<BenchUpgrade> All { get; } = new[]
    {
        new BenchUpgrade("ItemTransporter", "Item Transporter"),
        new BenchUpgrade("TougherBench", "Tougher Bench"),
        new BenchUpgrade("BenchWarmer", "Bench Warmer"),
        new BenchUpgrade("Dioxohealer", "Dioxohealer"),
        new BenchUpgrade("PortalSuppression", "Portal Suppression"),
        new BenchUpgrade("MatterSynthesizer", "Matter Synthesizer"),
        new BenchUpgrade("MetabolicField", "Metabolic Field"),
        new BenchUpgrade("BenchTurret", "Bench Turret"),
        new BenchUpgrade("Cheffigy", "Cheffigy"),
        new BenchUpgrade("ItemTransporter_ChefStation", "Item Transporter (Chef Station)"),
        new BenchUpgrade("ItemTransporter_UpgradeBench", "Item Transporter (Upgrade Bench)"),
    };

    /// <summary>The display name for an upgrade row, or the prettified row when unknown.</summary>
    public static string DisplayName(string row)
    {
        foreach (var u in All)
        {
            if (string.Equals(u.Row, row, StringComparison.OrdinalIgnoreCase))
            {
                return u.DisplayName;
            }
        }
        return row.Replace('_', ' ');
    }

    /// <summary>Strips the <see cref="TagPrefix"/> from a tag, leaving the bare row name.</summary>
    public static string RowFromTag(string tag)
        => tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)
            ? tag[TagPrefix.Length..]
            : tag;

    /// <summary>
    /// The rows of every upgrade currently installed on this deployable (empty when the bench
    /// has no upgrades, or for a non-bench deployable that carries no gameplay tags).
    /// </summary>
    public static IReadOnlyList<string> ReadInstalledRows(IList<FPropertyTag> deployableProps)
    {
        var container = FindTagContainer(deployableProps);
        if (container is null)
        {
            return Array.Empty<string>();
        }
        var rows = new List<string>(container.Tags.Count);
        foreach (var tag in container.Tags)
        {
            if (tag?.Value is { Length: > 0 } value)
            {
                rows.Add(RowFromTag(value));
            }
        }
        return rows;
    }

    /// <summary>True when this deployable can carry bench upgrades (its gameplay-tag container exists).</summary>
    public static bool SupportsUpgrades(IList<FPropertyTag> deployableProps)
        => FindTagContainer(deployableProps) is not null;

    /// <summary>
    /// Installs or removes one upgrade row on a deployable, mutating its gameplay-tag list in place.
    /// Returns true when the list changed. A newly-installed module is appended as the canonical
    /// <c>BenchUpgrade.&lt;Row&gt;</c> tag; removal drops every tag whose row matches. Returns false
    /// when the deployable has no gameplay-tag container or the value is already as requested.
    /// </summary>
    public static bool SetInstalled(IList<FPropertyTag> deployableProps, string row, bool installed)
    {
        var container = FindTagContainer(deployableProps);
        if (container is null)
        {
            return false;
        }

        var present = container.Tags.Any(t =>
            t?.Value is { Length: > 0 } v
            && string.Equals(RowFromTag(v), row, StringComparison.OrdinalIgnoreCase));

        if (installed == present)
        {
            return false;
        }

        if (installed)
        {
            container.Tags.Add(new FString(TagPrefix + row));
            return true;
        }

        var changed = false;
        for (var i = container.Tags.Count - 1; i >= 0; i--)
        {
            if (container.Tags[i]?.Value is { Length: > 0 } v
                && string.Equals(RowFromTag(v), row, StringComparison.OrdinalIgnoreCase))
            {
                container.Tags.RemoveAt(i);
                changed = true;
            }
        }
        return changed;
    }

    private static GameplayTagContainerStruct? FindTagContainer(IList<FPropertyTag> deployableProps)
    {
        if (deployableProps.FindByPrefix(ChangableDataPrefix)?.Property is not StructProperty cd
            || cd.Value is not PropertiesStruct cdProps)
        {
            return null;
        }
        return cdProps.Properties.FindByPrefix(GameplayTagsPrefix)?.Property is StructProperty tagsSp
            && tagsSp.Value is GameplayTagContainerStruct container
            ? container
            : null;
    }
}
