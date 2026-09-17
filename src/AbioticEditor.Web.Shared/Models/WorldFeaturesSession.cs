using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for the world-map features browser (buttons, elevators, resource nodes,
/// power sockets, teleporter pads, world teleporters/portals, entitlements, ...), implemented by
/// the file session (<see cref="WorldSaveSession"/>, every feature the loaded save carries) and a
/// live session per feature that has one: <see cref="LivePortalsFeatureSession"/> (the "World
/// Teleporters" pads, evidenced live UObject path) and <see cref="LiveButtonsFeatureSession"/>
/// (world buttons - id/position are evidenced the same way, but its four state fields are
/// best-effort/unconfirmed, see that class's own remarks). <c>WorldFeaturesTab</c> binds to
/// this interface alone.
/// </summary>
public interface IWorldFeaturesSession
{
    /// <summary>The loaded save's folder path (file session) or empty (live: nothing to resolve
    /// cross-region names against - a live read already reflects the actual running world).</summary>
    string Path { get; }

    /// <summary>Deployables known to this session, for the power-sockets feature's "what does
    /// this power" lookup. Empty for any session/feature that never needs it.</summary>
    IReadOnlyList<WorldDeployable> Deployables { get; }

    /// <summary>Decoded state of one map-backed feature, or null when this session has no such feature.</summary>
    WorldMapFeatureSnapshot? MapFeature(string featureId);

    /// <summary>
    /// Sets one field of one entry. A file session completes this synchronously (an in-memory
    /// tree edit, wrapped in a completed <see cref="Task{TResult}"/>); a live session awaits an
    /// actual round trip to the running game.
    /// </summary>
    Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value);

    /// <summary>Removes one entry, when the feature supports it.</summary>
    Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey);
}
