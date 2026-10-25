using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.PlayerSaves;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Items;

/// <summary>Explicit grant fields, separate from ingredient-based learning and material acquisition.</summary>
public enum RecipeSpecialGrantKind { Conversation, Trait, Compendium, Inspectable, Cooking, Processing, BenchAccess }

/// <summary>
/// A recipe grant listed by a source asset. This does not establish present access to the NPC,
/// completed dialogue, selected compendium section, or current application of a trait buff.
/// SectionKind and SectionIndex identify the exact source line/section; co-triggered world
/// flags are deliberately not represented as prerequisites.
/// </summary>
public sealed record RecipeSpecialGrant(string RecipeId, RecipeSpecialGrantKind Kind,
    string SourceId, string SourceLabel, string SourceAssetPath, string? SectionKind = null,
    int? RequiredCount = null, string? TraitId = null, int? SectionIndex = null)
{
    public string? LevelId { get; init; }
    public string? TriggerFunction { get; init; }
    public string? SourceObjectPath { get; init; }
}

/// <summary>Reads verified conversation, trait-buff and compendium grant fields from current assets.</summary>
public static class RecipeSpecialLearningCatalog
{
    private const string Conversations = "AbioticFactor/Content/Blueprints/DataTables/DT_NPC_Conversations";
    private const string Compendium = "AbioticFactor/Content/Blueprints/DataTables/DT_Compendium";
    private const string SoupRecipes = "AbioticFactor/Content/Blueprints/DataTables/DT_SoupRecipes";
    private const string ChemistryRecipes = "AbioticFactor/Content/Blueprints/DataTables/DT_ChemistryRecipes";
    private const string CookingHandler = "AbioticFactor/Content/Blueprints/Items/FakeItems/CookingProxy_BP";
    private const string ProcessingHandler = "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_ProcessingBench_ParentBP";
    private const string Buffs = "AbioticFactor/Content/Blueprints/DataTables/BuffsDebuffs/DT_BuffsDebuffs";
    private static readonly string[] DialogueGroups = ["BeckoningLines", "InitalContactMessages", "ReturnMessages"];

    public static IReadOnlyList<RecipeSpecialGrant> LoadFrom(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.HasMappings) return [];
        var result = new List<RecipeSpecialGrant>();
        ReadTable(Conversations, (id, row) => result.AddRange(ConversationGrants(id, row)));
        ReadTable(Compendium, (id, row) => result.AddRange(CompendiumGrants(id, row)));
        // These handlers reference these exact tables in their verified bytecode. Do not apply
        // their behavior to arbitrary rows merely because a display Source says Soup/Chemistry.
        ReadTable(SoupRecipes, (id, _) => result.Add(new(id, RecipeSpecialGrantKind.Cooking, id, id, CookingHandler)));
        ReadTable(ChemistryRecipes, (id, _) => result.Add(new(id, RecipeSpecialGrantKind.Processing, id, id, ProcessingHandler)));
        var buffs = provider.TryLoadDataTable(Buffs);
        foreach (var trait in TraitCatalog.LoadDetailsFrom(provider).Values)
        {
            if (string.IsNullOrWhiteSpace(trait.BuffRowName) || buffs is null) continue;
            var buff = buffs.RowMap.FirstOrDefault(pair => pair.Key.Text.Equals(trait.BuffRowName, StringComparison.OrdinalIgnoreCase)).Value;
            if (buff is null || Field(buff, "BuffLogic_Object") is not FSoftObjectPath logic) continue;
            var asset = logic.AssetPathName.Text;
            var separator = asset.LastIndexOf('.');
            if (separator < 0) continue;
            try
            {
                var defaults = provider.LoadPackageInternal(asset[..separator]).GetExports()
                    .FirstOrDefault(export => export.Name.StartsWith("Default__", StringComparison.Ordinal));
                if (defaults is null) continue;
                foreach (var recipe in RowIds(defaults.Properties.FirstOrDefault(property => property.Name.Text == "Recipes To Give")?.Tag?.GenericValue))
                    result.Add(new(recipe, RecipeSpecialGrantKind.Trait, trait.Id, trait.DisplayName, asset[..separator], TraitId: trait.Id));
            }
            catch (Exception exception)
            {
                EditorLog.Warn("RecipeLearning", $"Could not read recipe grants from trait buff {asset}: {exception.Message}");
            }
        }
        ReadBenchAccess(provider, result);
        ReadPlacedInspectables(provider, result);
        return result.DistinctBy(grant => (grant.RecipeId.ToUpperInvariant(), grant.Kind,
            grant.SourceId.ToUpperInvariant(), grant.SectionKind, grant.SectionIndex)).ToArray();

        void ReadTable(string path, Action<string, FStructFallback> append)
        {
            try
            {
                if (provider.TryLoadDataTable(path) is not { } table) return;
                foreach (var (id, row) in table.RowMap) append(id.Text, row);
            }
            catch (Exception exception)
            {
                EditorLog.Warn("RecipeLearning", $"Could not read recipe grant table {path}: {exception.Message}");
            }
        }
    }

    private static void ReadBenchAccess(GameAssetProvider provider, List<RecipeSpecialGrant> result)
    {
        foreach (var path in provider.AssetPaths.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && path.Contains("/Blueprints/DeployedObjects/", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var package = provider.LoadPackageInternal(path);
                if (!package.NameMap.Any(name => name.Name == "RecipesToUnlockOnAccess")) continue;
                var defaults = package.GetExports().FirstOrDefault(export => export.Name.StartsWith("Default__", StringComparison.Ordinal));
                if (defaults is null) continue;
                var label = Structure(InheritedField(defaults, "BenchDataTableRow")) is { } bench
                    ? Text(Field(bench, "RowName")) : null;
                label ??= Text(InheritedField(defaults, "ObjectDisplayText")) ?? string.Empty;
                result.AddRange(BenchGrants(path, label, Field(defaults, "RecipesToUnlockOnAccess")));
            }
            catch (Exception exception)
            {
                EditorLog.Warn("RecipeLearning", $"Could not read workbench grants from {path}: {exception.Message}");
            }
        }
    }

    private static void ReadPlacedInspectables(GameAssetProvider provider, List<RecipeSpecialGrant> result)
    {
        foreach (var path in provider.AssetPaths.Where(path => path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                // Name maps and class references are package metadata. Never enumerate all
                // level objects, meshes, inventories or scripts to locate these few actors.
                var package = provider.LoadPackageInternal(path);
                if (!package.NameMap.Any(name => name.Name?.Contains("InteractableProp", StringComparison.OrdinalIgnoreCase) == true)) continue;
                for (var index = 0; index < package.ExportMapLength; index++)
                {
                    var className = package switch
                    {
                        Package pak => pak.ExportMap[index].ClassIndex.ResolvedObject?.Name.Text,
                        IoPackage io => io.ResolveObjectIndex(io.ExportMap[index].ClassIndex)?.Name.Text,
                        _ => null,
                    };
                    if (className?.Contains("InteractableProp", StringComparison.OrdinalIgnoreCase) != true) continue;
                    var actor = package.ExportsLazy[index].Value;
                    var label = Text(InheritedField(actor, "ObjectDisplayText")) ?? string.Empty;
                    result.AddRange(InspectableGrants(path, actor.GetPathName(), label, Field(actor, "RecipesOnInteract")));
                }
            }
            catch (Exception exception)
            {
                EditorLog.Warn("RecipeLearning", $"Could not read inspection grants in {path}: {exception.Message}");
            }
        }
    }

    internal static IReadOnlyList<RecipeSpecialGrant> InspectableGrants(string levelPath, string objectPath, string label, object? handles)
        => RowIds(handles).Select(recipe => new RecipeSpecialGrant(recipe, RecipeSpecialGrantKind.Inspectable,
            objectPath, label, levelPath)
        {
            LevelId = Path.GetFileNameWithoutExtension(levelPath),
            SourceObjectPath = objectPath,
            TriggerFunction = "InteractableProp_BP_C.ExecuteUbergraph_InteractableProp_BP",
        }).ToArray();

    internal static IReadOnlyList<RecipeSpecialGrant> BenchGrants(string assetPath, string label, object? names)
        => NameIds(names).Select(recipe => new RecipeSpecialGrant(recipe, RecipeSpecialGrantKind.BenchAccess,
            assetPath, label, assetPath)
        {
            TriggerFunction = "RecipesToUnlockOnAccess",
        }).ToArray();

    private static object? InheritedField(UObject instance, string name)
    {
        if (Field(instance, name) is { } own) return own;
        var type = instance.Class?.Load<UClass>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (type is not null && visited.Add(type.GetPathName()))
        {
            if (type.ClassDefaultObject?.Load<UObject>() is { } defaults && Field(defaults, name) is { } inherited) return inherited;
            type = type.Super?.Load<UClass>() ?? type.SuperStruct?.Load<UClass>();
        }
        return null;
    }

    private static string[] NameIds(object? value)
        => value is UScriptArray array ? array.Properties.Select(property => property.GenericValue is FName name ? name.Text : property.GenericValue as string)
            .Select(Text).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];

    // The installed NarrativeNPC parent selects only these arrays for its grant handler.
    // IdleLines are not passed to TryUnlockCollectiblesAndWorldFlags by that state switch.
    internal static IReadOnlyList<RecipeSpecialGrant> ConversationGrants(string id, FStructFallback row)
    {
        var result = new List<RecipeSpecialGrant>();
        var label = Text(Field(row, "NPCName")) ?? id;
        foreach (var group in DialogueGroups)
        {
            foreach (var (line, index) in IndexedStructures(Field(row, group)))
            {
                foreach (var recipe in RowIds(Field(line, "RecipesToUnlock")))
                    result.Add(new(recipe, RecipeSpecialGrantKind.Conversation, id, label, Conversations,
                        SectionKind: group, SectionIndex: index));
            }
        }
        return result;
    }

    internal static IReadOnlyList<RecipeSpecialGrant> CompendiumGrants(string id, FStructFallback row)
    {
        var result = new List<RecipeSpecialGrant>();
        var label = Text(Field(row, "Subtitle")) ?? Text(Field(row, "Title")) ?? id;
        if (Field(row, "bHasKillRequirementSection") is true && Structure(Field(row, "KillRequirementSection")) is { } kills)
        {
            var count = Field(kills, "RequiredCount") switch { int value when value >= 0 => (int?)value, byte value => value, _ => null };
            foreach (var recipe in RowIds(Field(kills, "RecipesToUnlock")))
                result.Add(new(recipe, RecipeSpecialGrantKind.Compendium, id, label, Compendium, "Kill", count));
        }
        foreach (var (section, index) in IndexedStructures(Field(row, "Sections")))
        {
            var kind = Text(Field(section, "UnlockRequirement"));
            if (kind is not null && kind.LastIndexOf(':') is var separator && separator >= 0) kind = kind[(separator + 1)..];
            foreach (var recipe in RowIds(Field(section, "RecipesToUnlock")))
                result.Add(new(recipe, RecipeSpecialGrantKind.Compendium, id, label, Compendium,
                    SectionKind: kind, SectionIndex: index));
        }
        return result;
    }

    internal static IReadOnlyList<string> RowIds(object? value)
        => Structures(value).Select(row => Text(Field(row, "RowName"))).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static object? Field(IPropertyHolder row, string name)
        => row.Properties.FirstOrDefault(property => property.Name.Text == name)?.Tag?.GenericValue;
    private static FStructFallback? Structure(object? value)
        => value is FScriptStruct script ? script.StructType as FStructFallback : value as FStructFallback;
    private static IEnumerable<FStructFallback> Structures(object? value)
        => IndexedStructures(value).Select(entry => entry.Row);
    private static IEnumerable<(FStructFallback Row, int Index)> IndexedStructures(object? value)
    {
        if (value is not UScriptArray array) yield break;
        for (var index = 0; index < array.Properties.Count; index++)
            if (Structure(array.Properties[index].GenericValue) is { } row) yield return (row, index);
    }
    private static string? Text(object? value)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase) ? null : text;
    }
}
