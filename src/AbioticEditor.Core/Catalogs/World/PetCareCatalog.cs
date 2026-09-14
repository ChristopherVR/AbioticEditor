using AbioticEditor.Core.Assets;
using Newtonsoft.Json.Linq;
namespace AbioticEditor.Core.WorldSaves;

public sealed record PetMutationFood(string Target, IReadOnlyList<string> Foods);
public sealed record PetCareDefinition(string ItemRow, string? ClassPath, IReadOnlyList<string> TamingFoods, IReadOnlyList<PetMutationFood> Mutations);
public static class PetCareCatalog
{
    public static IReadOnlyList<PetCareDefinition> Load(GameAssetProvider provider)
    {
        var pets = PetGameData.TryLoadFrom(provider);
        var table = provider.TryLoadDataTable("AbioticFactor/Content/Blueprints/DataTables/DT_Pets");
        if (pets is null || table is null || JObject.FromObject(table)["Rows"] is not JObject rows) return [];
        return pets.Definitions.Select(pet =>
        {
            var row = rows.Properties().FirstOrDefault(r => r.Name.Equals(pet.PetRow, StringComparison.OrdinalIgnoreCase))?.Value as JObject;
            var foods = Field(row, "TamingFood_")?.Select(f => (string?)f["RowName"]).OfType<string>().ToArray() ?? [];
            var mutations = Field(row, "Mutations_")?.OfType<JObject>().Select(m =>
            {
                var target = (string?)Field(m, "MutationTarget_")?["RowName"] ?? "";
                return new PetMutationFood(pets.Definitions.FirstOrDefault(p => p.PetRow.Equals(target, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? target,
                    Field(m, "MutationItems_")?.Select(f => (string?)f["RowName"]).OfType<string>().ToArray() ?? []);
            }).ToArray() ?? [];
            return new PetCareDefinition(pet.ItemRow ?? pet.PetRow, pet.ClassPath, foods, mutations);
        }).ToArray();
    }
    private static JToken? Field(JObject? row, string prefix) => row?.Properties().FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))?.Value;
}
