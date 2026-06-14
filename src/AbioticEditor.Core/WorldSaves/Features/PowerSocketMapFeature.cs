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
        "View power-socket state and arm or disarm the timer for each socket. Remove a socket to reset it.";

    /// <summary>Socket keys are raw GUIDs, so number them and note what's plugged in.</summary>
    protected override string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
    {
        var device = props.GetString(PluggedInDevicePrefix);
        var plugged = !string.IsNullOrWhiteSpace(device) && device is not ("-1" or "None");
        return plugged ? $"Power Socket {ordinal} (in use)" : $"Power Socket {ordinal}";
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
        var pluggedInDevice = props.GetString(PluggedInDevicePrefix);
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
            WorldMapField.ReadOnly("pluggedInDevice", "Plugged-in device", pluggedInDevice,
                hint: "Asset ID of the device currently plugged into this socket; \"-1\" means nothing is plugged in."),
            WorldMapField.Bool("hasTimer", "Timer armed", hasTimer,
                hint: "true = a timer is armed on this socket; false = no timer active."),
            WorldMapField.ReadOnly("timerMode", "Timer mode", timerMode,
                hint: "E_PowerTimerModes enumerator value. Read-only: only NewEnumerator0 observed in saves; full enum set unknown."),
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
}
