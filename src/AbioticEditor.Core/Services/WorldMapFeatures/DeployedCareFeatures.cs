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

    /// <summary>
    /// Every genuine growable crop row in <c>ItemTable_Global</c> (game version surveyed
    /// 2026-09-17, see <c>docs/reference/research/</c>). Found by loading every
    /// <c>Plant_</c>-prefixed row and excluding the ones whose <c>WorldStaticMesh_</c>
    /// resolves to <c>SM_FarmPlot_Digital_Cartridge</c> - those are ammo cartridges for the
    /// separate "digital" farm plot (<c>Deployed_GardenPlot_Digital</c>, a different class this
    /// feature's own <c>/Farming/GardenPlot_</c> filter already excludes), not crops: e.g.
    /// <c>Plant_Blank</c>/<c>Plant_Pepper</c>/<c>Plant_9mm</c>/<c>Plant_Magnum</c>/
    /// <c>Plant_556</c>/<c>Plant_308</c>/<c>Plant_12g</c>/<c>Plant_Lamogi</c>. GameplayTags_ was
    /// tried first and rejected: both families are tagged <c>Item.Plant</c> (most cartridges are
    /// even tagged <c>Item.Material.Biological</c> too), so tags alone do not tell them apart.
    /// <c>Plant_Dead</c> is also excluded (a fallback identity, not a choosable planting).
    /// A saved row not in this list (a mod, or a future game update) stays selectable as the
    /// current value; see <see cref="ReadFields"/>.
    /// </summary>
    private static readonly string[] CropRows =
    [
        "Plant_Corn", "Plant_Tomato", "Plant_Wheat", "Plant_Greyeb", "Plant_Nyxshade", "Plant_Super_Tomato",
        "Plant_RopePlant", "Plant_Egg", "Plant_SpaceLettuce", "Plant_VinePlant", "Plant_Potato", "Plant_Rice",
        "Plant_Antelight", "Plant_Antelight_GRN", "Plant_Antelight_pink", "Plant_Antelight_red",
        "Plant_Antelight_orange", "Plant_Antelight_blue", "Plant_Antelight_RGB", "Plant_Antelight_space",
        "Plant_Pumpkin", "Plant_GlowTulip", "Plant_Shadowberry", "Plant_Carrot",
    ];
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
            fields.Add(WorldMapField.Choice($"crop:{spot}", $"Spot {spot + 1} crop", crop, CropRows,
                hint: "Changing the crop resets this spot's growth back to a fresh planting."));
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
        if (parts[0] == "crop")
        {
            if (string.IsNullOrWhiteSpace(value)) return WorldEditResult.Failure("Choose a crop.");
            var spot = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var proxy = Elements(props, "ItemProxies_").FirstOrDefault(p => p.FindByPrefix("SpotIndex_")?.Property?.Value is int i && i == spot);
            var itemRow = proxy is null ? null : Struct(proxy, "ItemRow_");
            if (itemRow?.FindByPrefix("RowName") is null)
                return WorldEditResult.Failure("This spot has nothing planted to change crops on.");
            GvasTags.SetName(itemRow, "RowName", value);
            // Reset to a fresh planting: stage 0 is "Sprout" (this array's own first entry) and
            // progress 0 is the start of that stage. This only changes an already-planted spot's
            // crop identity; planting into a never-used spot is not supported (no fixture shows
            // that field shape - see the research note).
            var changeable = Struct(proxy!, "ChangeableData_");
            if (changeable is not null)
            {
                PetDynamicProperties.SetOrAdd(changeable, "GrowthStage", 0);
                PetDynamicProperties.SetOrAdd(changeable, "GrowthProgress", 0);
            }
            return WorldEditResult.Success;
        }
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

/// <summary>Edits cartridge rows already saved by the powered Digital Garden Plot.</summary>
public sealed class DigitalGardenPlotsFeature : DeployedCareFeature
{
    private static readonly string[] CartridgeRows =
    [
        "Plant_Blank", "Plant_Pepper", "Plant_Lamogi", "Plant_9mm", "Plant_Magnum", "Plant_556", "Plant_308", "Plant_12g",
    ];

    public override string Id => "digital-garden-plots";
    public override string DisplayName => "Digital garden plots";
    public override string Description => "Cartridges in placed Digital Garden Plots. Printing progress is managed by the powered plot in game.";
    protected override bool IncludesEntry(IList<FPropertyTag> props)
        => ClassName(props).Contains("/Farming/Deployed_GardenPlot_Digital", StringComparison.Ordinal);

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var fields = new List<WorldMapField>();
        foreach (var proxy in Elements(props, "ItemProxies_"))
        {
            if (proxy.FindByPrefix("SpotIndex_")?.Property?.Value is not int spot) continue;
            var row = Struct(proxy, "ItemRow_")?.FindByPrefix("RowName")?.Property?.Value?.ToString();
            fields.Add(WorldMapField.Choice($"cartridge:{spot}", $"Slot {spot + 1} cartridge", row, OptionsFor(row),
                hint: "Changing a cartridge starts its saved printing progress again."));
            var changeable = Struct(proxy, "ChangeableData_");
            if (Dynamic(changeable, "GrowthStage") is int stage)
                fields.Add(WorldMapField.ReadOnly($"stage:{spot}", $"Slot {spot + 1} printing stage", Number(stage), "The powered plot advances this in game."));
            if (Dynamic(changeable, "GrowthProgress") is int progress)
                fields.Add(WorldMapField.ReadOnly($"growth:{spot}", $"Slot {spot + 1} printing progress", Number(progress), "The powered plot advances this in game."));
        }
        return fields;
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!fieldId.StartsWith("cartridge:", StringComparison.Ordinal)
            || !int.TryParse(fieldId.AsSpan("cartridge:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var spot)
            || string.IsNullOrWhiteSpace(value))
            return WorldEditResult.Failure("Choose a saved cartridge slot and cartridge.");
        var proxy = Elements(props, "ItemProxies_").FirstOrDefault(p => p.FindByPrefix("SpotIndex_")?.Property?.Value is int index && index == spot);
        var itemRow = proxy is null ? null : Struct(proxy, "ItemRow_");
        var rowName = itemRow?.FindByPrefix("RowName")?.Property?.Value?.ToString();
        if (itemRow?.FindByPrefix("RowName") is null) return WorldEditResult.Failure("This cartridge slot is not stored in the save.");
        var selected = OptionsFor(rowName).FirstOrDefault(option => string.Equals(option, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (selected is null) return WorldEditResult.Failure("Choose a cartridge offered by this plot.");
        if (string.Equals(rowName, selected, StringComparison.Ordinal)) return WorldEditResult.NoChange;
        GvasTags.SetName(itemRow, "RowName", selected);
        var changeable = Struct(proxy!, "ChangeableData_");
        if (changeable is not null)
        {
            PetDynamicProperties.SetOrAdd(changeable, "GrowthStage", 0);
            PetDynamicProperties.SetOrAdd(changeable, "GrowthProgress", 0);
        }
        return WorldEditResult.Success;
    }

    private static List<string> OptionsFor(string? current)
    {
        var options = CartridgeRows.ToList();
        if (!string.IsNullOrWhiteSpace(current) && !options.Contains(current, StringComparer.OrdinalIgnoreCase)) options.Add(current);
        return options;
    }
}

/// <summary>Edits the persisted switch state of placed wall Sconce lamps.</summary>
public sealed class SconceLampsFeature : DeployedCareFeature
{
    public override string Id => "sconce-lamps";
    public override string DisplayName => "Sconce lamps";
    public override string Description => "Saved on or off state for placed Sconce lamps.";
    protected override bool IncludesEntry(IList<FPropertyTag> props)
        => ClassName(props).Contains("/Misc/Deployed_Lamp_Sconce", StringComparison.Ordinal);
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var data = Struct(props, "ChangableData_");
        return data?.FindByPrefix("DynamicState_")?.Property?.Value is bool on
            ? [WorldMapField.Bool("on", "Lamp on", on)]
            : [WorldMapField.ReadOnly("on", "Lamp on", "Not stored in this save")];
    }
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (fieldId != "on" || !WorldMapAccessor.TryParseBool(value, out var on)) return WorldEditResult.Failure("Lamp state must be on or off.");
        var data = Struct(props, "ChangableData_");
        if (data?.FindByPrefix("DynamicState_")?.Property?.Value is not bool current) return WorldEditResult.Failure("Lamp state is not stored in this save.");
        return current == on ? WorldEditResult.NoChange
            : WorldMapAccessor.SetBool(data, "DynamicState_", on) ? WorldEditResult.Success : WorldEditResult.Failure("Lamp state could not be updated.");
    }
}

public sealed class ChemistryBenchesFeature : DeployedCareFeature
{
    public override string Id => "chemistry-benches";
    public override string DisplayName => "Chemistry benches";
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props) => $"Chemistry bench {ordinal}";
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
            fields.Add(new WorldMapField($"flask:{index}", index == 3 ? "Output" : $"Input {index + 1}",
                row is null or "Empty" or "None" ? "Empty" : row, WorldFieldKind.Text, true));
            index++;
        }
        return fields;
    }
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!fieldId.StartsWith("flask:", StringComparison.Ordinal)
            || !int.TryParse(fieldId.AsSpan("flask:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            || index is < 0 or > 3)
            return WorldEditResult.Failure("Only flask contents can be changed in a saved chemistry bench.");
        var inventory = Elements(props, "ContainerInventories_").FirstOrDefault();
        var slot = inventory is null ? null : Elements(inventory, "InventoryContent_").Skip(index).FirstOrDefault();
        if (slot is null || Struct(slot, "ItemDataTable_")?.FindByPrefix("RowName")?.Property is null)
            return WorldEditResult.Failure("This flask slot is not stored in the save.");
        var normalized = string.IsNullOrWhiteSpace(value) ? "Empty" : value.Trim();
        var current = Struct(slot, "ItemDataTable_")!.FindByPrefix("RowName")!.Property!.Value?.ToString();
        if (string.Equals(current, normalized, StringComparison.Ordinal)) return WorldEditResult.NoChange;
        WorldSaveWriter.ApplyChemistryFlaskSlot(slot, index, normalized);
        return WorldEditResult.Success;
    }
}
