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
    public static IReadOnlyList<string> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Array.Empty<string>();
        try
        {
            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/Environment/DT_MapPamphlets");
            var result = new List<string>();
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                foreach (var kv in dt.RowMap)
                {
                    if (!string.IsNullOrEmpty(kv.Key.Text)) result.Add(kv.Key.Text);
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
