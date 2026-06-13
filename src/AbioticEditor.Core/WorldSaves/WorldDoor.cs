namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Which door map a <see cref="WorldDoor"/> came from. The two maps store
/// significantly different fields:
/// <list type="bullet">
///   <item><see cref="Simple"/> - <c>SimpleDoorMap</c>, with an enum state, a yaw
///   rotation, and a one-way-unlocked flag.</item>
///   <item><see cref="Security"/> - <c>SecurityDoorMap</c>, just a single
///   <c>IsDoorOpen</c> bool.</item>
/// </list>
/// Both kinds also carry a <c>NoReset</c> bool.
/// </summary>
public enum WorldDoorKind
{
    Simple,
    Security,
}

/// <summary>
/// A single door entry from a world save. Backs onto either <c>SimpleDoorMap</c>
/// or <c>SecurityDoorMap</c>. The two underlying structs are different, so some
/// fields are <c>null</c> depending on <see cref="Kind"/>:
/// <list type="bullet">
///   <item><c>Simple</c> doors populate <see cref="DoorState"/>, <see cref="Yaw"/>,
///   <see cref="OneWayUnlocked"/>, and <see cref="NoReset"/>.</item>
///   <item><c>Security</c> doors populate <see cref="IsDoorOpen"/> and
///   <see cref="NoReset"/>.</item>
/// </list>
/// </summary>
/// <param name="Id">
/// Map key - an actor path string like
/// <c>/Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_0</c>.
/// </param>
/// <param name="Kind">Which underlying map this door lives in.</param>
/// <param name="DoorState">
/// Simple-door enum value (e.g. <c>E_DoorStates::NewEnumerator0</c>). Stored as
/// the enum string because ByteProperty deserializes large-form enums as
/// <c>FString</c>. <c>null</c> for security doors.
/// </param>
/// <param name="Yaw">
/// Simple-door rotation (in degrees, sometimes negative). <c>null</c> for
/// security doors.
/// </param>
/// <param name="OneWayUnlocked">
/// <c>true</c> if a simple one-way door has been opened from the locked side.
/// <c>null</c> for security doors.
/// </param>
/// <param name="IsDoorOpen">
/// <c>true</c> if a security door is currently open. <c>null</c> for simple
/// doors.
/// </param>
/// <param name="NoReset">
/// If <c>true</c>, this door is exempted from session-restart resets. Present
/// on both kinds.
/// </param>
public sealed record WorldDoor(
    string Id,
    WorldDoorKind Kind,
    string? DoorState,
    double? Yaw,
    bool? OneWayUnlocked,
    bool? IsDoorOpen,
    bool? NoReset);
