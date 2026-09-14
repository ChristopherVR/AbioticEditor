using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Builds and patches <c>DynamicProperties_</c> arrays (the <c>{Key:EDynamicProperty,
/// Value:int}</c> struct arrays carrying a pet's XP / mutation, on inventory-item slots).
/// Fabricating these from scratch fails on reload because UE5.4 stores the enum type in the
/// property tag's complete-type-name parameters AND struct arrays carry an internal prototype.
/// The verified trick: reuse an existing element's tag <see cref="FPropertyTag.Type"/> objects
/// for new elements, and - when a slot has no array at all - graft a <em>detached clone</em> of
/// an existing array tag (whose prototype is intact) rather than constructing one. All
/// <c>DynamicProperties_</c> use the same <c>EDynamicProperty</c> enum, so any element is a
/// valid template.
/// </summary>
internal static class PetDynamicProperties
{
    /// <summary>A detached array tag (prototype intact) plus the element tag-types to mint new entries.</summary>
    internal sealed class Template
    {
        public required FPropertyTag DetachedArrayTag { get; init; }
        public required FString ElementName { get; init; }
        public required FPropertyTypeName KeyTagType { get; init; }
        public required FPropertyTypeName ValueTagType { get; init; }
        public FPropertyTypeName? KeyEnumType { get; init; }
        public FPropertyTypeName? ElementStructType { get; init; }
    }

    /// <summary>
    /// Clones <paramref name="liveSave"/> and lifts a detached DynamicProperties array tag from a
    /// player inventory slot that has one (>=1 element), for grafting into a slot that lacks one.
    /// Null when no slot in the save carries a DynamicProperties array.
    /// </summary>
    public static Template? CaptureTemplate(SaveGame liveSave)
    {
        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            liveSave.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }
        if (clone.Properties?.FindByPrefix("CharacterSaveData")?.Property is not StructProperty csd
            || csd.Value is not PropertiesStruct csps)
        {
            return null;
        }

        foreach (var arrName in new[] { "HotbarInventory_", "Inventory_", "EquipmentInventory_" })
        {
            if (csps.Properties.FindByPrefix(arrName)?.Property is not ArrayProperty arr || arr.Value is null) continue;
            for (var i = 0; i < arr.Value.Length; i++)
            {
                if (arr.Value.GetValue(i) is not StructProperty slot || slot.Value is not PropertiesStruct sps) continue;
                if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is not StructProperty cd
                    || cd.Value is not PropertiesStruct cdps) continue;
                var tag = cdps.Properties.FirstOrDefault(p => p.Name?.Value?.StartsWith("DynamicProperties_", StringComparison.Ordinal) == true);
                if (BuildTemplate(tag) is { } t) return t;
            }
        }
        return null;
    }

    internal static int? Read(IList<FPropertyTag> props, string key)
    {
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty { Value: { } values }) return null;
        foreach (var element in values.OfType<StructProperty>())
            if (element.Value is PropertiesStruct ps && ps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString() == "EDynamicProperty::" + key)
                return ps.Properties.FindByPrefix("Value")?.Property?.Value is int value ? value : null;
        return null;
    }

    internal static void ApplyCoating(IList<FPropertyTag> props, int? index, int? durability, SaveGame? save)
    {
        if (index is null && durability is null)
        {
            if (Read(props, "WeaponCoating") is null && Read(props, "CoatingDurability") is null) return;
            index = -1;
            durability = 0;
        }
        if (index < -1 || durability < 0) throw new ArgumentOutOfRangeException(nameof(index));
        foreach (var (key, value) in new[] { ("WeaponCoating", index), ("CoatingDurability", durability) })
        {
            if (value is null || Read(props, key) == value) continue;
            if (SetOrAdd(props, key, value.Value)) continue;
            if (save is null) throw new InvalidOperationException("This save cannot supply the item property layout.");
            using var buffer = new MemoryStream();
            save.WriteTo(buffer); buffer.Position = 0;
            var clone = SaveGame.LoadFrom(buffer);
            var template = FindTemplate(clone.Properties ?? []);
            if (!WriteArray(props, template, [("EDynamicProperty::" + key, value.Value)]))
                throw new InvalidOperationException("No compatible item property layout was found in this save.");
        }
    }

    private static Template? FindTemplate(IEnumerable<FPropertyTag> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Name?.Value?.StartsWith("DynamicProperties_", StringComparison.Ordinal) == true && BuildTemplate(tag) is { } template) return template;
            if (FindNested(tag.Property) is { } nested) return nested;
        }
        return null;
    }
    private static Template? FindNested(FProperty? property)
    {
        if (property is StructProperty { Value: PropertiesStruct ps }) return FindTemplate(ps.Properties);
        if (property is ArrayProperty { Value: { } elements })
            foreach (var element in elements.OfType<FProperty>()) { if (FindNested(element) is { } found) return found; }
        if (property is MapProperty { Value: { } pairs })
            foreach (var pair in pairs) { if (FindNested(pair.Value) is { } found) return found; }
        return null;
    }

    private static Template? BuildTemplate(FPropertyTag? arrayTag)
    {
        if (arrayTag?.Property is not ArrayProperty ap || ap.Value is null || ap.Value.Length == 0) return null;
        if (ap.Value.GetValue(0) is not StructProperty e || e.Value is not PropertiesStruct eps) return null;
        var keyTag = eps.Properties.FirstOrDefault(p => p.Name?.Value == "Key");
        var valTag = eps.Properties.FirstOrDefault(p => p.Name?.Value == "Value");
        if (arrayTag.Name is null || keyTag?.Type is null || valTag?.Type is null) return null;

        return new Template
        {
            DetachedArrayTag = arrayTag,
            ElementName = arrayTag.Name,
            KeyTagType = keyTag.Type,
            ValueTagType = valTag.Type,
            KeyEnumType = (keyTag.Property as EnumProperty)?.EnumType,
            ElementStructType = e.StructType,
        };
    }

    private static StructProperty MakeElement(Template t, string keyEnumValue, int value)
    {
        var keyProp = new EnumProperty(new FString("Key"), t.KeyEnumType) { Value = new FString(keyEnumValue) };
        var valProp = FProperty.Create(new FString("Value"), t.ValueTagType);
        valProp.Value = value;
        var ps = new PropertiesStruct
        {
            Properties = new List<FPropertyTag>
            {
                new(new FString("Key"), t.KeyTagType, EPropertyTagFlags.None) { Property = keyProp },
                new(new FString("Value"), t.ValueTagType, EPropertyTagFlags.None) { Property = valProp },
            },
        };
        var sp = (StructProperty)FProperty.Create(t.ElementName, new FPropertyTypeName(new FString(nameof(StructProperty))));
        sp.Value = ps;
        sp.StructType = t.ElementStructType;
        return sp;
    }

    /// <summary>
    /// Grafts a fresh DynamicProperties array (from the detached <paramref name="template"/>)
    /// carrying <paramref name="values"/> into a slot's ChangeableData, replacing any existing
    /// array tag. Returns false when no template was captured.
    /// </summary>
    public static bool WriteArray(IList<FPropertyTag> changeableProps, Template? template, IReadOnlyList<(string Key, int Value)> values)
    {
        if (template?.DetachedArrayTag.Property is not ArrayProperty arr) return false;

        var elems = new FProperty[values.Count];
        for (var i = 0; i < values.Count; i++) elems[i] = MakeElement(template, values[i].Key, values[i].Value);
        arr.Value = elems;

        var existing = changeableProps.FirstOrDefault(p => p.Name?.Value?.StartsWith("DynamicProperties_", StringComparison.Ordinal) == true);
        if (existing is not null) changeableProps.Remove(existing);
        changeableProps.Add(template.DetachedArrayTag);
        return true;
    }

    /// <summary>
    /// Sets one dynamic int in place on a slot's existing array (matched by enum tail); appends a
    /// new element cloning the array's own element tag types when the key is absent. Returns false
    /// when the slot has no DynamicProperties array.
    /// </summary>
    public static bool SetOrAdd(IList<FPropertyTag> changeableProps, string keySuffix, int value)
    {
        var tag = changeableProps.FirstOrDefault(p => p.Name?.Value?.StartsWith("DynamicProperties_", StringComparison.Ordinal) == true);
        if (tag?.Property is not ArrayProperty ap || ap.Value is null) return false;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty e || e.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.EndsWith("::" + keySuffix, StringComparison.Ordinal))
            {
                var valProp = eps.Properties.FindByPrefix("Value")?.Property;
                if (valProp is not null) valProp.Value = value;
                return true;
            }
        }

        var t = BuildTemplate(tag);
        if (t is null) return false;
        var prefix = (ap.Value.GetValue(0) as StructProperty)?.Value is PropertiesStruct p0
            ? p0.Properties.FindByPrefix("Key")?.Property?.Value?.ToString()?.Split("::")[0]
            : null;
        var list = ap.Value.Cast<FProperty>().ToList();
        list.Add(MakeElement(t, $"{prefix ?? "EDynamicProperty"}::{keySuffix}", value));
        ap.Value = list.ToArray();
        return true;
    }
}
