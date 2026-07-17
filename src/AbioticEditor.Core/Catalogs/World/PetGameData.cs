using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One tameable companion as the game itself defines it: a <c>DT_Pets</c> row joined with
/// its <c>DT_NPCList</c> row and its <c>Item.Pet</c> inventory row.
/// </summary>
/// <param name="PetRow">The <c>DT_Pets</c> row name (e.g. <c>Lamogi_Speedy</c>).</param>
/// <param name="ItemRow">The <c>ItemTable_Global</c> row for the carried form, or null when
/// no pet-tagged item matched (the game's pet rows and item rows share names, except the
/// legacy base skink pair <c>pet_skink</c>/<c>biocannon</c> which is bridged by display
/// name).</param>
/// <param name="DisplayName">In-game name from <c>DT_NPCList</c> (e.g. "Speedogi").</param>
/// <param name="ClassPath">Full soft-object class path from <c>NPCSpawnClass</c> - the value
/// found in a world pet's <c>NPCClass_</c>.</param>
/// <param name="FamilyRow">Root of the row's <c>DefaultParent</c> chain (<c>pest</c>,
/// <c>Peccary</c>, <c>Skink</c>, <c>WinterSprite</c>, ...) - the game's own family
/// grouping.</param>
/// <param name="FamilyName">Display name of the family root row (e.g. "Lamogi").</param>
/// <param name="Category">The editor family bucket the root maps to.</param>
/// <param name="IsWeaponForm">True for the crafted BioCannon-style weapon forms.</param>
/// <param name="CompendiumRow">The bestiary entry unlocked by petting - also names the
/// portrait texture (<c>T_Compendium_&lt;row&gt;</c>).</param>
/// <param name="MutationTargets">Row names this pet can mutate into (feeds the upgrade
/// picker with the game's real mutation graph).</param>
public sealed record PetDefinition(
    string PetRow,
    string? ItemRow,
    string DisplayName,
    string? ClassPath,
    string FamilyRow,
    string FamilyName,
    PetCategory Category,
    bool IsWeaponForm,
    string? CompendiumRow,
    IReadOnlyList<string> MutationTargets);

/// <summary>
/// The game's own companion data, read live from the mounted paks: <c>DT_Pets</c> is the
/// authoritative "what is a pet" list (27 rows as of the anniversary update), joined by row
/// name with <c>DT_NPCList</c> (display name + spawn class) and with the <c>Item.Pet</c>-tagged
/// rows of <c>ItemTable_Global</c> (the carried item forms). Because this reads the tables the
/// game reads, new companions added by future updates (or mods that merge rows into these
/// tables) appear with no editor change.
/// </summary>
public sealed class PetGameData
{
    private const string PetsTable = "AbioticFactor/Content/Blueprints/DataTables/DT_Pets";
    private const string NpcListTable = "AbioticFactor/Content/Blueprints/DataTables/DT_NPCList";
    private const string ItemTable = "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";

    private readonly Dictionary<string, PetDefinition> _byPetRow;
    private readonly Dictionary<string, PetDefinition> _byItemRow;
    private readonly Dictionary<string, PetDefinition> _byShortClass;

    private PetGameData(List<PetDefinition> definitions)
    {
        Definitions = definitions;
        _byPetRow = new Dictionary<string, PetDefinition>(StringComparer.OrdinalIgnoreCase);
        _byItemRow = new Dictionary<string, PetDefinition>(StringComparer.OrdinalIgnoreCase);
        _byShortClass = new Dictionary<string, PetDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in definitions)
        {
            _byPetRow[d.PetRow] = d;
            if (d.ItemRow is not null) _byItemRow[d.ItemRow] = d;
            var shortClass = PetCatalog.ShortOf(d.ClassPath);
            if (shortClass.Length > 0 && !_byShortClass.ContainsKey(shortClass)) _byShortClass[shortClass] = d;
        }
    }

    /// <summary>All pets the game defines, in table order.</summary>
    public IReadOnlyList<PetDefinition> Definitions { get; }

    /// <summary>Lookup by <c>DT_Pets</c> row name, or null.</summary>
    public PetDefinition? ByPetRow(string? row)
        => row is not null && _byPetRow.TryGetValue(row, out var d) ? d : null;

    /// <summary>Lookup by inventory item row, or null.</summary>
    public PetDefinition? ByItemRow(string? row)
        => row is not null && _byItemRow.TryGetValue(row, out var d) ? d : null;

    /// <summary>Lookup by NPC class path or short class name, or null.</summary>
    public PetDefinition? ByClass(string? classOrShort)
    {
        var shortClass = PetCatalog.ShortOf(classOrShort);
        return shortClass.Length > 0 && _byShortClass.TryGetValue(shortClass, out var d) ? d : null;
    }

    /// <summary>
    /// Reads the pet tables from the mounted paks. Null when the provider is absent, has no
    /// mappings, or the tables cannot be read (callers fall back to the curated catalog -
    /// the graceful-degradation rule).
    /// </summary>
    public static PetGameData? TryLoadFrom(GameAssetProvider? provider)
    {
        if (provider is null || !provider.HasMappings) return null;
        try
        {
            var data = Load(provider);
            return data.Definitions.Count > 0 ? data : null;
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("PetGameData", $"Pet table load failed; using curated catalog. {ex.Message}");
            return null;
        }
    }

    private static PetGameData Load(GameAssetProvider provider)
    {
        // ----- DT_NPCList: row -> (display name, spawn class) -----
        var npcNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var npcClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var npcList = provider.TryLoadDataTable(NpcListTable);
        ParseNpcRows(npcList, npcNames, npcClasses);
        foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, npcList?.RowStructName, new[] { NpcListTable }))
        {
            ParseNpcRows(dt, npcNames, npcClasses);
        }

        // ----- DT_Pets: the authoritative pet rows -----
        var parents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var compendium = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var mutations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var petRows = new List<string>();
        var pets = provider.TryLoadDataTable(PetsTable);
        ParsePetRows(pets, petRows, parents, compendium, mutations);
        foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, pets?.RowStructName, new[] { PetsTable }))
        {
            ParsePetRows(dt, petRows, parents, compendium, mutations);
        }

        // ----- ItemTable_Global: the Item.Pet carried forms -----
        var petItems = LoadPetItems(provider);

        var definitions = new List<PetDefinition>(petRows.Count);
        foreach (var row in petRows)
        {
            var familyRow = RootOf(row, parents);
            var displayName = npcNames.TryGetValue(row, out var dn) && !string.IsNullOrWhiteSpace(dn)
                ? dn
                : row.Replace('_', ' ');
            var familyName = npcNames.TryGetValue(familyRow, out var fn) && !string.IsNullOrWhiteSpace(fn)
                ? fn
                : familyRow.Replace('_', ' ');

            // Item row: same-name row first; the legacy skink pair is bridged below.
            PetItemRow? item = petItems.FirstOrDefault(i => string.Equals(i.Row, row, StringComparison.OrdinalIgnoreCase));

            definitions.Add(new PetDefinition(
                PetRow: row,
                ItemRow: item?.Row,
                DisplayName: displayName,
                ClassPath: npcClasses.TryGetValue(row, out var cls) ? cls : null,
                FamilyRow: familyRow,
                FamilyName: familyName,
                Category: CategoryFor(familyRow, familyName),
                IsWeaponForm: item?.IsWeapon ?? row.EndsWith("_Crafted", StringComparison.OrdinalIgnoreCase),
                CompendiumRow: compendium.TryGetValue(row, out var comp) ? comp : null,
                MutationTargets: mutations.TryGetValue(row, out var m) ? m : Array.Empty<string>()));
        }

        // Bridge leftover pet items (rows that matched no DT_Pets row, e.g. the legacy
        // pet_skink / biocannon pair) by display name; weapon forms pair with the
        // "_Crafted" pet row of the same name.
        var claimed = new HashSet<string>(definitions.Where(d => d.ItemRow is not null).Select(d => d.ItemRow!), StringComparer.OrdinalIgnoreCase);
        foreach (var item in petItems)
        {
            if (claimed.Contains(item.Row)) continue;
            var key = NormalizeName(item.Name);
            for (var i = 0; i < definitions.Count; i++)
            {
                var d = definitions[i];
                if (d.ItemRow is not null) continue;
                if (NormalizeName(d.DisplayName) != key) continue;
                var isCrafted = d.PetRow.EndsWith("_Crafted", StringComparison.OrdinalIgnoreCase);
                if (isCrafted != item.IsWeapon) continue;
                definitions[i] = d with { ItemRow = item.Row, IsWeaponForm = item.IsWeapon };
                claimed.Add(item.Row);
                break;
            }
        }

        // Any pet-tagged item that still matched nothing (a future or modded pet whose
        // DT_Pets row we could not pair) is surfaced as an item-only definition rather
        // than dropped, so the carried-pet reader still detects it.
        foreach (var item in petItems)
        {
            if (claimed.Contains(item.Row)) continue;
            definitions.Add(new PetDefinition(
                PetRow: item.Row,
                ItemRow: item.Row,
                DisplayName: StripWeaponSuffix(item.Name ?? item.Row),
                ClassPath: null,
                FamilyRow: item.Row,
                FamilyName: StripWeaponSuffix(item.Name ?? item.Row),
                Category: PetCategory.Other,
                IsWeaponForm: item.IsWeapon,
                CompendiumRow: null,
                MutationTargets: Array.Empty<string>()));
        }

        return new PetGameData(definitions);
    }

    private sealed record PetItemRow(string Row, string? Name, bool IsWeapon);

    private static List<PetItemRow> LoadPetItems(GameAssetProvider provider)
    {
        var result = new List<PetItemRow>();
        var dt = provider.TryLoadDataTable(ItemTable);
        if (dt is null) return result;
        foreach (var kv in dt.RowMap)
        {
            string? tags = null, name = null;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                if (n.StartsWith("GameplayTags_", StringComparison.Ordinal))
                {
                    tags = p.Tag?.GenericValue?.ToString();
                }
                else if (n.StartsWith("ItemName_", StringComparison.Ordinal))
                {
                    name = p.Tag?.GenericValue?.ToString();
                }
            }
            if (tags is null || !tags.Contains("Item.Pet", StringComparison.OrdinalIgnoreCase)) continue;
            var isWeapon = tags.Contains("Item.Weapon", StringComparison.OrdinalIgnoreCase)
                || (name?.Contains("(Weapon)", StringComparison.OrdinalIgnoreCase) ?? false);
            result.Add(new PetItemRow(kv.Key.Text, name, isWeapon));
        }
        return result;
    }

    private static void ParseNpcRows(UDataTable? dt, Dictionary<string, string> names, Dictionary<string, string> classes)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            var row = kv.Key.Text;
            if (string.IsNullOrEmpty(row)) continue;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                if (n.StartsWith("DisplayName_", StringComparison.Ordinal))
                {
                    var v = p.Tag?.GenericValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(v) && !names.ContainsKey(row)) names[row] = v;
                }
                else if (n.StartsWith("NPCSpawnClass_", StringComparison.Ordinal))
                {
                    var v = p.Tag?.GenericValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(v) && !classes.ContainsKey(row)) classes[row] = v;
                }
            }
        }
    }

    private static void ParsePetRows(
        UDataTable? dt,
        List<string> rows,
        Dictionary<string, string?> parents,
        Dictionary<string, string?> compendium,
        Dictionary<string, List<string>> mutations)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            var row = kv.Key.Text;
            if (string.IsNullOrEmpty(row) || parents.ContainsKey(row)) continue;
            rows.Add(row);
            parents[row] = null;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                if (n.StartsWith("DefaultParent_", StringComparison.Ordinal))
                {
                    parents[row] = RowNameOf(p.Tag?.GenericValue);
                }
                else if (n.StartsWith("PettingCompendiumUnlock_", StringComparison.Ordinal))
                {
                    compendium[row] = RowNameOf(p.Tag?.GenericValue);
                }
                else if (n.StartsWith("Mutations_", StringComparison.Ordinal)
                         && p.Tag?.GenericValue is UScriptArray arr)
                {
                    var targets = new List<string>();
                    foreach (var el in arr.Properties)
                    {
                        var target = FieldOf(el.GenericValue, "MutationTarget_") is { } t ? RowNameOf(t) : null;
                        if (!string.IsNullOrEmpty(target)) targets.Add(target!);
                    }
                    if (targets.Count > 0) mutations[row] = targets;
                }
            }
        }
    }

    /// <summary>The <c>RowName</c> inside a DataTable-handle struct, or null / "None"-as-null.</summary>
    private static string? RowNameOf(object? value)
    {
        var v = FieldOf(value, "RowName")?.ToString();
        return string.IsNullOrEmpty(v) || v == "None" ? null : v;
    }

    private static object? FieldOf(object? value, string prefix)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        foreach (var p in sf.Properties)
        {
            if (p.Name.Text.StartsWith(prefix, StringComparison.Ordinal)) return p.Tag?.GenericValue;
        }
        return null;
    }

    private static string RootOf(string row, Dictionary<string, string?> parents)
    {
        var current = row;
        for (var hops = 0; hops < 16; hops++)
        {
            if (!parents.TryGetValue(current, out var parent) || parent is null
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
            current = parent;
        }
        return current;
    }

    private static PetCategory CategoryFor(string familyRow, string familyName)
    {
        foreach (var candidate in new[] { familyRow, familyName })
        {
            var c = PetCatalog.CategorizeToken(candidate);
            if (c is not null) return c.Value;
        }
        return PetCategory.Other;
    }

    private static string StripWeaponSuffix(string name)
        => name.Replace("(Weapon)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>Comparison key: lowercase alphanumerics, "(Weapon)" suffix removed.</summary>
    private static string NormalizeName(string? name)
        => new((StripWeaponSuffix(name ?? string.Empty))
            .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
