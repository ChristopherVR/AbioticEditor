using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.Items;

/// <summary>
/// Map pamphlet vocabulary from <c>DT_MapPamphlets</c> (the rows of the save's
/// <c>MapsUnlocked_</c> array, case-insensitively - saves contain mixed-case rows like
/// <c>map_office2</c> for the table's <c>Map_Office2</c>).
/// </summary>
public static class MapCatalog
{
    private const string PrimaryTable = "AbioticFactor/Content/Blueprints/DataTables/Environment/DT_MapPamphlets";

    public static IReadOnlyList<string> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? structName = null;
        try
        {
            var primary = provider.TryLoadDataTable(PrimaryTable);
            structName = primary?.RowStructName;
            AddRows(primary, result, seen);
        }
        catch
        {
            return result;
        }

        // Merge mod/patch map tables that share the pamphlet row struct, under any content root.
        foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, structName, new[] { PrimaryTable }))
        {
            AddRows(dt, result, seen);
        }
        return result;
    }

    private static void AddRows(UDataTable? dt, List<string> result, HashSet<string> seen)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            if (!string.IsNullOrEmpty(kv.Key.Text) && seen.Add(kv.Key.Text)) result.Add(kv.Key.Text);
        }
    }
}
