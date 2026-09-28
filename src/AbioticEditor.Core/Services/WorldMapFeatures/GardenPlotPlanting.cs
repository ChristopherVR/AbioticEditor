using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Plants a crop into, or clears, an empty garden spot. Deliberately narrow: only the shape the
/// real fixtures prove is written.
/// <para>
/// Evidence (see <c>docs/reference/research/research-garden-planting-and-pet-feeding.md</c>):
/// a <c>GardenPlot_Small</c> with a zero-length <c>ItemProxies_</c> array (never planted or
/// cleared) and a <c>GardenPlot_Small</c> with one proxy at spot 0 differ ONLY in that array
/// (plus the actor's own id/transform), so an empty spot is "no proxy element" and planting is
/// "append one proxy element". Every planted proxy in every fixture has the identical element
/// layout, so the new element is a clone of a game-authored planted proxy from the same save
/// (the same rule <c>AddDroppedItem</c> follows: never fabricate the struct from scratch), with
/// only spot, crop row and its per-item <c>AssetID_</c> replaced. The value state is the one
/// observed on every fully grown crop (stage 4 "Grown", progress 0, portions 1, durability
/// 350/350); no observed spot shows a freshly sprouted state, so none is invented.
/// </para>
/// <para>
/// Multi-spot plots (Medium, Large) and the round plot are NOT supported: no fixture shows a
/// partially planted or empty one, so whether an empty spot is a missing element there is
/// unproven. Whether the running game accepts a hand-planted spot has not been tested in game.
/// </para>
/// </summary>
public static class GardenPlotPlanting
{
    /// <summary>The one class whose empty and planted forms both appear in the fixtures.</summary>
    public const string SupportedClassMarker = "/Farming/GardenPlot_Small.";

    /// <summary>The only spot index a supported plot has.</summary>
    public const int SupportedSpot = 0;

    /// <summary>True when <paramref name="plotProps"/> is a plot whose planting is proven.</summary>
    public static bool IsSupported(IList<FPropertyTag> plotProps)
        => (plotProps.FindByPrefix("Class_")?.Property?.Value?.ToString() ?? "")
            .Contains(SupportedClassMarker, StringComparison.Ordinal);

    /// <summary>True when the plot has a saved proxy for <paramref name="spot"/> (i.e. is planted).</summary>
    public static bool IsPlanted(IList<FPropertyTag> plotProps, int spot)
        => FindProxy(plotProps, spot) is not null;

    /// <summary>
    /// Plants <paramref name="cropRow"/> at an empty spot of a supported plot. Returns a failure
    /// (and changes nothing) when the plot is unsupported, the spot is taken, or the save has no
    /// planted spot to copy the layout from.
    /// </summary>
    public static WorldEditResult Plant(SaveGame save, IList<FPropertyTag> plotProps, int spot, string cropRow)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(plotProps);
        if (string.IsNullOrWhiteSpace(cropRow)) return WorldEditResult.Failure("Choose a crop.");
        if (!IsSupported(plotProps) || spot != SupportedSpot)
            return WorldEditResult.Failure("Planting is only supported on the small garden plot's single spot.");
        if (IsPlanted(plotProps, spot)) return WorldEditResult.Failure("This spot is already planted.");
        if (plotProps.FindByPrefix("ItemProxies_")?.Property is not ArrayProperty proxies || proxies.Value is null)
            return WorldEditResult.Failure("This plot has no saved planting list.");

        var template = CaptureTemplate(save);
        if (template is null)
            return WorldEditResult.Failure("This world has no planted crop to copy the saved layout from. Plant one in game first.");
        if (template.Value is not PropertiesStruct tps) return WorldEditResult.Failure("The planting layout could not be read.");

        tps.Properties.FindByPrefix("SpotIndex_")!.Property!.Value = spot;
        var changeable = ((PropertiesStruct)((StructProperty)tps.Properties.FindByPrefix("ChangeableData_")!.Property!).Value!).Properties;
        changeable.FindByPrefix("AssetID_")!.Property!.Value = new FString(Guid.NewGuid().ToString("N").ToUpperInvariant());
        var itemRow = ((PropertiesStruct)((StructProperty)tps.Properties.FindByPrefix("ItemRow_")!.Property!).Value!).Properties;
        itemRow.FindByPrefix("RowName")!.Property!.Value = new FString(cropRow);

        var list = proxies.Value.Cast<FProperty>().ToList();
        list.Add(template);
        proxies.Value = list.ToArray();
        return WorldEditResult.Success;
    }

    /// <summary>Removes the planting at <paramref name="spot"/>, leaving the plot in its saved "empty" shape.</summary>
    public static WorldEditResult Clear(IList<FPropertyTag> plotProps, int spot)
    {
        ArgumentNullException.ThrowIfNull(plotProps);
        if (!IsSupported(plotProps) || spot != SupportedSpot)
            return WorldEditResult.Failure("Clearing is only supported on the small garden plot's single spot.");
        if (plotProps.FindByPrefix("ItemProxies_")?.Property is not ArrayProperty { Value: { } values } proxies)
            return WorldEditResult.Failure("This plot has no saved planting list.");
        var kept = values.Cast<FProperty>().Where(e => !IsSpot(e, spot)).ToArray();
        if (kept.Length == values.Length) return WorldEditResult.NoChange;
        proxies.Value = kept;
        return WorldEditResult.Success;
    }

    private static FProperty? FindProxy(IList<FPropertyTag> plotProps, int spot)
        => plotProps.FindByPrefix("ItemProxies_")?.Property is ArrayProperty { Value: { } values }
            ? values.Cast<FProperty>().FirstOrDefault(e => IsSpot(e, spot)) : null;

    private static bool IsSpot(FProperty element, int spot)
        => element is StructProperty { Value: PropertiesStruct ps }
            && ps.Properties.FindByPrefix("SpotIndex_")?.Property?.Value is int index && index == spot;

    /// <summary>
    /// A detached clone (from a serialize/reload copy of the save, so it shares nothing with the
    /// live graph) of a fully grown ordinary crop: stage 4, progress 0, raw cooking state.
    /// </summary>
    private static StructProperty? CaptureTemplate(SaveGame save)
    {
        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            save.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }
        foreach (var entry in WorldMapAccessor.Entries(clone, "DeployedObjectMap"))
        {
            var cls = entry.Props.FindByPrefix("Class_")?.Property?.Value?.ToString() ?? "";
            if (!cls.Contains("/Farming/GardenPlot_", StringComparison.Ordinal)) continue;
            if (entry.Props.FindByPrefix("ItemProxies_")?.Property is not ArrayProperty { Value: { } values }) continue;
            foreach (var element in values.OfType<StructProperty>())
                if (element.Value is PropertiesStruct ps && IsGrownTemplate(ps.Properties)) return element;
        }
        return null;
    }

    private static bool IsGrownTemplate(IList<FPropertyTag> proxy)
    {
        if (proxy.FindByPrefix("ChangeableData_")?.Property is not StructProperty { Value: PropertiesStruct cd }) return false;
        if (cd.Properties.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty { Value: { } dyn }) return false;
        int? stage = null, progress = null;
        foreach (var e in dyn.OfType<StructProperty>())
        {
            if (e.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            var value = eps.Properties.FindByPrefix("Value")?.Property?.Value as int?;
            if (key == "EDynamicProperty::GrowthStage") stage = value;
            if (key == "EDynamicProperty::GrowthProgress") progress = value;
        }
        return stage == 4 && progress == 0;
    }
}
