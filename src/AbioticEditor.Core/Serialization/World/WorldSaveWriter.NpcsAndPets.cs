using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - NPC and pet edits (health, XP, add/remove pets, limb health).
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Patches <c>NarrativeNPCMap</c> entries (IsDead / NarrativeState / name) by map key.
    /// Pets (the <c>PetNPC</c> map) are handled separately by <see cref="ApplyPets"/>.
    /// </summary>
    public static void ApplyNpcs(WorldSaveData data, IEnumerable<WorldNpc> updated)
    {
        var byId = updated.ToDictionary(n => n.Id, StringComparer.Ordinal);
        ApplyNpcMap(data, "NarrativeNPCMap", byId);
    }

    /// <summary>
    /// Patches <c>PetNPC</c> entries by GUID: life flag, player name, creature class
    /// (the "upgrade / downgrade"), per-limb health, and XP. Every field is patched in
    /// place on the existing struct - the limb map and dynamic-property array keep their
    /// shape, so untouched pets re-serialize byte-perfect. Pets present in the save but
    /// absent from <paramref name="updated"/> are left untouched.
    /// </summary>
    public static void ApplyPets(WorldSaveData data, IEnumerable<WorldPet> updated)
    {
        var byId = updated.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "PetNPC");
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var pet)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var p = ps.Properties;
            SetBool(p, "IsDead_", pet.IsDead);
            SetTextNone(p, "CustomName_", pet.CustomName ?? string.Empty);
            if (!string.IsNullOrEmpty(pet.NpcClass)) SetSoftObject(p, "NPCClass_", pet.NpcClass!);
            ApplyLimbHealth(p, pet.LimbHealth);
            ApplyDynamicInt(p, "XP", pet.Xp);
        }
    }

    /// <summary>
    /// Removes one <c>PetNPC</c> entry by GUID - the editor equivalent of deleting the pet
    /// from the world. Returns true when the entry existed. (Mirror of
    /// <see cref="RemoveDroppedItem"/>.)
    /// </summary>
    public static bool RemovePet(WorldSaveData data, string id)
    {
        if (data.Raw.Properties.FindByPrefix("PetNPC")?.Property is not MapProperty mp || mp.Value is null)
        {
            return false;
        }
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key), id, StringComparison.Ordinal))
            {
                mp.Value.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Per-limb health written when no explicit total is given (the game clamps to the level-scaled max).</summary>
    private const double FullLimbHealthOnArrival = 1000;

    /// <summary>
    /// Adds a pet to the world's <c>PetNPC</c> map by <b>cloning an existing entry</b> (so the
    /// struct layout is byte-identical to what the game writes) and overwriting only the fields
    /// that define this pet: a fresh GUID key, class, name, health, XP, and location. When a pet
    /// of the same class already exists it is preferred as the clone template (so the limb set
    /// matches). <paramref name="totalHealth"/> (e.g. a carried pet's durability) is distributed
    /// across the template's limbs to keep the total HP; null fills every limb. Returns the new
    /// GUID, or null when the world has no creature at all to use as a template. Mirrors
    /// <see cref="AddDroppedItem"/>.
    ///
    /// <para>When the world has no pets yet, the entry is cloned from a story NPC instead: the
    /// <c>PetNPC</c> and <c>NarrativeNPCMap</c> maps share the same NPC-state struct, so a pet can
    /// be placed into a world that has never had one, as long as it contains some creature.</para>
    /// </summary>
    public static string? AddPet(WorldSaveData data, WorldPet pet, double? totalHealth = null)
    {
        // The PetNPC map node must exist to add into (it can be empty - we will clone a template
        // from the NPC map in that case).
        if (data.Raw.Properties?.FindByPrefix("PetNPC")?.Property is not MapProperty mp || mp.Value is null)
        {
            return null;
        }

        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            data.Raw.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }

        var template = FindCreatureTemplate(clone, pet.NpcClass);
        if (template.Key is null)
        {
            return null; // no pet and no NPC anywhere to base the entry on
        }

        var key = template.Key;
        var value = template.Value;

        var existingKey = WorldSaveReader.ExtractMapKeyString(key);
        var newId = FormatGuidLike(existingKey, Guid.NewGuid());
        key.Value = new FString(newId);

        if (value is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            SetBool(ps.Properties, "IsDead_", false);
            SetTextNone(ps.Properties, "CustomName_", pet.CustomName ?? string.Empty);
            if (!string.IsNullOrEmpty(pet.NpcClass)) SetSoftObject(ps.Properties, "NPCClass_", pet.NpcClass!);

            if (ps.Properties.FindByPrefix("Location_")?.Property is StructProperty locSp && locSp.Value is VectorStruct vec)
            {
                var v = vec.Value;
                v.X = pet.X;
                v.Y = pet.Y;
                v.Z = pet.Z;
                vec.Value = v;
            }
            DistributeLimbHealth(ps.Properties, totalHealth);
            ApplyDynamicInt(ps.Properties, "XP", pet.Xp);
        }

        mp.Value.Add(new KeyValuePair<FProperty, FProperty>(key, value));
        return newId;
    }

    /// <summary>
    /// Finds a creature entry to clone a new pet from, preferring (1) a pet of the same class, then
    /// (2) any pet, then (3) any story NPC (NarrativeNPCMap shares the NPC-state struct). Returns a
    /// default pair when the world contains no creature at all.
    /// </summary>
    private static KeyValuePair<FProperty, FProperty> FindCreatureTemplate(SaveGame clone, string? npcClass)
    {
        var wantShort = PetCatalog.ShortOf(npcClass);

        static bool Matches(KeyValuePair<FProperty, FProperty> kv, string? want)
            => kv.Value is StructProperty s && s.Value is PropertiesStruct p
               && string.Equals(PetCatalog.ShortOf(p.Properties.FindByPrefix("NPCClass_")?.Property?.Value?.ToString()),
                   want, StringComparison.OrdinalIgnoreCase);

        var pets = (clone.Properties?.FindByPrefix("PetNPC")?.Property as MapProperty)?.Value;
        if (pets is { Count: > 0 })
        {
            var sameClass = pets.FirstOrDefault(kv => Matches(kv, wantShort));
            return sameClass.Key is not null ? sameClass : pets[0];
        }

        // No pets in this world: clone the shared NPC-state struct from a story NPC instead.
        var npcs = (clone.Properties?.FindByPrefix("NarrativeNPCMap")?.Property as MapProperty)?.Value;
        if (npcs is { Count: > 0 })
        {
            var sameClass = npcs.FirstOrDefault(kv => Matches(kv, wantShort));
            return sameClass.Key is not null ? sameClass : npcs[0];
        }

        return default;
    }

    /// <summary>
    /// Writes a pet's per-limb health. With <paramref name="totalHealth"/>, distributes it across
    /// the template's tracked (non-zero) limbs in proportion to their existing values so the sum
    /// equals the total (best-effort 1:1 with a carried pet's single durability); without it,
    /// fills every limb to <see cref="FullLimbHealthOnArrival"/>.
    /// </summary>
    private static void DistributeLimbHealth(IList<FPropertyTag> props, double? totalHealth)
    {
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is not MapProperty hm || hm.Value is null) return;

        if (totalHealth is not { } total || total <= 0)
        {
            foreach (var kv in hm.Value) if (kv.Value is not null) kv.Value.Value = FullLimbHealthOnArrival;
            return;
        }

        var weights = hm.Value.Select(kv => kv.Value?.Value is double d ? d : 0).ToList();
        var sum = weights.Sum();
        if (sum <= 0)
        {
            // The template carries no proportional info (every limb is zero), so we can't tell
            // which limbs the species actually tracks. Spreading total/count would put health on
            // untracked limbs and skew MaxLimb/Status; arrive at full instead (same as no-total).
            foreach (var kv in hm.Value) if (kv.Value is not null) kv.Value.Value = FullLimbHealthOnArrival;
            return;
        }
        for (var i = 0; i < hm.Value.Count; i++)
        {
            if (hm.Value[i].Value is not { } slot) continue;
            slot.Value = total * (weights[i] / sum);
        }
    }

    /// <summary>
    /// Patches existing <c>CurrentHealthMap_</c> limb values in place (matched by the full
    /// <c>EBodyLimbs::*</c> enum key). Never adds or removes limbs.
    /// </summary>
    private static void ApplyLimbHealth(IList<FPropertyTag> props, IReadOnlyDictionary<string, double> limbs)
    {
        if (limbs.Count == 0) return;
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is not MapProperty mp || mp.Value is null) return;

        foreach (var kv in mp.Value)
        {
            var key = kv.Key?.Value?.ToString();
            if (key is not null && kv.Value is not null && limbs.TryGetValue(key, out var v))
            {
                kv.Value.Value = v;
            }
        }
    }

    /// <summary>
    /// Sets one int inside <c>DynamicProperties_</c> (matched by <c>EDynamicProperty::*</c>
    /// enum tail, e.g. "XP"). When the key is absent but the array exists, a new element is
    /// appended by cloning the array's own element tag types (the verified-safe technique in
    /// <see cref="PetDynamicProperties.SetOrAdd"/>), so an edit to a pet whose XP entry was
    /// delta-omitted is no longer silently lost. Only truly a no-op when the pet has no
    /// <c>DynamicProperties_</c> array at all (nothing to clone a prototype from).
    /// </summary>
    private static void ApplyDynamicInt(IList<FPropertyTag> props, string keySuffix, int value)
        => PetDynamicProperties.SetOrAdd(props, keySuffix, value);

    private static void ApplyNpcMap(WorldSaveData data, string prefix, Dictionary<string, WorldNpc> byId)
    {
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, prefix);
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var npc)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            SetBool(ps.Properties, "IsDead_", npc.IsDead);
            if (!string.IsNullOrEmpty(npc.State))
            {
                SetEnumByte(ps.Properties, "NarrativeState_", npc.State!);
            }
            SetTextNone(ps.Properties, "CustomName_", npc.CustomName ?? string.Empty);
        }
    }
}
