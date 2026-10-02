using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Services;

/// <summary>
/// How a world-list entry's settings are named and explained, shared by the list tabs and the 3D
/// view's card so both read the same. Resource nodes get plain wording (Available / Depleted, a
/// window's Intact / Broken); every other list uses the field's own label and hint.
/// </summary>
public static class FeatureFieldText
{
    public static bool IsResourceNodes(string featureId) => string.Equals(featureId, "resource-nodes", StringComparison.OrdinalIgnoreCase);

    private static bool IsGlassPane(string featureId, WorldMapEntry entry)
        => IsResourceNodes(featureId) && string.Equals(ResourceNodeNaming.TypeToken(entry.Key), "GlassPane", StringComparison.OrdinalIgnoreCase);

    public static bool IsTrue(string? value) => bool.TryParse(value, out var result) && result;

    public static string Label(HostLanguageService l, string featureId, WorldMapEntry entry, WorldMapField field)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(field);
        return IsResourceNodes(featureId)
            ? field.Id switch
            {
                "position" => l.Resource("WorldResourceNodes_Position"),
                "harvested" => l.Resource(IsGlassPane(featureId, entry) ? "WorldResourceNodes_WindowState" : "WorldResourceNodes_State"),
                "dayPickedUp" => l.Resource("WorldResourceNodes_RespawnTiming"),
                _ => field.Label,
            }
            : field.Label;
    }

    public static string? Hint(HostLanguageService l, string featureId, WorldMapEntry entry, WorldMapField field)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(field);
        return IsResourceNodes(featureId)
            ? field.Id switch
            {
                "position" => l.Resource("WorldResourceNodes_PositionHint"),
                "harvested" => l.Resource(IsGlassPane(featureId, entry) ? "WorldResourceNodes_WindowStateHint" : "WorldResourceNodes_StateHint"),
                "dayPickedUp" => l.Resource("WorldResourceNodes_RespawnTimingHint"),
                _ => field.Hint,
            }
            : field.Hint;
    }

    /// <summary>Plain-language state beside resource-node toggles (Available/Depleted, Intact/Broken).</summary>
    public static string? State(HostLanguageService l, string featureId, WorldMapEntry entry, WorldMapField field)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(field);
        if (!IsResourceNodes(featureId) || field.Id != "harvested") return null;
        return l.Resource(IsGlassPane(featureId, entry)
            ? (IsTrue(field.Value) ? "WorldResourceNodes_Broken" : "WorldResourceNodes_Intact")
            : (IsTrue(field.Value) ? "WorldResourceNodes_Depleted" : "WorldResourceNodes_Available"));
    }

    public static string InputType(WorldMapField field) => field?.Kind switch
    {
        WorldFieldKind.Integer => "number",
        WorldFieldKind.Number => "number",
        _ => "text",
    };

    public static string? Step(WorldMapField field) => field?.Kind == WorldFieldKind.Number ? "any" : null;
}
