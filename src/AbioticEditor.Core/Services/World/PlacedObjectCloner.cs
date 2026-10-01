using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>The detached records one duplicated object consists of.</summary>
internal sealed record BuiltDuplicate(
    string SourceKey,
    string NewKey,
    KeyValuePair<FProperty, FProperty> ObjectPair,
    IReadOnlyList<KeyValuePair<FProperty, FProperty>> SocketPairs);

/// <summary>
/// Builds the entries a duplication adds. A copy is made from a serialize-and-reload clone of the save (a
/// "donor", sharing nothing with the live tree) so every member of the source, including ones this editor
/// does not model, comes along with its exact hash-suffixed name. Identity fields are then rewritten:
/// map key, <c>ChangableData.AssetID</c>, actor path, internal references, power outlet keys, teleporter
/// tag, bed claim, and stored-item ids. Nothing is committed here.
/// </summary>
internal static class PlacedObjectCloner
{
    /// <summary>A detached deep copy of a save (serialize, reload).</summary>
    public static SaveGame CreateDonor(SaveGame live)
    {
        using var buffer = new MemoryStream();
        live.WriteTo(buffer);
        buffer.Position = 0;
        return SaveGame.LoadFrom(buffer);
    }

    public static BuiltDuplicate Build(
        SaveGame donor, DuplicationRowPlan plan, ContentsMode contents, Func<string> newItemId)
    {
        var objectPair = FindPair(donor, "DeployedObjectMap", plan.SourceKey)
            ?? throw new InvalidOperationException($"Donor has no object {plan.SourceKey}.");
        if (objectPair.Value is not StructProperty { Value: PropertiesStruct entry })
        {
            throw new InvalidOperationException("Object entry is not a struct.");
        }
        var props = entry.Properties;

        // 1. Contents first (before identity rewrites) so re-minted item ids are not remapped again.
        if (contents == ContentsMode.Empty)
        {
            EmptyContainers(props);
            EmptyProxies(props);
        }
        else
        {
            ReMintItemIds(props.FindByPrefix("ContainerInventories_")?.Property, newItemId);
            ReMintItemIds(props.FindByPrefix("ItemProxies_")?.Property, newItemId);
        }

        // 2. Internal references: any string naming a copied object (or its actor path) points at its copy.
        RewriteStrings(props, s => plan.KeyMap.TryGetValue(s, out var k) ? k : plan.PathMap.GetValueOrDefault(s));

        // 3. Identity: map key and actor path.
        objectPair.Key.Value = new FString(plan.NewKey);
        if (!WorldMapAccessor.SetSoftObjectSubPath(props, "ActorPath_", plan.NewActorSubPath))
        {
            throw new InvalidOperationException("Could not set the copy's actor path.");
        }

        // 4. Placement.
        if (!WorldSaveWriter.ApplyTransformToProps(props, plan.NewTranslation, plan.NewRotation))
        {
            throw new InvalidOperationException("Could not place the copy (transform members are missing).");
        }

        // 5. Per-object state that must not be inherited.
        if (plan.ClearBedClaim) WorldSaveWriter.ClearBedClaim(props);
        ClearSeatOccupancy(props);
        if (plan.TeleporterTarget is { } tag && !TeleporterPadFeature.SetFrequency(props, tag))
        {
            throw new InvalidOperationException("Could not set the copy's teleporter tag.");
        }

        // 6. Power outlets.
        var socketPairs = new List<KeyValuePair<FProperty, FProperty>>();
        foreach (var s in plan.Sockets)
        {
            var pair = FindPair(donor, "PowerSocketMap", s.OldId)
                ?? throw new InvalidOperationException($"Donor has no outlet {s.OldId}.");
            if (pair.Value is not StructProperty { Value: PropertiesStruct sp })
            {
                throw new InvalidOperationException("Outlet entry is not a struct.");
            }
            pair.Key.Value = new FString(s.NewId);
            if (sp.Properties.FindByPrefix("PowerSocket_")?.Property is { } idLeaf) idLeaf.Value = new FString(s.NewId);
            if (s.PluggedBefore is not null && sp.Properties.FindByPrefix("PluggedInDeviceAssetID_")?.Property is { } plugged)
            {
                plugged.Value = new FString(s.PluggedAfter ?? WorldSaveWriter.NoPluggedDevice);
            }
            if (sp.Properties.FindByPrefix("ExtraPoweredDeviceAssetIDs_")?.Property is ArrayProperty extras)
            {
                RewriteExtras(extras, plan.KeyMap, s.ExtrasAfter);
            }
            socketPairs.Add(pair);
        }

        return new BuiltDuplicate(plan.SourceKey, plan.NewKey, objectPair, socketPairs);
    }

    /// <summary>
    /// Checks a built copy against its source: identical member layout, identity fields consistent, and no
    /// leftover string that still names a source object. Returns the problems (empty when the copy is sound).
    /// </summary>
    public static List<string> Validate(
        WorldSaveData live, DuplicationRowPlan plan, BuiltDuplicate built)
    {
        var problems = new List<string>();
        var origin = plan.Duplication.Donor ?? live;
        var source = WorldMapAccessor.FindEntry(origin.Raw, "DeployedObjectMap", plan.SourceKey);
        if (source is null || built.ObjectPair.Value is not StructProperty { Value: PropertiesStruct copy })
        {
            problems.Add($"{plan.SourceKey}: source or copy entry is missing.");
            return problems;
        }

        var sourceShape = Shape(source);
        var copyShape = Shape(copy.Properties);
        if (!sourceShape.SequenceEqual(copyShape, StringComparer.Ordinal))
        {
            problems.Add($"{plan.SourceKey}: the copy's member layout differs from its source.");
        }
        if (WorldSaveReader.ExtractMapKeyString(built.ObjectPair.Key) != plan.NewKey)
        {
            problems.Add($"{plan.SourceKey}: the copy's key was not set.");
        }
        if (copy.Properties.FindByPrefix("ChangableData_")?.Property is StructProperty { Value: PropertiesStruct cd }
            && source.FindByPrefix("ChangableData_")?.Property is StructProperty { Value: PropertiesStruct sd }
            && sd.Properties.GetString("AssetID_") == plan.SourceKey
            && cd.Properties.GetString("AssetID_") != plan.NewKey)
        {
            problems.Add($"{plan.SourceKey}: the copy's AssetID does not match its new key.");
        }
        if (WorldMapAccessor.GetSoftObjectPath(copy.Properties, "ActorPath_") is not { } ap
            || ap.SubPath != plan.NewActorSubPath)
        {
            problems.Add($"{plan.SourceKey}: the copy's actor path was not set.");
        }

        // No string in the copy may still name a copied source object or its old actor path.
        var stale = new HashSet<string>(plan.KeyMap.Keys, StringComparer.Ordinal);
        foreach (var p in plan.PathMap.Keys) stale.Add(p);
        RewriteStrings(copy.Properties, s =>
        {
            if (stale.Contains(s)) problems.Add($"{plan.SourceKey}: the copy still names '{s}'.");
            return null;
        });

        foreach (var s in plan.Sockets)
        {
            var pair = built.SocketPairs.FirstOrDefault(p => WorldSaveReader.ExtractMapKeyString(p.Key) == s.NewId);
            var sourceSocket = WorldMapAccessor.FindEntry(origin.Raw, "PowerSocketMap", s.OldId);
            if (pair.Value is not StructProperty { Value: PropertiesStruct sp } || sourceSocket is null)
            {
                problems.Add($"{plan.SourceKey}: outlet {s.OldId} was not copied.");
                continue;
            }
            if (!Shape(sourceSocket).SequenceEqual(Shape(sp.Properties), StringComparer.Ordinal))
            {
                problems.Add($"{plan.SourceKey}: the copy of outlet {s.OldId} has a different member layout.");
            }
            if (sp.Properties.GetString("PowerSocket_") != s.NewId)
            {
                problems.Add($"{plan.SourceKey}: outlet {s.NewId} does not carry its own id.");
            }
        }
        return problems;
    }

    // ---------- helpers ----------

    private static KeyValuePair<FProperty, FProperty>? FindPair(SaveGame save, string map, string key)
    {
        var pairs = WorldMapAccessor.GetPairs(save, map);
        if (pairs is null) return null;
        foreach (var p in pairs)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(p.Key), key, StringComparison.Ordinal)) return p;
        }
        return null;
    }

    /// <summary>Member layout signature: top-level names plus the members of Transform_ and ChangableData_.</summary>
    private static List<string> Shape(IList<FPropertyTag> props)
    {
        var shape = new List<string>();
        foreach (var t in props)
        {
            shape.Add(t.Name?.Value ?? "?");
            if (t.Name?.Value is { } n
                && (n.StartsWith("Transform_", StringComparison.Ordinal) || n.StartsWith("ChangableData_", StringComparison.Ordinal))
                && t.Property is StructProperty { Value: PropertiesStruct inner })
            {
                foreach (var m in inner.Properties) shape.Add("  " + (m.Name?.Value ?? "?"));
            }
        }
        return shape;
    }

    /// <summary>
    /// Rewrites string leaves in place: <paramref name="map"/> returns the replacement or null to keep the
    /// value. Walks structs, arrays and maps (array elements that are bare strings included).
    /// </summary>
    internal static void RewriteStrings(object? node, Func<string, string?> map)
    {
        switch (node)
        {
            case null:
                return;
            case IEnumerable<FPropertyTag> tags:
                foreach (var t in tags) RewriteStrings(t, map);
                return;
            case FPropertyTag tag:
                RewriteStrings(tag.Property, map);
                return;
            case StructProperty sp:
                RewriteStrings(sp.Value, map);
                return;
            case PropertiesStruct ps:
                foreach (var t in ps.Properties) RewriteStrings(t, map);
                return;
            case ArrayProperty ap:
                if (ap.Value is { } arr)
                {
                    for (var i = 0; i < arr.Length; i++)
                    {
                        var item = arr.GetValue(i);
                        if (item is FString fs && !string.IsNullOrEmpty(fs.Value))
                        {
                            if (map(fs.Value) is { } replaced) arr.SetValue(new FString(replaced), i);
                        }
                        else
                        {
                            RewriteStrings(item, map);
                        }
                    }
                }
                return;
            case MapProperty mp:
                if (mp.Value is { } pairs)
                {
                    foreach (var kv in pairs)
                    {
                        RewriteStrings(kv.Key, map);
                        RewriteStrings(kv.Value, map);
                    }
                }
                return;
            case FProperty p:
                switch (p.Value)
                {
                    case FString fs when !string.IsNullOrEmpty(fs.Value):
                        if (map(fs.Value) is { } replaced) p.Value = new FString(replaced);
                        break;
                    case string s when s.Length > 0:
                        if (map(s) is { } replacedText) p.Value = replacedText;
                        break;
                    default:
                        break;
                }
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// Remaps the extra-powered-device list of a copied outlet: copied devices point at their copies, external
    /// devices stay only when the plan kept them. Existing elements are re-used; none is invented.
    /// </summary>
    private static void RewriteExtras(
        ArrayProperty extras, IReadOnlyDictionary<string, string> keyMap, IReadOnlyList<string> after)
    {
        if (extras.Value is not { Length: > 0 } items) return;
        var keep = new HashSet<string>(after, StringComparer.Ordinal);
        var kept = new List<object?>();
        for (var i = 0; i < items.Length; i++)
        {
            var element = items.GetValue(i);
            var text = (element as FProperty)?.Value?.ToString() ?? element?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(text) || text == WorldSaveWriter.NoPluggedDevice)
            {
                kept.Add(element);
                continue;
            }
            var mapped = keyMap.TryGetValue(text, out var copy) ? copy : (keep.Contains(text) ? text : null);
            if (mapped is null) continue;
            if (element is FProperty fp) fp.Value = new FString(mapped);
            else element = new FString(mapped);
            kept.Add(element);
        }
        var replacement = Array.CreateInstance(items.GetType().GetElementType()!, kept.Count);
        for (var i = 0; i < kept.Count; i++) replacement.SetValue(kept[i], i);
        extras.Value = replacement;
    }

    private static void ClearSeatOccupancy(IList<FPropertyTag> props)
    {
        if (props.FindByPrefix("ActiveSeats_")?.Property is ArrayProperty { Value: bool[] seats })
        {
            Array.Clear(seats);
        }
    }

    private static void EmptyProxies(IList<FPropertyTag> props)
    {
        if (props.FindByPrefix("ItemProxies_")?.Property is ArrayProperty { Value: { Length: > 0 } proxies } ap)
        {
            ap.Value = Array.CreateInstance(proxies.GetType().GetElementType()!, 0);
        }
    }

    /// <summary>
    /// Resets every slot of every container inventory to the game's own empty-slot shape (row "Empty", item id
    /// "-1", stack 0, liquid -1, no dynamic properties, no gameplay tags). Existing members are reset; none is
    /// created, so a slot keeps exactly the member layout it had.
    /// </summary>
    private static void EmptyContainers(IList<FPropertyTag> props)
    {
        if (props.FindByPrefix("ContainerInventories_")?.Property is not ArrayProperty { Value: { } inventories }) return;
        foreach (var inv in inventories)
        {
            if (inv is not StructProperty { Value: PropertiesStruct ips }) continue;
            if (ips.Properties.FindByPrefix("InventoryContent_")?.Property is not ArrayProperty { Value: { } slots }) continue;
            foreach (var slot in slots)
            {
                if (slot is StructProperty { Value: PropertiesStruct sps }) EmptySlot(sps.Properties);
            }
        }
    }

    private static void EmptySlot(IList<FPropertyTag> slot)
    {
        if (slot.FindByPrefix("ItemDataTable_")?.Property is StructProperty { Value: PropertiesStruct row })
        {
            WorldMapAccessor.SetName(row.Properties, "RowName", "Empty");
            if (row.Properties.FindByPrefix("DataTable")?.Property is ObjectProperty op) op.ObjectType = new FString(string.Empty);
        }
        if (slot.FindByPrefix("ChangeableData_")?.Property is not StructProperty { Value: PropertiesStruct data }) return;
        var p = data.Properties;
        SetIfPresent(p, "AssetID_", new FString(WorldSaveWriter.NoPluggedDevice));
        SetIfPresent(p, "CurrentItemDurability_", 0.0);
        SetIfPresent(p, "MaxItemDurability_", 0.0);
        SetIfPresent(p, "CurrentStack_", 0);
        SetIfPresent(p, "CurrentAmmoInMagazine_", 0);
        SetIfPresent(p, "LiquidLevel_", -1);
        SetIfPresent(p, "DynamicState_", false);
        SetIfPresent(p, "PlayerMadeString_", new FString(string.Empty));
        if (p.FindByPrefix("CurrentLiquid_") is not null) WorldMapAccessor.SetEnumByte(p, "CurrentLiquid_", "E_LiquidType::NewEnumerator0");
        if (p.FindByPrefix("TextureVariantRow_")?.Property is StructProperty { Value: PropertiesStruct variant })
        {
            WorldMapAccessor.SetName(variant.Properties, "RowName", "None");
            if (variant.Properties.FindByPrefix("DataTable")?.Property is ObjectProperty vop) vop.ObjectType = new FString(string.Empty);
        }
        if (p.FindByPrefix("GameplayTags_")?.Property is StructProperty { Value: GameplayTagContainerStruct tags })
        {
            tags.Tags.Clear();
        }
        if (p.FindByPrefix("DynamicProperties_")?.Property is ArrayProperty { Value: { Length: > 0 } dyn } dp)
        {
            dp.Value = Array.CreateInstance(dyn.GetType().GetElementType()!, 0);
        }
    }

    private static void SetIfPresent(IList<FPropertyTag> tags, string prefix, object value)
    {
        if (tags.FindByPrefix(prefix)?.Property is { } p) p.Value = value;
    }

    /// <summary>Gives every item id (<c>AssetID_</c> leaf holding a 32-hex value) under a subtree a fresh value.</summary>
    private static void ReMintItemIds(object? node, Func<string> newItemId)
    {
        switch (node)
        {
            case FPropertyTag tag when tag.Name?.Value.StartsWith("AssetID_", StringComparison.Ordinal) == true
                && tag.Property?.Value?.ToString() is { Length: 32 } text && text.All(Uri.IsHexDigit):
                tag.Property.Value = new FString(newItemId());
                return;
            case FPropertyTag tag:
                ReMintItemIds(tag.Property, newItemId);
                return;
            case StructProperty sp:
                ReMintItemIds(sp.Value, newItemId);
                return;
            case PropertiesStruct ps:
                foreach (var t in ps.Properties) ReMintItemIds(t, newItemId);
                return;
            case ArrayProperty { Value: { } arr }:
                foreach (var item in arr) ReMintItemIds(item, newItemId);
                return;
            default:
                return;
        }
    }
}
