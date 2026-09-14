using AbioticEditor.Core.Assets;
using Newtonsoft.Json.Linq;
namespace AbioticEditor.Core.Items;

public sealed record WeaponCoatingDefinition(int Index, string Row, string Name, int Durability);
public static class WeaponCoatingCatalog
{
    public static bool Supports(ItemCatalogEntry item)
        => item.Tags.Any(tag => tag == "Item.Weapon" || tag.StartsWith("Item.Weapon.", StringComparison.Ordinal))
            && !item.Tags.Any(tag => tag == "Item.Weapon.NoCoatings" || tag.StartsWith("Item.Weapon.NoCoatings.", StringComparison.Ordinal));

    public static IReadOnlyList<WeaponCoatingDefinition> Load(GameAssetProvider provider)
    {
        var table = provider.TryLoadDataTable("AbioticFactor/Content/Blueprints/DataTables/DT_WeaponCoatings");
        if (table is null || JObject.FromObject(table)["Rows"] is not JObject rows) return [];
        // The game resolves WeaponCoating through GetDataTableRowNames at this exact index.
        return rows.Properties().Select((row, index) => new WeaponCoatingDefinition(index, row.Name,
            (string?)((JObject)row.Value).Properties().FirstOrDefault(p => p.Name.StartsWith("CoatingName_", StringComparison.Ordinal))?.Value["LocalizedString"] ?? row.Name,
            (int?)((JObject)row.Value).Properties().FirstOrDefault(p => p.Name.StartsWith("Durability_", StringComparison.Ordinal))?.Value ?? 100)).ToArray();
    }
}
