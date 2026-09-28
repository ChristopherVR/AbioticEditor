using System.Globalization;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Every <c>EDynamicProperty</c> a saved pet carries, read-only. This is an inspection model, not
/// an editor (the save stores no food identity at all, only these ints): there is no write path and none is planned until the meaning of the timer fields is
/// proven in game (see <c>docs/reference/research/research-garden-planting-and-pet-feeding.md</c>).
/// </summary>
/// <param name="Source">"world" for a <c>PetNPC</c> entry, or the inventory array a carried pet sits in.</param>
/// <param name="Id">The PetNPC map key, or "Array[index]" for a carried pet.</param>
/// <param name="Species">The creature class tail (world) or the item row (carried).</param>
/// <param name="Dynamic">All saved dynamic ints, keyed by the enum tail (<c>XP</c>, <c>TimerState</c>, ...), in file order.</param>
public sealed record PetSavedCareState(string Source, string Id, string? Species, IReadOnlyDictionary<string, int> Dynamic)
{
    /// <summary>Experience points (a pet's level derives from these).</summary>
    public int? Xp => Get("XP");

    /// <summary>
    /// Opaque clock-like counter present on every saved pet and 0 on the non-pet items that carry the
    /// same key (healing kits, pet beds, nets...). Not proven to be the feeding cooldown: values sit
    /// between about 270,000 and 450,000 across fixtures, a pet stored as an item (in a container)
    /// keeps one, and it is not derivable from the world's <c>MinutesPassed</c>.
    /// </summary>
    public int? TimerState => Get("TimerState");

    /// <summary>Second timestamp-shaped counter, present on Peccary Sows only in the fixtures. Meaning unknown.</summary>
    public int? Generic2 => Get("Generic2");

    /// <summary>Progress toward the next mutation (carried pets only; 3 on both fixture pets).</summary>
    public int? MutationProgress => Get("MutationProgress");

    /// <summary>1-based index of the pet's current identity in its owner row's mutation list (carried pets only).</summary>
    public int? PetMutation => Get("PetMutation");

    /// <summary>Stack-size style counter the game stores on most items; 1 when present on pets.</summary>
    public int? Portions => Get("Portions");

    private int? Get(string key) => Dynamic.TryGetValue(key, out var v) ? v : null;

    /// <summary>Reads every pet in a world save's <c>PetNPC</c> map.</summary>
    public static IReadOnlyList<PetSavedCareState> ReadWorld(SaveGame world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var result = new List<PetSavedCareState>();
        var pairs = WorldSaveReader.GetMapPairs(world.Properties, "PetNPC");
        if (pairs is null) return result;
        foreach (var pair in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(pair.Key);
            if (key is null || pair.Value is not StructProperty { Value: PropertiesStruct ps }) continue;
            var cls = ps.Properties.FindByPrefix("NPCClass_")?.Property?.Value?.ToString();
            var tail = cls is null ? null : cls[(cls.LastIndexOf('.') + 1)..];
            if (tail is not null && tail.EndsWith("_C", StringComparison.Ordinal)) tail = tail[..^2];
            result.Add(new("world", key, tail, ReadDynamic(ps.Properties)));
        }
        return result;
    }

    /// <summary>Reads every carried pet (equipment, hotbar and main inventory) of a player save.</summary>
    public static IReadOnlyList<PetSavedCareState> ReadCarried(SaveGame player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var result = new List<PetSavedCareState>();
        if (player.Properties?.FindByPrefix("CharacterSaveData")?.Property is not StructProperty { Value: PropertiesStruct root }) return result;
        foreach (var prefix in new[] { "EquipmentInventory_", "HotbarInventory_", "Inventory_" })
        {
            if (root.Properties.FindByPrefix(prefix)?.Property is not ArrayProperty { Value: { } slots }) continue;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots.GetValue(i) is not StructProperty { Value: PropertiesStruct slot }) continue;
                string? row = null;
                if (slot.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty { Value: PropertiesStruct handle })
                    row = handle.Properties.GetString("RowName");
                var companionSlot = prefix == "EquipmentInventory_" && i == 12;
                if (!PetItemCatalog.IsPetItem(row) && !(companionSlot && !string.IsNullOrEmpty(row) && row != "Empty")) continue;
                var dynamic = slot.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty { Value: PropertiesStruct cd }
                    ? ReadDynamic(cd.Properties) : new Dictionary<string, int>();
                result.Add(new(prefix.TrimEnd('_'), string.Create(CultureInfo.InvariantCulture, $"{prefix.TrimEnd('_')}[{i}]"), row, dynamic));
            }
        }
        return result;
    }

    private static Dictionary<string, int> ReadDynamic(IList<FPropertyTag> props)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty { Value: { } values }) return map;
        foreach (var element in values.OfType<StructProperty>())
        {
            if (element.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is null || eps.Properties.FindByPrefix("Value")?.Property?.Value is not int value) continue;
            map[key[(key.LastIndexOf(':') + 1)..]] = value;
        }
        return map;
    }
}
