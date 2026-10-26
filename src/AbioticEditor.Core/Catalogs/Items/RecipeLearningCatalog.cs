using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Items;

/// <summary>One historically discovered item type, or one member of an explicit substitute group. Counts are crafting costs, not discovery requirements.</summary>
public sealed record RecipeLearningInput(string RowId, string? TablePath, string? DisplayName,
    IReadOnlyList<string> AlternativeItemIds, bool IsSubstitute, bool IsResolved);

/// <summary>Learning metadata from one recipe row. Null means the current data does not establish that rule.</summary>
public sealed record RecipeLearningDefinition(string RecipeId, string TablePath, bool? PickupDiscoveryEnabled,
    int? ResearchDifficulty, IReadOnlyList<RecipeLearningInput> DiscoveryInputs,
    IReadOnlyList<string> LinkedRecipeIds, string? OutputItemId)
{
    /// <summary>Output pickup has a separate passive gate. It must not be inferred from crafting ingredients.</summary>
    public bool? OutputPickupDiscoveryEnabled { get; init; }
    public bool DiscoveryInputsKnown { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool? RequiresResearch => ResearchDifficulty switch { 0 => false, 1 or 2 => true, _ => null };
}

/// <summary>A DT_SkillRecipes grant, resolved to DT_Skills identity rather than a save-array or enum ordinal.</summary>
public sealed record RecipeSkillGrant(string RecipeId, string SkillId, int Level);
public sealed record RecipeLearningLinkReference(string SourceRecipeId, string TargetRecipeId, string? TablePath);
public sealed record RecipeLearningRejectedLink(RecipeLearningLinkReference Link, string Reason);

/// <summary>Versioned, serializable evidence. Older snapshots lacking this object retain unknown learning rules.</summary>
public sealed record RecipeLearningCatalogData(int SchemaVersion, IReadOnlyList<RecipeLearningDefinition> Recipes,
    IReadOnlyList<string> StartingRecipeIds, IReadOnlyList<string> StartingResearchRecipeIds,
    IReadOnlyList<RecipeSkillGrant> SkillGrants)
{
    public bool StartingRecipesKnown { get; init; }
    public bool StartingResearchKnown { get; init; }
    public IReadOnlyList<RecipeSpecialGrant> SpecialGrants { get; init; } = [];
    public IReadOnlyList<RecipeLearningRejectedLink> RejectedLinks { get; init; } = [];
}

/// <summary>
/// Extracts the learning fields consumed by Abiotic_CharacterProgressionComponent. The installed
/// component's RecipeTable selects the pickup algorithm's table; sharing a row struct does not
/// make another table participate. Script evidence is recorded in the recipe-learning research.
/// </summary>
public static class RecipeLearningCatalog
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Optional imported metadata must be structurally usable before any player analysis.</summary>
    public static RecipeLearningCatalogData? Validated(RecipeLearningCatalogData? data)
    {
        if (data is null || data.SchemaVersion != CurrentSchemaVersion || data.Recipes is null
            || data.StartingRecipeIds is null || data.StartingResearchRecipeIds is null || data.SkillGrants is null
            || data.SpecialGrants is null || data.RejectedLinks is null) return null;
        if (data.Recipes.Any(recipe => recipe is null || string.IsNullOrWhiteSpace(recipe.RecipeId)
            || recipe.DiscoveryInputs is null || recipe.LinkedRecipeIds is null
            || recipe.LinkedRecipeIds.Any(string.IsNullOrWhiteSpace)
            || recipe.DiscoveryInputs.Any(input => input is null || string.IsNullOrWhiteSpace(input.RowId)
                || input.AlternativeItemIds is null || input.AlternativeItemIds.Any(string.IsNullOrWhiteSpace)))
            || data.StartingRecipeIds.Any(string.IsNullOrWhiteSpace) || data.StartingResearchRecipeIds.Any(string.IsNullOrWhiteSpace)
            || data.SkillGrants.Any(grant => grant is null || string.IsNullOrWhiteSpace(grant.RecipeId) || string.IsNullOrWhiteSpace(grant.SkillId))
            || data.SpecialGrants.Any(grant => grant is null || string.IsNullOrWhiteSpace(grant.RecipeId) || string.IsNullOrWhiteSpace(grant.SourceId))) return null;
        return data;
    }
    private const string ProgressionPackage = "AbioticFactor/Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent";
    private const string CraftingTable = "AbioticFactor/Content/Blueprints/DataTables/DT_Recipes";
    private const string SoupTable = "AbioticFactor/Content/Blueprints/DataTables/DT_SoupRecipes";
    private const string ChemistryTable = "AbioticFactor/Content/Blueprints/DataTables/DT_ChemistryRecipes";
    private const string SubstituteTable = "AbioticFactor/Content/Blueprints/Items/ItemTable_RecipeSubstitutes";
    private const string SkillRecipesTable = "AbioticFactor/Content/Blueprints/DataTables/Customization/DT_SkillRecipes";
    private const string SkillsTable = "AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills";

    public static RecipeLearningCatalogData? LoadFrom(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.HasMappings) return null;
        try
        {
            var defaults = provider.LoadPackageInternal(ProgressionPackage).GetExports()
                .FirstOrDefault(export => export.Name == "Default__Abiotic_CharacterProgressionComponent_C");
            var configuredTable = defaults is null ? null : ObjectPath(Value(defaults.Properties, "RecipeTable"));
            // 1.5's pickup loop calls GetAllRecipeRowHandles, then GetRecipeRow.
            // Those typed handles select DT_Recipes, including official mod row overlays.
            if (configuredTable is null && provider.TryLoadDataTable(CraftingTable)?.RowStructName == "AbioticRecipe")
                configuredTable = CraftingTable;
            var groups = ReadGroups(provider);
            var definitions = new List<RecipeLearningDefinition>();
            var links = new List<RecipeLearningLinkReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tables = new[] { CraftingTable, SoupTable, ChemistryTable }
                .Concat(configuredTable is null ? [] : new[] { configuredTable })
                .Concat(RecipeCatalog.DiscoverExtraTables(provider)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in tables)
            {
                var table = provider.TryLoadDataTable(path);
                if (table is null) continue;
                foreach (var (name, row) in table.RowMap)
                    if (Usable(name.Text) && seen.Add(name.Text))
                    {
                        definitions.Add(ReadDefinition(name.Text, row, path,
                            string.Equals(path, configuredTable, StringComparison.OrdinalIgnoreCase), groups));
                        links.AddRange(ReadLinks(name.Text, row));
                    }
            }
            if (definitions.Count == 0) return null;
            var starting = defaults is null ? null : (Value(defaults.Properties, "RecipesUnlockedArray") as UScriptArray ?? Value(defaults.Properties, "RecipesUnlocked") as UScriptArray);
            var research = defaults is null ? null : Value(defaults.Properties, "RecipesRequiringResearch_Array") as UScriptArray;
            var validated = ValidateLinks(definitions, links, out var rejected);
            return new(CurrentSchemaVersion, validated, Names(starting), Names(research), ReadSkillGrants(provider))
            {
                StartingRecipesKnown = starting is not null,
                StartingResearchKnown = research is not null,
                RejectedLinks = rejected,
            };
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("RecipeLearning", "Learning tables could not be read; their requirements remain unknown.", ex);
            return null;
        }
    }

    internal static RecipeLearningDefinition ReadDefinition(string id, FStructFallback row, string tablePath,
        bool isPickupTable, IReadOnlyDictionary<string, RecipeLearningInput> groups)
    {
        var flag = Value(row.Properties, "NotUnlockableByPickup_");
        var research = Struct(Value(row.Properties, "ResearchData_"));
        var inputs = new List<RecipeLearningInput>();
        var ingredients = Value(row.Properties, "RecipeItems_") as UScriptArray;
        if (ingredients is not null)
            foreach (var element in ingredients.Properties)
            {
                var ingredient = Struct(element.GenericValue);
                var handle = ingredient is null ? null : Struct(Value(ingredient.Properties, "Item_"));
                var name = handle is null ? null : Value(handle.Properties, "RowName")?.ToString();
                var table = handle is null ? null : ObjectPath(Value(handle.Properties, "DataTable"));
                table ??= handle is null ? null : Value(handle.Properties, "Table")?.ToString() switch
                {
                    "EAnyItemTable::GlobalItem" => "AbioticFactor/Content/Blueprints/Items/ItemTable_Global",
                    "EAnyItemTable::RecipeSubstitute" => SubstituteTable,
                    _ => null,
                };
                if (!Usable(name))
                {
                    inputs.Add(new(name ?? string.Empty, table, null, [], false, false));
                    continue;
                }
                var isSubstitute = string.Equals(table, SubstituteTable, StringComparison.OrdinalIgnoreCase);
                inputs.Add(isSubstitute && groups.TryGetValue(name!, out var group) ? group
                    : new(name!, table, null, isSubstitute ? [] : new[] { name! }, isSubstitute, table is not null && !isSubstitute));
            }
        var output = Struct(Value(row.Properties, "ItemToCreate_"));
        var outputId = output is null ? null : Value(output.Properties, "RowName")?.ToString();
        return new(id, tablePath, isPickupTable && flag is bool blocked ? !blocked : null,
            research is null ? null : EnumNumber(Value(research.Properties, "MinigameDifficulty_")),
            inputs.AsReadOnly(), [], Usable(outputId) ? outputId : null)
        {
            DiscoveryInputsKnown = ingredients is not null,
        };
    }

    private static IEnumerable<RecipeLearningLinkReference> ReadLinks(string id, FStructFallback row)
    {
        if (Value(row.Properties, "LinkedRecipesToUnlock_") is not UScriptArray array) yield break;
        foreach (var element in array.Properties)
        {
            var handle = Struct(element.GenericValue);
            var target = handle is null ? null : Value(handle.Properties, "RowName")?.ToString();
            if (Usable(target)) yield return new(id, target!, ObjectPath(Value(handle!.Properties, "DataTable"))
                ?? (row.Properties.Any(p => p.Name.Text == "LinkedRecipesToUnlock") ? CraftingTable : null));
        }
    }

    internal static IReadOnlyList<RecipeLearningDefinition> ValidateLinks(IReadOnlyList<RecipeLearningDefinition> definitions,
        IEnumerable<RecipeLearningLinkReference> references, out IReadOnlyList<RecipeLearningRejectedLink> rejected)
    {
        var byId = new Dictionary<string, RecipeLearningDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions) byId.TryAdd(definition.RecipeId, definition);
        var tables = definitions.Select(definition => definition.TablePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var links = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<RecipeLearningRejectedLink>();
        foreach (var reference in references)
        {
            var reason = !byId.ContainsKey(reference.SourceRecipeId) ? "source-not-defined"
                : reference.TablePath is null || !tables.Contains(reference.TablePath) ? "target-table-not-loaded"
                : !byId.TryGetValue(reference.TargetRecipeId, out var target) ? "target-not-defined"
                : !target.TablePath.Equals(reference.TablePath, StringComparison.OrdinalIgnoreCase) ? "target-table-mismatch" : null;
            if (reason is not null) { invalid.Add(new(reference, reason)); continue; }
            if (!links.TryGetValue(reference.SourceRecipeId, out var targets)) links[reference.SourceRecipeId] = targets = [];
            if (!targets.Contains(reference.TargetRecipeId, StringComparer.OrdinalIgnoreCase)) targets.Add(reference.TargetRecipeId);
        }
        rejected = invalid.AsReadOnly();
        return definitions.Select(definition => definition with
        {
            LinkedRecipeIds = links.TryGetValue(definition.RecipeId, out var targets) ? targets.AsReadOnly() : [],
        }).ToArray();
    }

    private static Dictionary<string, RecipeLearningInput> ReadGroups(GameAssetProvider provider)
    {
        var groups = new Dictionary<string, RecipeLearningInput>(StringComparer.OrdinalIgnoreCase);
        var table = provider.TryLoadDataTable(SubstituteTable);
        if (table is null) return groups;
        foreach (var (name, row) in table.RowMap)
        {
            var alternatives = Value(row.Properties, "ItemsOfType_") as UScriptArray;
            var items = HandleNames(alternatives);
            groups.TryAdd(name.Text, new(name.Text, SubstituteTable,
                Value(row.Properties, "ItemTypeName_")?.ToString(), items, true, alternatives is not null && items.Length > 0));
        }
        return groups;
    }

    private static IReadOnlyList<RecipeSkillGrant> ReadSkillGrants(GameAssetProvider provider)
    {
        var grants = new List<RecipeSkillGrant>();
        var recipes = provider.TryLoadDataTable(SkillRecipesTable);
        var skills = provider.TryLoadDataTable(SkillsTable);
        if (recipes is null || skills is null) return grants;
        var blueprintEnum = provider.UseFileProvider(files => files.TryLoadPackage("AbioticFactor/Content/Blueprints/Data/E_CharacterSkills", out var package)
            ? package.GetExports().OfType<UEnum>().FirstOrDefault() : null);
        var nativeEnum = provider.UseFileProvider(files => files.MappingsContainer?.MappingsForGame?.Enums
            .GetValueOrDefault("ECharacterSkills"));
        var names = blueprintEnum?.Names.ToDictionary(pair => pair.Item1.Text, pair => pair.Item2, StringComparer.OrdinalIgnoreCase);
        var byNativeName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, row) in skills.RowMap)
        {
            var type = Value(row.Properties, "SkillType")?.ToString();
            if (Usable(type)) byNativeName.TryAdd(type!.Split("::", StringSplitOptions.None)[^1], name.Text);
        }
        foreach (var (_, row) in recipes.RowMap)
        {
            var recipeValue = Value(row.Properties, "PerkBuffID_");
            var recipeId = Struct(recipeValue) is { } recipeHandle ? Value(recipeHandle.Properties, "RowName")?.ToString() : recipeValue?.ToString();
            var skill = Value(row.Properties, "Skill_")?.ToString();
            var level = Value(row.Properties, "LevelGainedAt_") as int?;
            if (!Usable(recipeId) || skill is null || level is not int requiredLevel || requiredLevel <= 0) continue;
            var nativeName = skill;
            if (!skill.StartsWith("ECharacterSkills::", StringComparison.Ordinal)
                && (names is null || nativeEnum is null || !names.TryGetValue(skill, out var number) || !nativeEnum.TryGetValue(number, out nativeName))) continue;
            if (!byNativeName.TryGetValue(nativeName.Split("::", StringSplitOptions.None)[^1], out var skillId)) continue;
            var grant = new RecipeSkillGrant(recipeId!, skillId, requiredLevel);
            if (!grants.Any(existing => existing.Level == grant.Level && existing.RecipeId.Equals(grant.RecipeId, StringComparison.OrdinalIgnoreCase)
                && existing.SkillId.Equals(grant.SkillId, StringComparison.OrdinalIgnoreCase))) grants.Add(grant);
        }
        return grants.AsReadOnly();
    }

    private static string[] Names(UScriptArray? array)
        => array?.Properties.Select(property => property.GenericValue?.ToString()).Where(Usable).Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    private static string[] HandleNames(UScriptArray? array)
        => array?.Properties.Select(property => Struct(property.GenericValue))
            .Select(handle => handle is null ? null : Value(handle.Properties, "RowName")?.ToString())
            .Where(Usable).Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    private static object? Value(IEnumerable<FPropertyTag> properties, string prefix)
        => properties.FirstOrDefault(property => property.Name.Text.MatchesUnrealProperty(prefix))?.Tag?.GenericValue;
    private static FStructFallback? Struct(object? value) => (value is FScriptStruct script ? script.StructType : value) as FStructFallback;
    private static bool Usable(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Equals("None", StringComparison.OrdinalIgnoreCase);
    private static string? ObjectPath(object? value)
    {
        if (value is not FPackageIndex index || index.IsNull || index.ResolvedObject is not { } resolved) return null;
        var path = resolved.GetPathName().Split('.')[0];
        return path.StartsWith("/Game/", StringComparison.Ordinal) ? "AbioticFactor/Content/" + path[6..] : path.TrimStart('/');
    }
    private static int? EnumNumber(object? value)
    {
        if (value is byte number) return number;
        if (value is int integer) return integer;
        var text = value?.ToString();
        if (text is null) return null;
        if (text == "EMinigameDifficulty::Easy") return 0;
        if (text == "EMinigameDifficulty::Medium") return 1;
        if (text == "EMinigameDifficulty::Hard") return 2;
        const string prefix = "E_MinigameDifficulty::NewEnumerator";
        return text.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(text.AsSpan(prefix.Length), out var result) ? result : null;
    }
}
