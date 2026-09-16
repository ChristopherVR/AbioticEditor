using AbioticEditor.Core.Assets;
using Newtonsoft.Json.Linq;
namespace AbioticEditor.Core.WorldSaves;

public sealed record PetMutationFood(string Target, IReadOnlyList<string> Foods);
public sealed record PetCareDefinition(string ItemRow, string? ClassPath, IReadOnlyList<string> TamingFoods, IReadOnlyList<PetMutationFood> Mutations);

/// <summary>
/// One choice in a carried pet's <c>PetMutation</c> picker: <see cref="Value"/> is the exact
/// integer the save stores for this target (see <see cref="PetCareCatalog.MutationOptionsFor"/>
/// for how it is derived and the fixture evidence behind it).
/// </summary>
public sealed record PetMutationOption(int Value, string TargetRow, string DisplayName, IReadOnlyList<string> Foods);

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

    /// <summary>
    /// The valid <c>PetMutation</c> choices for a carried pet (item row or <c>DT_Pets</c> row),
    /// derived from <c>DT_Pets</c>' <c>Mutations_</c> array, plus what the save's stored int
    /// actually encodes for it.
    ///
    /// <para><c>DT_Pets</c> defines <c>Mutations_</c> only on a family's "owner" row (e.g. the
    /// base <c>pest</c> row lists Volatile/Snow/Magma/Enlightened/Leyak/Carbonated as targets,
    /// plus the base <c>Pest</c> NPC itself at position 0; every other pest variant's own
    /// <c>Mutations_</c> is empty). A carried pet's saved <c>PetMutation</c> int is that owner
    /// list's 1-based position of the pet's own current identity (0 is the save's default/unset
    /// sentinel, never a list entry): verified against two real carried pets in the fixtures
    /// (session 2026-09-17, see <c>docs/reference/research/</c>) - a Leyak Pest, whose identity
    /// sits at 0-based index 5 in <c>pest</c>'s list, stored <c>PetMutation=6</c>; a crafted Magma
    /// Skink, whose identity sits at 0-based index 0 in <c>Skink_Crafted</c>'s list (the separate
    /// weapon-form lineage, not <c>Skink</c>'s own list), stored <c>PetMutation=1</c>. Both match
    /// index+1 exactly.</para>
    ///
    /// <para>Because the crafted lineage owns its own separate list (not inherited through
    /// <c>DefaultParent_</c>), the owner row is found by searching every <c>DT_Pets</c> row's
    /// <c>Mutations_</c> for one that targets this pet's own row - not by walking the
    /// <c>DefaultParent_</c> chain. When the pet's own row already carries a non-empty
    /// <c>Mutations_</c> (it IS a family owner), that row is used directly.</para>
    /// </summary>
    public static IReadOnlyList<PetMutationOption> MutationOptionsFor(GameAssetProvider provider, string? itemRowOrPetRow)
    {
        if (string.IsNullOrEmpty(itemRowOrPetRow)) return [];
        var pets = PetGameData.TryLoadFrom(provider);
        var table = provider.TryLoadDataTable("AbioticFactor/Content/Blueprints/DataTables/DT_Pets");
        if (pets is null || table is null || JObject.FromObject(table)["Rows"] is not JObject rows) return [];

        var petRow = pets.Definitions.FirstOrDefault(p =>
                p.ItemRow?.Equals(itemRowOrPetRow, StringComparison.OrdinalIgnoreCase) == true
                || p.PetRow.Equals(itemRowOrPetRow, StringComparison.OrdinalIgnoreCase))
            ?.PetRow ?? itemRowOrPetRow;

        JObject? RowByName(string name) => rows.Properties().FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value as JObject;

        var ownMutations = Field(RowByName(petRow), "Mutations_");
        var owner = ownMutations is not null && ownMutations.Any() ? ownMutations : null;
        if (owner is null)
        {
            foreach (var candidate in rows.Properties())
            {
                var candidateMutations = Field(candidate.Value as JObject, "Mutations_");
                if (candidateMutations is null) continue;
                var matches = candidateMutations.OfType<JObject>().Any(m =>
                    ((string?)Field(m, "MutationTarget_")?["RowName"])?.Equals(petRow, StringComparison.OrdinalIgnoreCase) == true);
                if (matches) { owner = candidateMutations; break; }
            }
        }
        if (owner is null) return [];

        var options = new List<PetMutationOption>();
        var index = 0;
        foreach (var m in owner.OfType<JObject>())
        {
            index++;
            var target = (string?)Field(m, "MutationTarget_")?["RowName"] ?? "";
            var display = pets.Definitions.FirstOrDefault(p => p.PetRow.Equals(target, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? target;
            var foodItems = Field(m, "MutationItems_")?.Select(f => (string?)f["RowName"]).OfType<string>().ToArray() ?? [];
            options.Add(new PetMutationOption(index, target, display, foodItems));
        }
        return options;
    }

    private static JToken? Field(JObject? row, string prefix) => row?.Properties().FirstOrDefault(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))?.Value;
}
