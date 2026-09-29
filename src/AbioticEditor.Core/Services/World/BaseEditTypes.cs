using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>What to do with a link or dependent record that crosses the edit boundary.</summary>
public enum ReferencePolicy
{
    /// <summary>Stop: the edit is refused (nothing is written) until the link is dealt with.</summary>
    Refuse,

    /// <summary>Unplug / clear / remove the dependent record so nothing dangles.</summary>
    Drop,

    /// <summary>Leave it as it is and report it (the link may dangle, or the copy may share it).</summary>
    Keep,
}

/// <summary>How serious a finding is.</summary>
public enum BaseEditSeverity
{
    /// <summary>Something worth knowing; the edit still applies.</summary>
    Info,

    /// <summary>A risk or an unverified assumption; the edit still applies.</summary>
    Warning,

    /// <summary>The edit cannot be applied as staged; nothing is written until it is resolved.</summary>
    Blocking,
}

/// <summary>One finding from validating or previewing a staged base edit.</summary>
/// <param name="Severity">See <see cref="BaseEditSeverity"/>.</param>
/// <param name="Code">A short stable code (for tests and UI filtering).</param>
/// <param name="Message">Plain-language explanation.</param>
/// <param name="Key">The object the finding is about, when there is one.</param>
public sealed record BaseEditIssue(BaseEditSeverity Severity, string Code, string Message, string? Key = null)
{
    /// <summary>True for <see cref="BaseEditSeverity.Blocking"/>.</summary>
    public bool IsBlocking => Severity == BaseEditSeverity.Blocking;
}

/// <summary>One item stored inside a placed object's container inventory.</summary>
public sealed record StoredItemRef(int InventoryIndex, int SlotIndex, string ItemId, int Count);

/// <summary>Reads what a placed object holds inside itself (its own container inventories).</summary>
public static class PlacedObjectContents
{
    /// <summary>The non-empty slots of every <c>ContainerInventories_</c> inventory of an entry.</summary>
    public static IReadOnlyList<StoredItemRef> ReadItems(IList<FPropertyTag> entryProps)
    {
        var result = new List<StoredItemRef>();
        if (entryProps.FindByPrefix("ContainerInventories_")?.Property is not ArrayProperty { Value: { } inventories })
        {
            return result;
        }
        for (var i = 0; i < inventories.Length; i++)
        {
            if (inventories.GetValue(i) is not StructProperty { Value: PropertiesStruct inv }) continue;
            if (inv.Properties.FindByPrefix("InventoryContent_")?.Property is not ArrayProperty { Value: { } slots }) continue;
            for (var j = 0; j < slots.Length; j++)
            {
                if (slots.GetValue(j) is not StructProperty { Value: PropertiesStruct slot }) continue;
                var row = SlotRow(slot.Properties);
                if (string.IsNullOrEmpty(row) || row is "Empty" or "None") continue;
                var count = slot.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty { Value: PropertiesStruct cd }
                    ? (int)cd.Properties.GetLong("CurrentStack_", 0)
                    : 0;
                result.Add(new StoredItemRef(i, j, row, count));
            }
        }
        return result;
    }

    /// <summary>Number of entries in <c>ItemProxies_</c> (planted crops and similar spawned proxies).</summary>
    public static int ProxyCount(IList<FPropertyTag> entryProps)
        => entryProps.FindByPrefix("ItemProxies_")?.Property is ArrayProperty { Value: { } v } ? v.Length : 0;

    internal static string? SlotRow(IList<FPropertyTag> slotProps)
        => slotProps.FindByPrefix("ItemDataTable_")?.Property is StructProperty { Value: PropertiesStruct rh }
            ? rh.Properties.GetString("RowName")
            : null;
}

/// <summary>A <c>PowerSocketMap</c> record as the base-edit planners see it.</summary>
internal sealed record SocketRecord(string Id, string? OwnerKey, string? Plugged, IReadOnlyList<string> Extras);

internal static class PlacedPowerRecords
{
    /// <summary>Every socket record of a save, with its owner (null for level-placed sockets).</summary>
    public static List<SocketRecord> Read(WorldSaveData data)
    {
        var list = new List<SocketRecord>();
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "PowerSocketMap"))
        {
            var id = e.Props.GetString("PowerSocket_") ?? e.Key;
            var plugged = e.Props.GetString("PluggedInDeviceAssetID_");
            if (string.IsNullOrEmpty(plugged) || plugged == WorldSaveWriter.NoPluggedDevice) plugged = null;
            list.Add(new SocketRecord(
                e.Key, PlacedGroupReferenceAnalyzer.OwnerKeyOf(id), plugged,
                PlacedObjectCensus.ReadStringArray(e.Props, "ExtraPoweredDeviceAssetIDs_")));
        }
        return list;
    }
}
