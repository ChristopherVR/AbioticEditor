using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Items;

/// <summary>One row from the game's <c>DT_TextureVariants</c> table.</summary>
public sealed record ItemVariantDefinition(
    string RowName,
    string DisplayName,
    string? Description,
    string? IconAssetPath);

/// <summary>
/// Visual appearances that an inventory item's <c>TextureVariantRow</c> can select. The game
/// table is flat and carries no compatibility metadata, so reviewed item-to-row families live
/// here while <see cref="Entries"/> retains every row for advanced/manual selection.
/// </summary>
public sealed class ItemVariantCatalog
{
    private const string TablePath =
        "AbioticFactor/Content/Blueprints/DataTables/Customization/DT_TextureVariants";

    private readonly IReadOnlyDictionary<string, ItemVariantDefinition> _byRow;

    private ItemVariantCatalog(IReadOnlyDictionary<string, ItemVariantDefinition> byRow)
    {
        _byRow = byRow;
        Entries = byRow.Values
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RowName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Every row in the installed or bundled game table, including fixed item defaults.</summary>
    public IReadOnlyList<ItemVariantDefinition> Entries { get; }

    public ItemVariantDefinition? Find(string? rowName)
        => rowName is not null && _byRow.TryGetValue(rowName, out var entry) ? entry : null;

    /// <summary>
    /// Reviewed choices for an item. This intentionally excludes speculative painting/frame
    /// mappings and weapon upgrade appearances that belong to separate item IDs.
    /// </summary>
    public IReadOnlyList<ItemVariantDefinition> ForItem(string? itemId)
    {
        if (itemId is null || !CuratedRows.TryGetValue(itemId, out var rows)) return [];
        return rows.Select(row => Find(row) ?? Placeholder(row)).ToArray();
    }

    public static ItemVariantCatalog FromRegistry(IEnumerable<ItemVariantDefinition>? entries)
    {
        var byRow = new Dictionary<string, ItemVariantDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entry.RowName)) byRow[entry.RowName] = entry;
        }
        return new ItemVariantCatalog(byRow);
    }

    /// <summary>Loads all variant rows from the mounted game data.</summary>
    public static ItemVariantCatalog LoadFrom(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.HasMappings)
            throw new GameAssetProvider.MappingsRequiredException("DT_TextureVariants");

        var table = provider.LoadPackageInternal(TablePath)
            .GetExports().OfType<UDataTable>().FirstOrDefault()
            ?? throw new InvalidDataException("DT_TextureVariants has no UDataTable export.");
        var byRow = new Dictionary<string, ItemVariantDefinition>(table.RowMap.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, row) in table.RowMap)
        {
            var rowName = key.Text;
            if (string.IsNullOrWhiteSpace(rowName)) continue;
            var displayName = Read(row, "ItemNameOverride_");
            byRow[rowName] = new ItemVariantDefinition(
                rowName,
                string.IsNullOrWhiteSpace(displayName) ? FriendlyRowName(rowName) : displayName,
                NullIfEmpty(Read(row, "ItemDescriptionOverride_")),
                NullIfEmpty(Read(row, "IconOverride_")));
        }
        return new ItemVariantCatalog(byRow);
    }

    private static string? Read(FStructFallback row, string prefix)
        => row.Properties.FirstOrDefault(property =>
                property.Name.Text.StartsWith(prefix, StringComparison.Ordinal))
            ?.Tag?.GenericValue?.ToString();

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ItemVariantDefinition Placeholder(string rowName)
        => new(rowName, FriendlyRowName(rowName), null, null);

    private static string FriendlyRowName(string rowName)
    {
        var words = rowName.Replace('_', ' ').Trim();
        return words.Length == 0 ? rowName : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static readonly Dictionary<string, string[]> CuratedRows =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["armor_hat_bonnet"] = ["bonnet_black"],
            ["armor_hat_buckethat"] = ["buckethat_green", "buckethat_navy", "buckethat_red"],
            ["armor_helmet_hardhat"] =
                ["gear_hardhat_blue", "gear_hardhat_brown", "gear_hardhat_gray", "gear_hardhat_green", "gear_hardhat_red", "gear_hardhat_white", "gear_hardhat_yellow"],
            ["armor_helmet_karate"] =
                ["gear_armor_karatehelmet_black", "gear_armor_karatehelmet_red", "gear_armor_karatehelmet_white"],
            ["armor_helmet_hydrohat"] =
                ["hydrohat_green", "hydrohat_orange", "hydrohat_red", "hydrohat_yellow"],
            ["armor_helmet_gasmask"] =
                ["labmask_shiny_blue", "labmask_shiny_dark", "labmask_shiny_gold", "labmask_shiny_green", "labmask_shiny_orange", "labmask_shiny_pink", "labmask_shiny_purple", "labmask_shiny_red"],
            ["armor_chest_puffycoat"] = ["puffycoat_gray", "puffycoat_red", "puffycoat_white"],
            ["backpack_small"] =
                ["backpack_small_gray", "backpack_small_green", "backpack_small_purple", "backpack_small_red", "backpack_small_white", "backpack_small_yellow"],
            ["Poster"] =
                ["poster_0091", "poster_Detour", "poster_TDL", "poster_USM", "poster_VOTV_01", "poster_VOTV_02", "poster_VOTV_03", "poster_VOTV_04"],
            ["ArcadeMachine"] = ["arcademachine_DETOUR", "arcademachine_TDL", "arcademachine_USM", "arcade_IS0083"],
            ["Bench_Locker"] = ["lockerroombench_brown"],
            ["Deployable_Chair_Executive_01"] = ["office_chair_executive_black"],
            ["Deployable_Couch_Office_Armchair_01"] = ["office_couch_black", "office_couch_blue"],
            ["Deployable_Couch_Office_Long_01"] = ["office_couch_blue"],
            ["fish_antefish"] = ["fish_ante_koi", "fish_ante_rare1"],
            ["fish_crab_gem"] = ["fish_crab_gem_rare1"],
            ["fish_darkwater"] = ["fish_darkwater_rare1"],
            ["fish_eel"] = ["fish_eel_goldentail", "fish_eel_rare1", "fish_eel_translucent"],
            ["fish_fog"] = ["fish_fog_rare1"],
            ["fish_ice"] = ["fish_ice_rare1"],
            ["fish_is98"] = ["fish_is98_rare1"],
            ["fish_moon"] = ["fish_moon_rare1"],
            ["fish_portal"] = ["fish_portal_rare1", "fish_portal_rare2"],
            ["fish_rad"] = ["fish_rad_rare1"],
            ["fish_reaper"] = ["fish_reaper_rare1"],
            ["fish_silk"] = ["fish_silk_rare1"],
            ["fish_umbra"] = ["fish_umbra_rare1"],
        };
}
