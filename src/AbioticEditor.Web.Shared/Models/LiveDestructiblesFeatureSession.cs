using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="DestructiblesFeatureId"/> ("Breakable Objects", <see cref="DestructibleMapFeature"/>'s
/// live twin) - implements the same <see cref="IWorldFeaturesSession"/> boundary
/// <c>WorldFeaturesTab</c> already binds to, the same shape <see cref="LiveButtonsFeatureSession"/>
/// uses.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and
/// Abiotic_GenericDestructible_BP_C's own blueprint bytecode - see <see
/// cref="LiveDestructiblesChannel"/> and the Lua module's own header comment
/// (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/destructibles.lua</c>) for the full
/// mapping. <c>broken</c> is a real, independently settable live property (<c>actor.Broken</c>); a
/// null value only means this particular actor could not be read right now (renders as a read-only
/// "not available live" row rather than a toggle). Setting it back to <c>false</c> ("repair") is
/// refused - see the field's own hint text - because no live game function restores the intact
/// mesh/collision once broken (confirmed from <c>OnRep_Broken</c>'s own bytecode, which does
/// nothing at all when <c>Broken</c> is false).</para>
/// </summary>
public sealed class LiveDestructiblesFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>DestructibleMapFeature.Id</c> (Core/WorldSaves/Features/DestructibleMapFeature.cs).</summary>
    public const string DestructiblesFeatureId = "destructibles";

    private readonly LiveDestructiblesChannel _channel;

    private LiveDestructiblesFeatureSession(LiveDestructiblesChannel channel, LiveDestructibleDirectory directory)
    {
        _channel = channel;
        Destructibles = directory.Destructibles;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveDestructiblesFeatureSession> ConnectAsync(
        LiveDestructiblesChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveDestructiblesFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveDestructible> Destructibles { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: an edit already reached the running game by the time it returns, so
    /// there is never a client-side staged copy - see <see cref="LivePortalsFeatureSession.IsDirty"/>.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) - see LivePortalsFeatureSession's identical remark.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Destructibles = directory.Destructibles;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, DestructiblesFeatureId, StringComparison.Ordinal)) return null;
        var entries = Destructibles.Select(d => new WorldMapEntry(
            d.Id,
            d.Label,
            new[]
            {
                d.Broken is { } known
                    ? WorldMapField.Bool("broken", "Broken", known,
                        hint: "true = destroyed/broken. Applies live immediately using the game's own break "
                            + "path (mesh, collision, and effects). Setting this back to false is refused: no "
                            + "live game function restores the intact mesh/collision once broken (edit the "
                            + "save file instead to repair it offline).")
                    : WorldMapField.ReadOnly("broken", "Broken", "not available live",
                        hint: "Could not read this property off this object right now (it may have just "
                            + "unloaded). Try again after the next refresh, or edit it in the save file instead."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            DestructiblesFeatureId, "Breakable Objects",
            "Ice walls, spore webbing, ceiling tiles and other breakable world objects: break one immediately. "
                + "There is no live way to repair one once broken - edit the save file instead.",
            MapName: "DestructibleMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, DestructiblesFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (!string.Equals(fieldId, "broken", StringComparison.Ordinal))
        {
            return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        }
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = Destructibles.FirstOrDefault(d => string.Equals(d.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("this object was not found (it may have been unloaded).");

        // Rejected before even reaching the running game: no live path repairs an already-broken
        // object, ever - see the class remarks and destructibles.lua's own header comment. A round
        // trip would only come back with the exact same refusal.
        if (!wanted)
        {
            return WorldEditResult.Failure("this object cannot be repaired live - the game's own OnRep_Broken "
                + "does nothing once Broken is set back to false, and no other live function restores the "
                + "intact mesh/collision once it has broken (edit the save file directly for that).");
        }

        // A null current value means "could not read this property off this actor right now" (see
        // the class remarks), not "false" - it never matches wanted, so the edit is still attempted
        // below rather than silently reported as NoChange.
        if (current.Broken == true) return WorldEditResult.NoChange;

        try
        {
            await _channel.SetAsync([new LiveDestructibleEdit(entryKey, Broken: true)]).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when it could not find a live
            // property for this field - surface it as-is rather than a generic failure.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("breakable objects cannot be removed."));
}
