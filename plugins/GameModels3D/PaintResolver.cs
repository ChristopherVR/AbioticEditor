using AbioticEditor.Core.WorldSaves;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// The materials a painted object wears, the way the game picks them: the class default's
/// <c>PaintedDeployableRow</c> names a row of <c>DT_PaintedDeployables</c>, whose
/// <c>Materials_&lt;Colour&gt;</c> array holds one material per slot. The game's
/// <c>Try_ApplyTextureOverrides</c> sets slot <c>i</c> of every static mesh component the actor
/// itself owns to entry <c>i</c> when that entry is set (see
/// <c>docs/reference/research/research-deployable-paint.md</c>).
/// </summary>
internal static class PaintResolver
{
    private const string TablePath = "/Game/Blueprints/DataTables/DT_PaintedDeployables.DT_PaintedDeployables";
    private const int MaxClassDepth = 16;

    /// <summary>
    /// Material paths by slot (null where the slot keeps its own material) for a class painted
    /// <paramref name="paintColor"/> (an <c>EPaintColor</c> value), or null when the class cannot be
    /// painted or the colour has no materials for it.
    /// </summary>
    public static IReadOnlyList<string?>? Materials(IFileProvider provider, string classPath, int paintColor)
    {
        var colour = DeployablePaintCatalog.Colors.FirstOrDefault(c => c.Value == paintColor)?.DisplayName;
        if (colour is null) return null;
        if (!provider.TryLoadPackageObject(classPath, out var obj) || obj is not UStruct cls || RowOf(cls) is not { } rowName) return null;
        if (!provider.TryLoadPackageObject(TablePath, out var tableObject) || tableObject is not UDataTable table) return null;
        var row = table.RowMap.FirstOrDefault(r => r.Key.Text.Equals(rowName, StringComparison.OrdinalIgnoreCase)).Value;
        if (row is null) return null;

        var column = "Materials_" + colour;
        var property = row.Properties.FirstOrDefault(p => p.Name.Text == column)
                       ?? row.Properties.FirstOrDefault(p => p.Name.Text.StartsWith(column + "_", StringComparison.Ordinal));
        if (property?.Tag?.GetValue<UScriptArray>() is not { } array) return null;
        var paths = array.Properties
            .Select(p => p.GetValue<FSoftObjectPath>().AssetPathName.Text)
            .Select(path => string.IsNullOrEmpty(path) || path == "None" ? null : path)
            .ToList();
        return paths.Any(p => p is not null) ? paths : null;
    }

    /// <summary>The <c>DT_PaintedDeployables</c> row a class's default sets, inherited from its parents when it sets none.</summary>
    private static string? RowOf(UStruct cls)
    {
        var current = cls;
        for (var depth = 0; current is not null && depth < MaxClassDepth; depth++)
        {
            if (current is UClass { ClassDefaultObject: { IsNull: false } cdo } && cdo.Load() is { } defaults
                && defaults.TryGetValue(out FStructFallback handle, "PaintedDeployableRow")
                && handle.GetOrDefault<FName>("RowName").Text is { Length: > 0 } name && name != "None")
            {
                return name;
            }
            current = current.SuperStruct?.Load<UStruct>();
        }
        return null;
    }
}
