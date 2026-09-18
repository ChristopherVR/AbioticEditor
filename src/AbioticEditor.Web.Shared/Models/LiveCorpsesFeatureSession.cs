using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="CorpsesFeatureId"/> ("Corpses", <see cref="CorpseMapFeature"/>'s live twin) -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c> already
/// binds to, the same shape <see cref="LiveButtonsFeatureSession"/> uses.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump of
/// <c>CharacterCorpse_ParentBP_C</c> - see <see cref="LiveCorpsesChannel"/> and the Lua module's own
/// header comment (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/corpses.lua</c>) for the
/// full mapping. Unlike every other live world-map feature session so far, this one really does
/// support removal live: no blueprint function cleanly despawns a corpse (checked against the dump,
/// matching round 78's identical finding for tamed pets), so removal destroys the actor outright
/// via <c>K2_DestroyActor</c> - no undo, same as <c>LivePetsChannel.RemoveAsync</c>.</para>
/// </summary>
public sealed class LiveCorpsesFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>CorpseMapFeature.Id</c> (Core/WorldSaves/Features/CorpseMapFeature.cs).</summary>
    public const string CorpsesFeatureId = "corpses";

    private readonly LiveCorpsesChannel _channel;

    private LiveCorpsesFeatureSession(LiveCorpsesChannel channel, LiveCorpseDirectory directory)
    {
        _channel = channel;
        Corpses = directory.Corpses;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveCorpsesFeatureSession> ConnectAsync(
        LiveCorpsesChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveCorpsesFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveCorpse> Corpses { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: a removal already reached the running game by the time it returns,
    /// so there is never a client-side staged copy - see <see cref="LivePortalsFeatureSession.IsDirty"/>.</summary>
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
        Corpses = directory.Corpses;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField BoolOrUnavailable(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.ReadOnly(id, label, known ? "Yes" : "No", hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this property off this corpse actor right now (it may have just "
                    + "unloaded). Try again after the next refresh, or check the save file instead.");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, CorpsesFeatureId, StringComparison.Ordinal)) return null;
        var entries = Corpses.Select(c => new WorldMapEntry(
            c.Id,
            c.Label,
            new[]
            {
                BoolOrUnavailable("gibbed", "Gibbed", c.Gibbed, "whether this corpse was gibbed rather than left as a body"),
                BoolOrUnavailable("looted", "Looted", c.Looted, "whether the player has already looted this corpse"),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            CorpsesFeatureId, "Corpses",
            "NPC corpses currently loaded in the world. Remove one to despawn it immediately (and any loot "
                + "still on it) - there is no undo.",
            MapName: "CorpseMap", SupportsRemoval: true, RemoveActionLabel: "Remove this Corpse", entries);
    }

    public Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
        => Task.FromResult(string.Equals(featureId, CorpsesFeatureId, StringComparison.Ordinal)
            ? WorldEditResult.Failure($"unknown field '{fieldId}': corpses have no editable fields live, remove the entry instead.")
            : WorldEditResult.Failure("this feature has no live equivalent."));

    public async Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
    {
        if (!string.Equals(featureId, CorpsesFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        var current = Corpses.FirstOrDefault(c => string.Equals(c.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("this corpse was not found (it may already be gone).");

        try
        {
            await _channel.RemoveAsync(entryKey).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when the destroy call itself failed -
            // surface it as-is rather than a generic failure.
            return WorldEditResult.Failure(ex.Message);
        }
        // Drop the corpse from the local list and repaint right away, instead of making the caller
        // wait on the corpses.list world scan below too - a removed corpse should disappear from
        // the map feature list the moment the game confirms the destroy. That scan still runs
        // right after, in the background, purely as reconciliation.
        Corpses = Corpses.Where(c => !string.Equals(c.Id, entryKey, StringComparison.Ordinal)).ToList();
        Changed?.Invoke();
        _ = ReconcileAsync();
        return WorldEditResult.Success;
    }

    /// <summary>Best-effort background re-read after a removal already applied its own result to
    /// the local model and repainted. Never lets a reconciliation failure surface as an error for a
    /// removal that already succeeded.</summary>
    private async Task ReconcileAsync()
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch { /* best-effort; the confirmed removal already applied to the local model above */ }
    }
}
