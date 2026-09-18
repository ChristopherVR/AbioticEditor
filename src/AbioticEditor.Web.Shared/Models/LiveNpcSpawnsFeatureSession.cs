using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="NpcSpawnsFeatureId"/> ("NPC Spawns", <see cref="NpcSpawnMapFeature"/>'s live twin) -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, the same shape <see cref="LiveButtonsFeatureSession"/> uses.
///
/// <para>See <see cref="LiveNpcSpawnsChannel"/> for the full field mapping and citations. Most
/// state fields are read-only info (<c>onCooldown</c>/<c>cooldownDaysRemaining</c>/
/// <c>hasSpawnedOnce</c>/<c>hasBeenEncounteredOnce</c>/<c>spawnCount</c>), each null when this
/// actor could not be read right now rather than a guessed value. <c>resetCooldown</c> and
/// <c>forceSpawn</c> are momentary "do it now" toggles rather than persistent state - each always
/// reads back <c>false</c> so the checkbox resets after a refresh, matching how the game itself has
/// no notion of these as stored values. <c>cooldownRemainingSeconds</c> (round 110) is different: a
/// real, persistent editable number (the game's own <c>SetSpawnOnCooldown(seconds, 0)</c>, its
/// <c>TimeRemaining</c> argument confirmed to pass through unclamped) - it reads back whatever the
/// game now reports, not a reset checkbox.</para>
/// </summary>
public sealed class LiveNpcSpawnsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>NpcSpawnMapFeature.Id</c> (Core/WorldSaves/Features/NpcSpawnMapFeature.cs).</summary>
    public const string NpcSpawnsFeatureId = "npc-spawns";

    private readonly LiveNpcSpawnsChannel _channel;

    private LiveNpcSpawnsFeatureSession(LiveNpcSpawnsChannel channel, LiveNpcSpawnDirectory directory)
    {
        _channel = channel;
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though npcspawns.lua already keys every row by the actor's own unique full name.
        Spawners = LiveFeatureRows.DistinctById(directory.Spawners, s => s.Id);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveNpcSpawnsFeatureSession> ConnectAsync(
        LiveNpcSpawnsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveNpcSpawnsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveNpcSpawn> Spawners { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: an action already reached the running game by the time it returns,
    /// so there is never a client-side staged copy - see <see cref="LivePortalsFeatureSession.IsDirty"/>.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) when it chooses to refresh this session at all -
    // see LiveConnect.razor's own comment on why "npc-spawns" is deliberately excluded from that
    // loop (the same reasoning as the creatures/"npcs" session: this backs onto a full sweep of
    // potentially hundreds of spawner actors).
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though npcspawns.lua already keys every row by the actor's own unique full name.
        Spawners = LiveFeatureRows.DistinctById(directory.Spawners, s => s.Id);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField BoolInfo(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.ReadOnly(id, label, known ? "true" : "false", hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this spawner actor right now (it may have just unloaded).");

    private static WorldMapField NumberInfo(string id, string label, double? value, string hint)
        => value is { } known
            ? WorldMapField.ReadOnly(id, label, known.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this off this spawner actor right now (it may have just unloaded).");

    /// <summary>Round 110: unlike the other numeric fields on this row, cooldown remaining is a
    /// real editable value (the game's own <c>SetSpawnOnCooldown</c>, confirmed to pass its
    /// seconds argument through unclamped) - editable whenever this spawner type is controllable
    /// and a current value could be read; otherwise read-only, matching <see cref="ActionField"/>'s
    /// own "no known live control" / "could not read right now" split.</summary>
    private static WorldMapField CooldownRemainingField(double? value, bool controllable)
    {
        if (!controllable)
        {
            return WorldMapField.ReadOnly("cooldownRemainingSeconds", "Cooldown remaining (s)", "not available live",
                hint: "This spawner type has no known live cooldown control.");
        }
        if (value is { } known)
        {
            return WorldMapField.Number("cooldownRemainingSeconds", "Cooldown remaining (s)", known,
                hint: "Seconds left on this spawner's cooldown, read live from the game's own spawn director. "
                    + "Editable: set an exact value (the game's own SetSpawnOnCooldown). Applies live immediately.");
        }
        return WorldMapField.ReadOnly("cooldownRemainingSeconds", "Cooldown remaining (s)", "not available live",
            hint: "Could not read this off this spawner actor right now (it may have just unloaded).");
    }

    private static WorldMapField ActionField(string id, string label, bool controllable, string hint)
        => controllable
            ? WorldMapField.Bool(id, label, false, hint: hint)
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "This spawner type has no known live control for this action.");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, NpcSpawnsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Spawners.Select(s => new WorldMapEntry(
            s.Id,
            s.Label,
            new[]
            {
                BoolInfo("onCooldown", "On cooldown", s.OnCooldown, "Whether this spawner is currently waiting out its cooldown."),
                CooldownRemainingField(s.CooldownRemainingSeconds, s.Controllable),
                NumberInfo("cooldownDaysRemaining", "Cooldown days remaining", s.CooldownDaysRemaining,
                    "In-game days left on this spawner's day-based cooldown (a different figure from the offline "
                        + "save's \"last day on cooldown\" field - this is a live days-REMAINING count)."),
                BoolInfo("hasSpawnedOnce", "Has spawned once", s.HasSpawnedOnce, "Whether this spawner has ever fired."),
                BoolInfo("hasBeenEncounteredOnce", "Has been encountered once", s.HasBeenEncounteredOnce,
                    "Whether the player has ever encountered this spawner's NPC."),
                NumberInfo("spawnCount", "Spawn count", s.SpawnCount, "How many NPCs this spawner currently has spawned."),
                ActionField("resetCooldown", "Reset cooldown now", s.Controllable,
                    "Tick to let this spawner fire again immediately (the game's own SetSpawnOnCooldown(0, 0)). "
                        + "Applies live immediately; this box always shows unticked afterward."),
                ActionField("forceSpawn", "Force spawn now", s.Controllable,
                    "Tick to ask this spawner to spawn immediately, bypassing its normal checks (the game's own "
                        + "TrySpawnNPC with ForceSuccessByTrigger). Best-effort: a successful request means the "
                        + "game accepted the call, not a confirmed spawn. Applies live immediately; this box "
                        + "always shows unticked afterward."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            NpcSpawnsFeatureId, "NPC Spawns",
            "NPC spawner actors currently loaded in this region: set an exact cooldown, reset a spawner's "
                + "cooldown, or force it to spawn now.",
            MapName: "NPCSpawnMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, NpcSpawnsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (fieldId is not ("resetCooldown" or "forceSpawn" or "cooldownRemainingSeconds"))
        {
            return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        }

        var current = Spawners.FirstOrDefault(s => string.Equals(s.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("NPC spawner not found (it may have been unloaded).");
        if (!current.Controllable)
        {
            return WorldEditResult.Failure("this spawner type has no known live control (unrecognized class).");
        }

        try
        {
            if (fieldId == "cooldownRemainingSeconds")
            {
                // Round 110: a real persistent value, not a momentary toggle - see the class
                // remarks. Parsed/compared the same way every other Number field in this codebase
                // is (WorldMapAccessor.TryParseDouble, matching NpcSpawnMapFeature's own offline
                // ApplyCooldownRemaining).
                if (!WorldMapAccessor.TryParseDouble(value, out var wantedSeconds))
                {
                    return WorldEditResult.Failure($"'{value}' is not a valid number.");
                }
                if (current.CooldownRemainingSeconds == wantedSeconds) return WorldEditResult.NoChange;
                await _channel.SetCooldownRemainingAsync(entryKey, wantedSeconds).ConfigureAwait(false);
            }
            else
            {
                if (!WorldMapAccessor.TryParseBool(value, out var wanted))
                {
                    return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
                }
                // Unticking the momentary action box is a no-op: there is nothing to undo, it was
                // never "set" as persistent state to begin with.
                if (!wanted) return WorldEditResult.NoChange;

                if (fieldId == "resetCooldown")
                {
                    await _channel.ResetCooldownAsync(entryKey).ConfigureAwait(false);
                }
                else
                {
                    await _channel.ForceSpawnAsync(entryKey).ConfigureAwait(false);
                }
            }
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when it could not find a live
            // control for this field/action (see npcspawns.lua's own header comment) - surface it
            // as-is.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("NPC spawners cannot be removed."));
}
