using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// A self-contained editor for one previously-unmodeled world-save map (VehicleMap,
/// ResourceNodeMap, PowerSocketMap, ButtonMap, NPCSpawnMap, PortalMap, ElevatorMap,
/// TramMap, TriggerMap, ServerEntitlements, …). Each map is a <c>MapProperty</c> keyed by an
/// actor path / GUID whose value is a per-actor state struct; a feature exposes that state as
/// a flat list of <see cref="WorldMapEntry"/> rows of typed, optionally-editable
/// <see cref="WorldMapField"/>s.
///
/// <para>The point of the shared shape is reuse: the CLI and the app drive <b>every</b>
/// feature through this one interface (list rows, edit a field) with no per-map host code,
/// and new features are discovered automatically (see <see cref="WorldMapFeatures"/>). Read
/// stays lossless - features only ever patch existing leaf values via
/// <see cref="WorldMapAccessor"/>, never restructure the tree.</para>
/// </summary>
public interface IWorldMapFeature
{
    /// <summary>Stable lowercase token used as the CLI subcommand and settings key (e.g. <c>vehicles</c>).</summary>
    string Id { get; }

    /// <summary>The top-level save property this feature owns (e.g. <c>VehicleMap</c>).</summary>
    string MapName { get; }

    /// <summary>Short human title (e.g. <c>Vehicles</c>).</summary>
    string DisplayName { get; }

    /// <summary>One-line explanation of what editing this does, shown in the UI/CLI help.</summary>
    string Description { get; }

    /// <summary>True when <paramref name="save"/> actually carries this map (most are region-only).</summary>
    bool AppliesTo(SaveGame save);

    /// <summary>Reads every entry as typed rows. Empty when the map is absent.</summary>
    IReadOnlyList<WorldMapEntry> Read(SaveGame save);

    /// <summary>
    /// Sets one field of one entry. Implementations validate (<see cref="WorldMapField.Options"/>,
    /// numeric ranges) and patch only that leaf. Returns <see cref="WorldEditResult.Failure"/>
    /// (never throws) for an unknown entry/field or an invalid value; the caller persists the
    /// save afterwards (keeping a .bak) only when <see cref="WorldEditResult.Changed"/> is true.
    /// </summary>
    WorldEditResult SetField(SaveGame save, string entryKey, string fieldId, string? value);

    /// <summary>Label for the remove/reset button (e.g. "Remove this Entry", or "Disconnect" for sockets).</summary>
    string RemoveActionLabel { get; }

    /// <summary>
    /// True when an entry can be removed from this map. Removal drops the actor's persisted
    /// state so the game re-creates it at its blueprint default on next load. Off for maps where
    /// deleting a shared/metadata entry would be unsafe (e.g. teleporter pads, entitlements).
    /// </summary>
    bool SupportsRemoval { get; }

    /// <summary>
    /// Removes one entry from the map. Returns <see cref="WorldEditResult.Failure"/> (never
    /// throws) for an unknown key or when <see cref="SupportsRemoval"/> is false.
    /// </summary>
    WorldEditResult Remove(SaveGame save, string entryKey);
}

/// <summary>One row of a feature: a map entry (one actor) and its editable fields.</summary>
/// <param name="Key">The map key (actor path or GUID) identifying this entry.</param>
/// <param name="Label">The readable row label.</param>
/// <param name="Fields">The typed, optionally-editable fields.</param>
/// <param name="LinkTargetId">
/// Optional id of another editable entity this entry points at (e.g. the container a power socket
/// powers). When set, the UI offers a "go to" action; null when the entry links to nothing.
/// </param>
/// <param name="LinkLabel">Button text for the link action (e.g. "Open Crafting Bench"); null when none.</param>
/// <param name="LinkNeedsHostResolution">
/// True when the link target could not be resolved within this save and the host must resolve it
/// folder-wide (e.g. a power socket whose device lives in another region save). The host then fills
/// in the friendly name and decides whether the link can open anything.
/// </param>
public sealed record WorldMapEntry(
    string Key,
    string Label,
    IReadOnlyList<WorldMapField> Fields,
    string? LinkTargetId = null,
    string? LinkLabel = null,
    bool LinkNeedsHostResolution = false);

/// <summary>One typed, possibly-editable value within a <see cref="WorldMapEntry"/>.</summary>
public sealed record WorldMapField(
    string Id,
    string Label,
    string? Value,
    WorldFieldKind Kind,
    bool Editable,
    IReadOnlyList<string>? Options = null,
    string? Hint = null)
{
    /// <summary>A read-only display field (status the user can see but not change).</summary>
    public static WorldMapField ReadOnly(string id, string label, string? value, string? hint = null)
        => new(id, label, value, WorldFieldKind.Text, Editable: false, Options: null, Hint: hint);

    /// <summary>An editable boolean field.</summary>
    public static WorldMapField Bool(string id, string label, bool value, string? hint = null)
        => new(id, label, value ? "true" : "false", WorldFieldKind.Bool, Editable: true, Options: null, Hint: hint);

    /// <summary>An editable integer field.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "'Integer' is the clearest name for a whole-number field kind.")]
    public static WorldMapField Integer(string id, string label, long value, string? hint = null)
        => new(id, label, value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WorldFieldKind.Integer, Editable: true, Options: null, Hint: hint);

    /// <summary>An editable real-number field.</summary>
    public static WorldMapField Number(string id, string label, double value, string? hint = null)
        => new(id, label, value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            WorldFieldKind.Number, Editable: true, Options: null, Hint: hint);

    /// <summary>An editable field constrained to a built-in list of choices (e.g. portal tags).</summary>
    public static WorldMapField Choice(string id, string label, string? value, IReadOnlyList<string> options, string? hint = null)
        => new(id, label, value, WorldFieldKind.Enum, Editable: true, Options: options, Hint: hint);
}

/// <summary>How a <see cref="WorldMapField"/> should be parsed and rendered.</summary>
public enum WorldFieldKind
{
    Text,

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "'Integer' is the clearest name for a whole-number field kind.")]
    Integer,

    Number,
    Bool,
    Enum,
}

/// <summary>The result of a <see cref="IWorldMapFeature.SetField"/> call.</summary>
public sealed record WorldEditResult(bool Changed, string? Error)
{
    /// <summary>The edit applied and the save now needs writing.</summary>
    public static WorldEditResult Success { get; } = new(true, null);

    /// <summary>The value was already what was requested - valid, but nothing to write.</summary>
    public static WorldEditResult NoChange { get; } = new(false, null);

    /// <summary>The edit was rejected; <paramref name="error"/> says why.</summary>
    public static WorldEditResult Failure(string error) => new(false, error);

    /// <summary>True when the request was rejected (as opposed to a successful or no-op edit).</summary>
    public bool IsError => Error is not null;
}
