namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The item a player recognises for a garden crop row, shared by the world garden list and the
/// 3D view's garden card so both show the same picture and name.
/// </summary>
public static class GardenCropItems
{
    /// <summary>
    /// The row the crop field actually stores (e.g. <c>Plant_Corn</c>) is a planting-state row
    /// from <c>ItemTable_Global</c> - real, but not the same row as the actual seed/produce item
    /// a player would recognise or that has a picture worth showing. Checked directly against
    /// the bundled registry: <c>Plant_Corn</c>'s own entry has a blank <c>DisplayName</c> and an
    /// <c>icon_missingitem</c> placeholder <c>IconAssetPath</c> - it is not meant to be browsed
    /// or pictured on its own, which is exactly why the crop image never showed at all . Maps each of
    /// <c>DeployedCareFeatures.cs</c>'s own <c>CropRows</c> to the real, pictured item id a player
    /// would recognise: the plantable seed for most crops, or the harvested food itself for the
    /// two root vegetables the game has no separate seed item for (Potato, Carrot) - verified
    /// against the bundled registry row by row, not guessed from a naming pattern (the pattern is
    /// not consistent enough to guess: "Plant_Egg" maps to "seed_eggplant", not "seed_egg";
    /// "Plant_SpaceLettuce" drops "Space" entirely; "Plant_Super_Tomato" drops its underscore).
    /// A crop added to CropRows in the future without a matching entry here still shows correctly
    /// as its own raw row id, which then resolves however IsBrowsable already resolves any other
    /// unrecognised item id today.
    /// </summary>
    private static readonly Dictionary<string, string> PicturedItems = new(StringComparer.Ordinal)
    {
        ["Plant_Corn"] = "seed_corn",
        ["Plant_Tomato"] = "seed_tomato",
        ["Plant_Wheat"] = "seed_wheat",
        ["Plant_Greyeb"] = "seed_greyeb",
        ["Plant_Nyxshade"] = "seed_nyxshade",
        ["Plant_Super_Tomato"] = "seed_supertomato",
        ["Plant_RopePlant"] = "seed_ropeplant",
        ["Plant_Egg"] = "seed_eggplant",
        ["Plant_SpaceLettuce"] = "seed_lettuce",
        ["Plant_VinePlant"] = "seed_vine",
        ["Plant_Potato"] = "food_potato",
        ["Plant_Rice"] = "seed_rice",
        ["Plant_Antelight"] = "seed_antelight",
        ["Plant_Antelight_GRN"] = "seed_antelight_GRN",
        ["Plant_Antelight_pink"] = "seed_antelight_pink",
        ["Plant_Antelight_red"] = "seed_antelight_red",
        ["Plant_Antelight_orange"] = "seed_antelight_orange",
        ["Plant_Antelight_blue"] = "seed_antelight_blue",
        ["Plant_Antelight_RGB"] = "seed_antelight_RGB",
        ["Plant_Antelight_space"] = "seed_antelight_space",
        ["Plant_Pumpkin"] = "seed_pumpkin",
        ["Plant_GlowTulip"] = "seed_glowtulip",
        ["Plant_Shadowberry"] = "seed_shadowberry",
        ["Plant_Carrot"] = "food_carrot",
    };

    /// <summary>The pictured item id for a crop row, or the row itself when there is no better one.</summary>
    public static string PicturedItemFor(string row) => PicturedItems.GetValueOrDefault(row, row);
}
