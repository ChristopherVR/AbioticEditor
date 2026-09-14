using System.Globalization;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>Shared access to a verified subset of placed objects, without exposing unrelated actors.</summary>
public abstract class DeployedCareFeature : WorldMapFeatureBase
{
    public override string MapName => "DeployedObjectMap";
    public override bool SupportsRemoval => false;
    public override bool AppliesTo(SaveGame save) => WorldMapAccessor.Entries(save, MapName).Any(e => IncludesEntry(e.Props));
    protected static string ClassName(IList<FPropertyTag> props) => props.FindByPrefix("Class_")?.Property?.Value?.ToString() ?? "";
    protected static IList<FPropertyTag>? Struct(IList<FPropertyTag>? props, string prefix)
        => props?.FindByPrefix(prefix)?.Property is StructProperty { Value: PropertiesStruct ps } ? ps.Properties : null;
    protected static IEnumerable<IList<FPropertyTag>> Elements(IList<FPropertyTag> props, string prefix)
        => props.FindByPrefix(prefix)?.Property is ArrayProperty { Value: { } elements }
            ? elements.OfType<StructProperty>().Select(e => e.Value).OfType<PropertiesStruct>().Select(e => e.Properties) : [];
    protected static int? Dynamic(IList<FPropertyTag>? props, string key)
    {
        if (props is null) return null;
        foreach (var element in Elements(props, "DynamicProperties_"))
            if (element.FindByPrefix("Key")?.Property?.Value?.ToString() == "EDynamicProperty::" + key)
                return element.FindByPrefix("Value")?.Property?.Value is int value ? value : null;
        return null;
    }
    protected static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props) => $"{DisplayName.TrimEnd('s')} {ordinal}";
}

public sealed class GardenPlotsFeature : DeployedCareFeature
{
    private static readonly string[] Stages = ["Sprout", "Budding", "Juvenile", "Flowering", "Grown", "Harvested", "Regrowing", "Dead"];
    public override string Id => "garden-plots";
    public override string DisplayName => "Garden plots";
    public override string Description => "Water, fertilizer and planted crops. Save changes before opening the world in game.";
    protected override bool IncludesEntry(IList<FPropertyTag> props) => ClassName(props).Contains("/Farming/GardenPlot_", StringComparison.Ordinal);
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var fields = new List<WorldMapField>();
        var data = Struct(props, "ChangableData_");
        if (data?.FindByPrefix("LiquidLevel_")?.Property?.Value is int water)
            fields.Add(new("water", "Water stored", Number(water), WorldFieldKind.Integer, true, Hint: $"Maximum {WaterCapacity(props):N0} stored water units."));
        var fertilizer = (data?.FindByPrefix("PlayerMadeString_")?.Property?.Value?.ToString() ?? "").Split(",|,", StringSplitOptions.None);
        for (var i = 0; i < fertilizer.Length; i++)
            if (int.TryParse(fertilizer[i], out var value))
                fields.Add(new($"fertilizer:{i}", $"Spot {i + 1} fertilizer", Number(value), WorldFieldKind.Integer, true, Hint: "0 means none; 1,000 means a 1x fertilizer multiplier."));
        foreach (var proxy in Elements(props, "ItemProxies_"))
        {
            if (proxy.FindByPrefix("SpotIndex_")?.Property?.Value is not int spot) continue;
            var crop = Struct(proxy, "ItemRow_")?.FindByPrefix("RowName")?.Property?.Value?.ToString();
            fields.Add(WorldMapField.ReadOnly($"crop:{spot}", $"Spot {spot + 1} crop", crop));
            var changeable = Struct(proxy, "ChangeableData_");
            if (Dynamic(changeable, "GrowthStage") is int stage)
                fields.Add(new($"stage:{spot}", $"Spot {spot + 1} stage", stage >= 0 && stage < Stages.Length ? Stages[stage] : Number(stage), WorldFieldKind.Enum, true, Stages));
            if (Dynamic(changeable, "GrowthProgress") is int progress)
                fields.Add(new($"growth:{spot}", $"Spot {spot + 1} growth", Number(progress), WorldFieldKind.Integer, true, Hint: "Progress toward the next stage, from 0 to 10,000."));
        }
        return fields;
    }
    private static int WaterCapacity(IList<FPropertyTag> props)
        => ClassName(props).Contains("GardenPlot_Large.", StringComparison.Ordinal) ? 3300
            : ClassName(props).Contains("GardenPlot_Medium.", StringComparison.Ordinal) ? 1650 : 400;
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        var field = ReadFields(props).FirstOrDefault(f => f.Id == fieldId && f.Editable);
        if (field is null) return WorldEditResult.Failure("That garden field is not available in this save.");
        if (field.Value == value) return WorldEditResult.NoChange;
        var parts = fieldId.Split(':');
        var number = parts[0] == "stage" ? Array.IndexOf(Stages, value) : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        if (number < 0 || (parts[0] is "growth" or "fertilizer" && number > 10000)) return WorldEditResult.Failure("Choose a valid value between 0 and 10,000.");
        if (fieldId == "water" && number > WaterCapacity(props)) return WorldEditResult.Failure($"This plot holds up to {WaterCapacity(props)} water units.");
        var data = Struct(props, "ChangableData_")!;
        if (fieldId == "water") WorldMapAccessor.SetInt(data, "LiquidLevel_", number);
        else if (parts[0] == "fertilizer")
        {
            var tag = data.FindByPrefix("PlayerMadeString_")!.Property!;
            var values = tag.Value!.ToString()!.Split(",|,", StringSplitOptions.None);
            values[int.Parse(parts[1], CultureInfo.InvariantCulture)] = Number(number);
            tag.Value = string.Join(",|,", values);
        }
        else
        {
            var spot = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var proxy = Elements(props, "ItemProxies_").First(p => p.FindByPrefix("SpotIndex_")?.Property?.Value is int i && i == spot);
            if (!PetDynamicProperties.SetOrAdd(Struct(proxy, "ChangeableData_")!, parts[0] == "stage" ? "GrowthStage" : "GrowthProgress", number))
                return WorldEditResult.Failure("The crop's saved progress could not be updated.");
        }
        return WorldEditResult.Success;
    }
}

public sealed class PowerChairsFeature : DeployedCareFeature
{
    public override string Id => "power-chairs";
    public override string DisplayName => "Power chairs";
    public override string Description => "Battery charge for placed Power Chairs. These chairs have no storage.";
    protected override bool IncludesEntry(IList<FPropertyTag> props) => ClassName(props).Contains("/Deployed_Furniture_Chair_PowerChair", StringComparison.Ordinal);
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
        => Struct(props, "ChangableData_")?.FindByPrefix("LiquidLevel_")?.Property?.Value is int charge
            ? [new("charge", "Battery charge", Number(charge), WorldFieldKind.Integer, true, Hint: "0 to 200. 200 is fully charged.")]
            : [WorldMapField.ReadOnly("charge", "Battery charge", "Not stored in this save")];
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (fieldId != "charge" || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var charge) || charge is < 0 or > 200)
            return WorldEditResult.Failure("Battery charge must be between 0 and 200.");
        var data = Struct(props, "ChangableData_");
        if (data?.FindByPrefix("LiquidLevel_") is null) return WorldEditResult.Failure("Battery charge is not stored in this save.");
        return WorldMapAccessor.SetInt(data, "LiquidLevel_", charge) ? WorldEditResult.Success : WorldEditResult.NoChange;
    }
}

public sealed class ChemistryBenchesFeature : DeployedCareFeature
{
    public override string Id => "chemistry-benches";
    public override string DisplayName => "Chemistry benches";
    public override string Description => "Three saved inputs and the output flask. Open contents to change items; mixing runs in the game.";
    protected override bool IncludesEntry(IList<FPropertyTag> props) => ClassName(props).Contains("/Deployed_ChemistryBench.", StringComparison.Ordinal);
    protected override (string? TargetId, string? Label, bool NeedsHostResolution) LinkFor(string key, IList<FPropertyTag> props)
        => (key, "Edit flask contents", false);
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var fields = new List<WorldMapField>
        {
            WorldMapField.ReadOnly("processing", "Mixing progress", "Available only while the game is running", "The save stores flask contents, not a running batch timer.")
        };
        var inventory = Elements(props, "ContainerInventories_").FirstOrDefault();
        if (inventory is null) return fields;
        var index = 0;
        foreach (var slot in Elements(inventory, "InventoryContent_").Take(4))
        {
            var row = Struct(slot, "ItemDataTable_")?.FindByPrefix("RowName")?.Property?.Value?.ToString();
            fields.Add(WorldMapField.ReadOnly($"flask:{index}", index == 3 ? "Output" : $"Input {index + 1}",
                row is null or "Empty" or "None" ? "Empty" : row));
            index++;
        }
        return fields;
    }
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
        => WorldEditResult.Failure("Use Edit flask contents to change saved items. Mixing progress belongs to the running game.");
}
