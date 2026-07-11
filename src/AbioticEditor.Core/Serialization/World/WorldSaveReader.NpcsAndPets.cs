using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveReader - NPC and pet reads (health, XP, limb health, spawn maps).
public static partial class WorldSaveReader
{
    /// <summary>
    /// Reads <c>NarrativeNPCMap</c> (story NPCs / traders). Tamed companions live in
    /// <c>PetNPC</c> and are read separately by <see cref="ReadPets"/>; both maps share
    /// the same <c>SaveData_NPCState_Struct</c>, but pets fill the pet-specific fields.
    /// </summary>
    private static List<WorldNpc> ReadNpcs(SaveGame save)
    {
        var result = new List<WorldNpc>();
        ReadNpcMap(save, "NarrativeNPCMap", isPet: false, result);
        return result;
    }

    /// <summary>
    /// Reads the <c>PetNPC</c> map: per-pet name, life flag, creature class, location,
    /// per-limb health (<c>CurrentHealthMap_</c>) and XP (<c>DynamicProperties_</c>).
    /// </summary>
    private static List<WorldPet> ReadPets(SaveGame save)
    {
        var result = new List<WorldPet>();
        var pairs = GetMapPairs(save.Properties, "PetNPC");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var p = ps.Properties;
            var isDead = p.TryGetBool("IsDead_") ?? false;
            var state = p.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString();
            var customName = p.FindByPrefix("CustomName_")?.Property?.Value?.ToString();
            var npcClass = p.FindByPrefix("NPCClass_")?.Property?.Value?.ToString();

            double x = 0, y = 0, z = 0;
            if (p.FindByPrefix("Location_")?.Property is StructProperty locSp && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }

            var limbs = ReadLimbHealth(p);
            var xp = ReadDynamicInt(p, "XP");

            result.Add(new WorldPet(
                id, isDead, npcClass, x, y, z,
                string.IsNullOrEmpty(customName) ? null : customName,
                limbs, xp, state));
        }
        return result;
    }

    /// <summary>
    /// Reads <c>CurrentHealthMap_</c>: a map of <c>EBodyLimbs::*</c> (EnumProperty) to a
    /// DoubleProperty current-health value. Keyed by the full enum string.
    /// </summary>
    private static Dictionary<string, double> ReadLimbHealth(IList<FPropertyTag> props)
    {
        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is MapProperty mp && mp.Value is not null)
        {
            foreach (var kv in mp.Value)
            {
                var key = kv.Key?.Value?.ToString();
                if (key is null) continue;
                dict[key] = kv.Value?.Value is double d ? d : 0;
            }
        }
        return dict;
    }

    /// <summary>
    /// Reads one int from <c>DynamicProperties_</c> - an array of {Key (EnumProperty
    /// <c>EDynamicProperty::*</c>), Value (IntProperty)} structs. Matches by enum tail
    /// (e.g. <paramref name="keySuffix"/> = "XP"); returns 0 when absent.
    /// </summary>
    private static int ReadDynamicInt(IList<FPropertyTag> props, string keySuffix)
    {
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap || ap.Value is null)
            return 0;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.EndsWith("::" + keySuffix, StringComparison.Ordinal))
            {
                return eps.Properties.FindByPrefix("Value")?.Property?.Value switch
                {
                    int ii => ii,
                    long ll => (int)ll,
                    _ => 0,
                };
            }
        }
        return 0;
    }

    private static void ReadNpcMap(SaveGame save, string prefix, bool isPet, List<WorldNpc> result)
    {
        var pairs = GetMapPairs(save.Properties, prefix);
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var isDead = ps.Properties.TryGetBool("IsDead_") ?? false;
            var state = ps.Properties.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString();
            var customName = ps.Properties.FindByPrefix("CustomName_")?.Property?.Value?.ToString();
            // Pets: take the class from NPCClass_ (inside NarrativeNPCMap that field
            // serializes as None - there the map key carries the class instead).
            var npcClass = ps.Properties.FindByPrefix("NPCClass_")?.Property?.Value?.ToString();
            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("Location_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }
            result.Add(new WorldNpc(id, isDead, state, x, y, z, isPet, customName, npcClass));
        }
    }
}
