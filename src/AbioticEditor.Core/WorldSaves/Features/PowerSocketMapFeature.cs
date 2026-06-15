using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>PowerSocketMap</c> (region saves): each power-socket actor stores
/// its plugged-in device, whether a timer is active, and the timer mode. The one
/// meaningful editable state is <c>HasTimer_</c> (bool) - true = timer is armed.
///
/// <para><c>TimerMode_</c> is a <c>ByteProperty</c> carrying an <c>E_PowerTimerModes</c>
/// enumerator.  Only a single enumerator value (<c>E_PowerTimerModes::NewEnumerator0</c>)
/// was observed across all available fixture saves; the full set of valid enumerators
/// cannot be determined from save data alone, so <c>timerMode</c> is exposed as a
/// read-only display field rather than an editable choice to avoid corrupting saves with
/// invented names.</para>
///
/// <para>This follows the same pattern as <see cref="ElevatorMapFeature"/>: derive from
/// <see cref="WorldMapFeatureBase"/>, expose typed fields in <see cref="ReadFields"/>,
/// and patch one leaf in <see cref="ApplyField"/> via <see cref="WorldMapAccessor"/>.</para>
/// </summary>
public sealed class PowerSocketMapFeature : WorldMapFeatureBase
{
    /// <summary>Leaf prefix for the socket's own GUID/ID string (StrProperty).</summary>
    private const string PowerSocketPrefix = "PowerSocket_";

    /// <summary>Leaf prefix for the asset ID of the device plugged into this socket (StrProperty; "-1" = nothing).</summary>
    private const string PluggedInDevicePrefix = "PluggedInDeviceAssetID_";

    /// <summary>Leaf prefix for whether a timer is currently armed on this socket (BoolProperty).</summary>
    private const string HasTimerPrefix = "HasTimer_";

    /// <summary>Leaf prefix for the timer-mode enumerator (ByteProperty of E_PowerTimerModes).</summary>
    private const string TimerModePrefix = "TimerMode_";

    /// <summary>Leaf prefix for the array of extra powered device asset IDs (ArrayProperty of StrProperty).</summary>
    private const string ExtraDevicesPrefix = "ExtraPoweredDeviceAssetIDs_";

    /// <inheritdoc/>
    public override string Id => "power-sockets";

    /// <inheritdoc/>
    public override string MapName => "PowerSocketMap";

    /// <inheritdoc/>
    public override string DisplayName => "Power Sockets";

    /// <inheritdoc/>
    public override string Description =>
        "View power-socket state and arm or disarm the timer for each socket. Disconnect a socket to reset it.";

    /// <summary>
    /// The remove action on a socket resets/disconnects it (drops its persisted state so the
    /// game re-creates the socket at its blueprint default), so the button reads "Disconnect"
    /// rather than the generic "Remove this Entry".
    /// </summary>
    public override string RemoveActionLabel => "Disconnect";

    // Device index for the save currently being read, rebuilt once per Read via OnBeginRead so
    // LabelFor/ReadFields/LinkFor can name the plugged-in device and link to it when it is a
    // container in this save. PluggedInDeviceAssetID_ shares the DeployedObjectMap key space; see
    // PowerSocketDeviceResolver. Cross-save devices (a socket here powering a device in another
    // region save, ~10% in practice) are not in this index, so they show the raw id, not a name.
    private Dictionary<string, PowerSocketDeviceResolver.DeviceInfo> _deviceIndex = new();

    /// <inheritdoc/>
    protected override void OnBeginRead(SaveGame save)
        => _deviceIndex = PowerSocketDeviceResolver.BuildIndex(save);

    /// <summary>Socket keys are raw GUIDs, so number them and name what's plugged in when known.</summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
    {
        var device = props.GetString(PluggedInDevicePrefix);
        if (PowerSocketDeviceResolver.IsNothingPlugged(device))
        {
            return $"Power Socket {ordinal}";
        }
        return _deviceIndex.TryGetValue(device!, out var info)
            ? $"Power Socket {ordinal} ({info.FriendlyName})"
            : $"Power Socket {ordinal} (in use)";
    }

    /// <summary>
    /// Links a socket to the device it powers when that device is a container in this save, so the
    /// UI can jump to it in the CONTAINERS tab. Non-container devices (cables, batteries, plug
    /// strips) and cross-save devices get no link (nothing to open here).
    /// </summary>
    protected override (string? TargetId, string? Label, bool NeedsHostResolution) LinkFor(
        string key, IList<FPropertyTag> props)
    {
        var device = props.GetString(PluggedInDevicePrefix);
        if (PowerSocketDeviceResolver.IsNothingPlugged(device))
        {
            return (null, null, false);
        }
        if (_deviceIndex.TryGetValue(device!, out var info))
        {
            // Resolved in this save: only containers can be opened (cables/batteries cannot).
            return info.IsContainer ? (info.Id, $"Open {info.FriendlyName}", false) : (null, null, false);
        }
        // Not in this save: the device lives in another region save. The host resolves its friendly
        // name and container-ness folder-wide and fills in the description / open action.
        return (device, "Find plugged-in device", true);
    }

    /// <summary>
    /// Reads one power-socket entry's struct into the typed display/edit fields:
    /// <list type="bullet">
    ///   <item><c>socketId</c> (ReadOnly) – the socket's own GUID string.</item>
    ///   <item><c>pluggedInDevice</c> (ReadOnly) – asset ID of the plugged-in device; "-1" means nothing is plugged in.</item>
    ///   <item><c>hasTimer</c> (Bool, editable) – whether a timer is currently armed on this socket.</item>
    ///   <item><c>timerMode</c> (ReadOnly) – the <c>E_PowerTimerModes</c> enumerator value; read-only because the
    ///     full enumerator set cannot be determined from save data alone.</item>
    ///   <item><c>extraDevices</c> (ReadOnly) – count of extra powered device asset IDs.</item>
    /// </list>
    /// </summary>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var socketId = props.GetString(PowerSocketPrefix);
        var pluggedInDevice = DescribeDevice(props.GetString(PluggedInDevicePrefix));
        var hasTimer = props.TryGetBool(HasTimerPrefix) ?? false;
        var timerMode = props.GetEnumString(TimerModePrefix);

        // ExtraPoweredDeviceAssetIDs_ is an ArrayProperty; surface its element count (0 when absent).
        var extraDeviceCount = props.FindByPrefix(ExtraDevicesPrefix)?.Property is ArrayProperty arr
            ? arr.Value?.Length ?? 0
            : 0;

        return new[]
        {
            WorldMapField.ReadOnly("socketId", "Socket ID", socketId,
                hint: "The socket's own persistent GUID string (matches the map entry key)."),
            // pluggedInDevice resolves the device asset id to a friendly name (plug board, crafting
            // bench, battery, ...) using this save's deployable index. When the device is a
            // container in this save, the detail pane also offers a button to jump to it.
            WorldMapField.ReadOnly("pluggedInDevice", "Plugged-in device", pluggedInDevice,
                hint: "The device currently drawing power from this socket. When it is a container "
                    + "(bench, fridge, crate, ...) in this region you can open it from the link "
                    + "below. \"Nothing plugged in\" means the socket is free."),
            WorldMapField.Bool("hasTimer", "Timer armed", hasTimer,
                hint: "Whether this socket's power timer is armed. Set to true to arm the timer, "
                    + "false to disarm it (no timer active)."),
            WorldMapField.ReadOnly("timerMode", "Timer mode", timerMode,
                hint: "E_PowerTimerModes enumerator value. Read-only: only NewEnumerator0 has ever "
                    + "been observed, so the full set of valid modes is unknown - changing it "
                    + "blindly could corrupt the socket's behaviour."),
            WorldMapField.ReadOnly("extraDevices", "Extra powered devices",
                extraDeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hint: "Number of additional device asset IDs in the ExtraPoweredDeviceAssetIDs array."),
        };
    }

    /// <summary>
    /// Patches one field of one power-socket entry's struct. Only <c>hasTimer</c> is
    /// editable; all other fields are read-only. Returns <see cref="WorldEditResult.NoChange"/>
    /// when the requested value already matches; never throws.
    /// </summary>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "hasTimer", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure(
                $"unknown or read-only field '{fieldId}'. Editable field: hasTimer.");
        }

        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = props.TryGetBool(HasTimerPrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }

        return WorldMapAccessor.SetBool(props, HasTimerPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the HasTimer field is missing from this power-socket entry.");
    }

    /// <summary>
    /// Turns a raw plugged-in-device asset id into a readable description: "nothing plugged in",
    /// a friendly device name when the id resolves in this save, or the short id when it does not
    /// (a device in another region save, or a stale reference).
    /// </summary>
    private string DescribeDevice(string? assetId)
    {
        if (PowerSocketDeviceResolver.IsNothingPlugged(assetId))
        {
            return "Nothing plugged in";
        }
        if (_deviceIndex.TryGetValue(assetId!, out var info))
        {
            var kind = info.IsContainer ? "container" : "device";
            return $"{info.FriendlyName} ({kind})";
        }
        // The device lives in another region save (common: sub-level sockets power hub devices).
        // The host resolves the friendly name folder-wide and rewrites this; until then, the
        // "Find plugged-in device" link can locate it.
        return "Device in another region save (use the link below to locate it)";
    }
}
