using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>One row of a <c>DT_Customization_*</c> table.</summary>
/// <param name="RowName">The row key stored in the save, e.g. <c>Head_M01a</c>.</param>
/// <param name="DisplayName">In-game label, e.g. "Hubert" - falls back to the row name.</param>
/// <param name="IconAssetPath">2D preview texture (CustomizationIcons/icon_*), when the row has a real one.</param>
/// <param name="ColorHex">Swatch color hex for color rows (HairColor/ShirtColor ColorA).</param>
public sealed record CustomizationOption(
    string RowName,
    string DisplayName,
    string? IconAssetPath = null,
    string? ColorHex = null)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Loads the row vocabulary of every customization DataTable so editor dropdowns can
/// offer all valid choices for each <see cref="CustomizationField"/>.
/// </summary>
public static class CustomizationCatalog
{
    /// <summary>
    /// Maps table name (<c>DT_Customization_Head</c> ...) -> its options. Tables that fail
    /// to load are omitted; without usmap mappings the result is empty.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CustomizationOption>> LoadFrom(GameAssetProvider provider)
    {
        var result = new Dictionary<string, IReadOnlyList<CustomizationOption>>(StringComparer.OrdinalIgnoreCase);
        if (!provider.HasMappings) return result;

        foreach (var tableName in CustomizationSaveFile.KnownFields.Select(f => f.TableName).Distinct())
        {
            try
            {
                var pkg = provider.LoadPackageInternal(
                    $"AbioticFactor/Content/Blueprints/DataTables/Customization/{tableName}");
                var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault();
                if (dt is null) continue;

                var options = new List<CustomizationOption>(dt.RowMap.Count);
                foreach (var kv in dt.RowMap)
                {
                    // Columns are hash-suffixed (DisplayName_63_*, Icon_46_*, ColorA_38_*)
                    // - match by prefix.
                    string? display = null, icon = null, colorHex = null;
                    foreach (var p in kv.Value.Properties)
                    {
                        var n = p.Name.Text;
                        if (n.StartsWith("DisplayName", StringComparison.Ordinal))
                        {
                            display = p.Tag?.GenericValue?.ToString();
                        }
                        else if (n.StartsWith("Icon", StringComparison.Ordinal))
                        {
                            var s = p.Tag?.GenericValue?.ToString();
                            // The engine's WhiteSquareTexture placeholder is not a usable preview.
                            if (!string.IsNullOrEmpty(s) && !s.Contains("WhiteSquare", StringComparison.OrdinalIgnoreCase))
                            {
                                icon = s;
                            }
                        }
                        else if (n.StartsWith("ColorA", StringComparison.Ordinal))
                        {
                            colorHex = p.Tag?.GenericValue switch
                            {
                                CUE4Parse.UE4.Objects.Core.Math.FLinearColor lc => lc.Hex,
                                { } v => v.ToString(),
                                _ => null,
                            };
                        }
                    }
                    options.Add(new CustomizationOption(
                        kv.Key.Text,
                        string.IsNullOrWhiteSpace(display) ? kv.Key.Text : display!,
                        icon,
                        colorHex));
                }
                result[tableName] = options;
            }
            catch
            {
                // Table missing or unreadable in this game version - skip it.
            }
        }
        return result;
    }
}
