using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Builds a <see cref="PowerGraph"/> from world saves, one save at a time so a host can load the
/// large hub save without keeping every sibling in memory (only small records are retained, never
/// the <see cref="SaveGame"/>). Read-only: nothing here writes to a save.
///
/// <para>Rules encoded here, each backed by fixture evidence (see
/// <c>docs/reference/research/research-power-network-links.md</c>):</para>
/// <list type="bullet">
///   <item>A <c>PowerSocketMap</c> record is the ONLY owner of a link: its
///     <c>PluggedInDeviceAssetID_</c> names the device it supplies. No device record and no other
///     map stores the reverse direction.</item>
///   <item>A socket key that is a level actor path is a fixed, level-placed socket.</item>
///   <item>Any other socket key is a device GUID (32 hex) followed by a socket number, so the first
///     32 characters name the device that owns the outlet.</item>
///   <item>Device ids are <c>DeployedObjectMap</c> keys and are found across every supplied save.</item>
/// </list>
/// </summary>
public sealed class PowerGraphBuilder
{
    private const string SocketMap = "PowerSocketMap";
    private const string DeviceMap = "DeployedObjectMap";
    private const string PluggedPrefix = "PluggedInDeviceAssetID_";
    private const string ExtraPrefix = "ExtraPoweredDeviceAssetIDs_";

    private readonly Dictionary<string, PowerSocketDeviceResolver.DeviceInfo> _deviceIndex
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RawSocket> _rawSockets = [];
    private readonly List<string> _files = [];

    private sealed record RawSocket(string File, string Key, string? Plugged, IReadOnlyList<string> Extras);

    /// <summary>Adds one save's deployables and power sockets. The first save to define a device id wins.</summary>
    public PowerGraphBuilder AddSave(string fileName, SaveGame save)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(save);
        _files.Add(fileName);
        PowerSocketDeviceResolver.MergeSave(_deviceIndex, fileName, save);
        foreach (var entry in WorldMapAccessor.Entries(save, SocketMap))
        {
            var extras = new List<string>();
            if (entry.Props.FindByPrefix(ExtraPrefix)?.Property is ArrayProperty { Value: { } array })
            {
                foreach (var element in array)
                {
                    var value = element is FProperty { Value: { } inner } ? inner : element;
                    var text = value switch { FString fs => fs.Value, string s => s, _ => value?.ToString() };
                    if (!PowerSocketDeviceResolver.IsNothingPlugged(text))
                    {
                        extras.Add(text!);
                    }
                }
            }
            AddSocketRecord(fileName, entry.Key, entry.Props.GetString(PluggedPrefix), extras);
        }
        return this;
    }

    /// <summary>
    /// Adds already-decoded records for one save file. This is the same data <see cref="AddSave"/>
    /// reads, exposed so a host can preview a hypothetical layout, and so tests can build small
    /// graphs (loops, orphans) that no fixture happens to contain.
    /// </summary>
    public PowerGraphBuilder AddRecords(
        string fileName,
        IEnumerable<(string Id, string? ClassName, bool IsContainer)> devices,
        IEnumerable<(string Key, string? PluggedInDeviceId, IReadOnlyList<string> ExtraDeviceIds)> sockets)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        _files.Add(fileName);
        foreach (var (id, className, isContainer) in devices)
        {
            _deviceIndex.TryAdd(id, new PowerSocketDeviceResolver.DeviceInfo(
                id, className, PowerSocketDeviceResolver.FriendlyName(className), isContainer, fileName));
        }
        foreach (var (key, plugged, extras) in sockets)
        {
            AddSocketRecord(fileName, key, plugged, extras);
        }
        return this;
    }

    private void AddSocketRecord(string fileName, string key, string? plugged, IReadOnlyList<string> extras)
        => _rawSockets.Add(new RawSocket(fileName, key,
            PowerSocketDeviceResolver.IsNothingPlugged(plugged) ? null : plugged, extras));

    /// <summary>Convenience: builds a graph from several already-loaded saves.</summary>
    public static PowerGraph Build(IEnumerable<(string FileName, SaveGame Save)> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);
        var builder = new PowerGraphBuilder();
        foreach (var (file, save) in saves)
        {
            builder.AddSave(file, save);
        }
        return builder.Build();
    }

    /// <summary>Resolves every stored reference against the saves added so far.</summary>
    public PowerGraph Build()
    {
        var devices = new Dictionary<string, PowerDeviceNode>(StringComparer.OrdinalIgnoreCase);
        var sockets = new Dictionary<string, PowerSocketNode>(StringComparer.Ordinal);
        var links = new List<StoredPowerLink>();

        PowerDeviceNode Device(string id)
        {
            if (devices.TryGetValue(id, out var existing))
            {
                return existing;
            }
            var node = _deviceIndex.TryGetValue(id, out var info)
                ? new PowerDeviceNode(id, info.ClassName, RoleOf(info.ClassName), info.FriendlyName, info.IsContainer,
                    new PowerLocation(info.SourceFile ?? string.Empty, DeviceMap, id))
                : new PowerDeviceNode(id, null, PowerDeviceRole.Unknown, "Missing device", false, null);
            devices[id] = node;
            return node;
        }

        foreach (var raw in _rawSockets)
        {
            var location = new PowerLocation(raw.File, SocketMap, raw.Key);
            PowerSocketNode socket;
            if (raw.Key.Contains("PersistentLevel", StringComparison.Ordinal))
            {
                socket = new PowerSocketNode(raw.Key, PowerSocketKind.LevelPlaced, location, null, null, null);
            }
            else if (TrySplitOwnedKey(raw.Key, out var ownerId, out var number))
            {
                var owner = Device(ownerId);
                socket = new PowerSocketNode(raw.Key, PowerSocketKind.DeviceOwned, location, ownerId,
                    Status(owner, raw.File), number);
            }
            else
            {
                socket = new PowerSocketNode(raw.Key, PowerSocketKind.Unrecognized, location, null, null, null);
            }
            sockets[raw.Key] = socket;

            if (raw.Plugged is not null)
            {
                links.Add(new StoredPowerLink(socket, PowerLinkSlot.PluggedIn, raw.Plugged, Status(Device(raw.Plugged), raw.File)));
            }
            foreach (var extra in raw.Extras)
            {
                links.Add(new StoredPowerLink(socket, PowerLinkSlot.ExtraPowered, extra, Status(Device(extra), raw.File)));
            }
        }
        return new PowerGraph(_files.ToArray(), devices, sockets, links);
    }

    private static PowerEndpointStatus Status(PowerDeviceNode device, string fromFile)
        => device.IsMissing
            ? PowerEndpointStatus.MissingInSuppliedSaves
            : string.Equals(device.Location!.FileName, fromFile, StringComparison.OrdinalIgnoreCase)
                ? PowerEndpointStatus.Resolved
                : PowerEndpointStatus.ResolvedInOtherSave;

    /// <summary>A device-owned socket key is 32 hex characters (the owner GUID) then a numeric socket number.</summary>
    internal static bool TrySplitOwnedKey(string key, out string ownerId, out string number)
    {
        ownerId = number = string.Empty;
        if (key.Length <= 32)
        {
            return false;
        }
        for (var i = 0; i < 32; i++)
        {
            if (!Uri.IsHexDigit(key[i]))
            {
                return false;
            }
        }
        var tail = key[32..];
        if (!tail.All(char.IsAsciiDigit))
        {
            return false;
        }
        ownerId = key[..32];
        number = tail;
        return true;
    }

    /// <summary>Classifies a deployable class name as power infrastructure or an ordinary device.</summary>
    public static PowerDeviceRole RoleOf(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return PowerDeviceRole.Other;
        }
        static bool Has(string name, string needle) => name.Contains(needle, StringComparison.OrdinalIgnoreCase);
        if (Has(className, "Battery")) return PowerDeviceRole.Battery;
        if (Has(className, "PlugStrip")) return PowerDeviceRole.PlugStrip;
        if (Has(className, "Plugboard")) return PowerDeviceRole.Plugboard;
        if (Has(className, "CableReroute")) return PowerDeviceRole.CableReroute;
        if (Has(className, "LaserPowerConverter")) return PowerDeviceRole.LaserPowerConverter;
        return PowerDeviceRole.Other;
    }
}
