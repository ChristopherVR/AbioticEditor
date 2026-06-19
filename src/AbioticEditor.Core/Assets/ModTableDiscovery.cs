using System.Runtime.CompilerServices;
using AbioticEditor.Core.Diagnostics;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Finds mod-added (and patch-added) DataTables across the mounted paks so every catalog can
/// merge them without hardcoding the base content root. Discovery is keyed on the table's
/// <c>RowStructName</c>: a mod that adds items/recipes/traits/etc. must reuse the game's row
/// struct for the game itself to read the table, so struct identity catches mod tables
/// regardless of their filename or content root (e.g. <c>MyMod/Content/...</c>).
///
/// The candidate set is restricted to assets named like a DataTable (the UE/AF convention,
/// e.g. <c>DT_*</c>, <c>CDT_*</c>, <c>ItemTable_*</c>) so the scan does not have to load all
/// ~50k mounted assets. A mod that ships a table under a non-conventional name will not be
/// auto-discovered; its content simply degrades to raw ids, exactly as before this feature.
/// </summary>
public static class ModTableDiscovery
{
    private static readonly string[] TableNamePrefixes =
    {
        "DT_", "CDT_", "BDT_", "ItemTable_", "DataTable",
    };

    private const string AssetExtension = ".uasset";

    // Built once per provider (loading every candidate table is the expensive part); cached so
    // the ~10 catalogs that query it during a single EnsureLoaded don't each re-scan.
    private static readonly ConditionalWeakTable<GameAssetProvider, IReadOnlyDictionary<string, IReadOnlyList<string>>> IndexCache = new();

    /// <summary>
    /// Returns the package paths (no extension) of every discovered DataTable whose row struct is
    /// <paramref name="rowStructName"/>, excluding <paramref name="excludePaths"/> (the tables a
    /// catalog already loaded by name). Empty when the struct is unknown or nothing matches.
    /// Never throws - a failed probe is logged and skipped.
    /// </summary>
    public static IReadOnlyList<string> DiscoverTablesByRowStruct(
        GameAssetProvider provider,
        string? rowStructName,
        IEnumerable<string> excludePaths)
    {
        if (string.IsNullOrEmpty(rowStructName)) return Array.Empty<string>();

        var index = GetOrBuildIndex(provider);
        if (!index.TryGetValue(rowStructName, out var matches)) return Array.Empty<string>();

        var exclude = new HashSet<string>(excludePaths, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(matches.Count);
        foreach (var path in matches)
        {
            if (!exclude.Contains(path)) result.Add(path);
        }
        return result;
    }

    /// <summary>
    /// Convenience over <see cref="DiscoverTablesByRowStruct"/>: yields the loaded
    /// <see cref="UDataTable"/> for every discovered table (skipping any that fail to load).
    /// Lets a single-table catalog merge mod/patch tables in a couple of lines.
    /// </summary>
    public static IEnumerable<UDataTable> LoadTablesByRowStruct(
        GameAssetProvider provider,
        string? rowStructName,
        IEnumerable<string> excludePaths)
    {
        foreach (var path in DiscoverTablesByRowStruct(provider, rowStructName, excludePaths))
        {
            UDataTable? dt = null;
            try
            {
                dt = provider.TryLoadDataTable(path);
            }
            catch (Exception ex)
            {
                EditorLog.Warn("ModTableDiscovery", $"Failed to load discovered table '{path}'.", ex);
            }
            if (dt is not null) yield return dt;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> GetOrBuildIndex(GameAssetProvider provider)
        => IndexCache.GetValue(provider, BuildIndex);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildIndex(GameAssetProvider provider)
    {
        var byStruct = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!provider.HasMappings)
        {
            // Without usmap mappings DataTable properties (and the row struct) can't be read.
            return Empty;
        }

        var scanned = 0;
        foreach (var assetPath in provider.AssetPaths)
        {
            if (!LooksLikeDataTable(assetPath)) continue;

            var pkgPath = assetPath[..^AssetExtension.Length];
            try
            {
                var dt = provider.TryLoadDataTable(pkgPath);
                var structName = dt?.RowStructName;
                if (string.IsNullOrEmpty(structName)) continue;
                scanned++;
                if (!byStruct.TryGetValue(structName, out var list))
                {
                    list = new List<string>();
                    byStruct[structName] = list;
                }
                list.Add(pkgPath);
            }
            catch (Exception ex)
            {
                EditorLog.Warn("ModTableDiscovery", $"Failed to probe candidate table '{pkgPath}'.", ex);
            }
        }

        EditorLog.Info("ModTableDiscovery", $"Indexed {scanned} DataTable(s) across {byStruct.Count} row struct(s).");
        return byStruct.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>True when the mounted asset path is named like a cooked DataTable asset.</summary>
    public static bool LooksLikeDataTable(string assetPath)
    {
        if (!assetPath.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase)) return false;
        var nameStart = assetPath.LastIndexOf('/') + 1;
        var nameEnd = assetPath.Length - AssetExtension.Length;
        if (nameEnd <= nameStart) return false;
        var name = assetPath.AsSpan(nameStart, nameEnd - nameStart);

        foreach (var prefix in TableNamePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
