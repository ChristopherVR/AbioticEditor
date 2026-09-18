namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live NPC-spawner editing: the live twin of
/// <c>Core/WorldSaves/Features/NpcSpawnMapFeature.cs</c> (the save's <c>NPCSpawnMap</c>, whose
/// leaves are <c>CurrentCooldownRemaining_</c>/<c>LastDayOnCooldown_</c>/<c>SpawnCount_</c>/
/// <c>HasSpawnedOnce_</c>/<c>MinutesPassedCooldownStarted_</c>/<c>HasBeenEncounteredOnce_</c>).
/// Lists every loaded NPC spawner actor and lets a host reset a spawner's cooldown or force it to
/// spawn immediately - see <c>npcspawns.list</c>/<c>npcspawns.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/npcspawns.lua</c> for the full field
/// mapping and citations (round 102, grounded in a real CUE4Parse class+bytecode dump).
///
/// <para><b>Class discovery covers three roots</b>, not one (a genuine correction found while
/// grounding this against the dump, not the original task assumption of a single hierarchy):
/// the overwhelming majority of spawner classes chain up to <c>Abiotic_NPCSpawn_ParentBP_C</c>
/// (every zombie/pest/gatekeeper/order/pillager/darklens/security-bot/peccary/winter-sprite/
/// single-grunt family confirmed from the dump's own <c>super=</c> chain), but
/// <c>NPCSpawn_Entity_C</c> and <c>NPCSpawn_Narrative_C</c> declare <c>super=Actor</c> directly -
/// two genuinely separate roots, added as additional sweeps on the Lua side. Neither exposes any
/// of the cooldown/count system below at all, so their rows always report
/// <see cref="LiveNpcSpawn.Controllable"/> = false with every state field null.</para>
///
/// <para><b>Cooldown/count state lives partly on the spawner actor itself and partly on a native
/// world subsystem</b>, <c>AIDirectorSubsystem</c> (confirmed from the spawner's own bytecode,
/// which calls <c>GetCurrentCooldownRemainingFromSpawner</c>/
/// <c>GetHasBeenEncounteredOnceForSpawner</c>/<c>GetSpawnedAIFromSpawner</c> on it via
/// <c>SubsystemBlueprintLibrary::GetWorldSubsystem</c>, passing itself as the spawner argument):
/// <see cref="LiveNpcSpawn.OnCooldown"/> is the spawner's own <c>IsOnCooldown()</c> function;
/// <see cref="LiveNpcSpawn.CooldownRemainingSeconds"/>/<see cref="LiveNpcSpawn.CooldownDaysRemaining"/>/
/// <see cref="LiveNpcSpawn.HasBeenEncounteredOnce"/> come from the subsystem;
/// <see cref="LiveNpcSpawn.HasSpawnedOnce"/> is the spawner's own direct <c>HasSpawnedOnce</c>
/// property; <see cref="LiveNpcSpawn.SpawnCount"/> is the spawner's own
/// <c>GetCurrentSpawnedCount(false)</c> function. The offline save leaf
/// <c>MinutesPassedCooldownStarted_</c> has no confirmed live counterpart anywhere in the dump and
/// is not exposed here.</para>
///
/// <para><see cref="ResetCooldownAsync"/>/<see cref="ForceSpawnAsync"/> are momentary, not
/// persistent state. <c>resetCooldown</c> calls the spawner's own real
/// <c>SetSpawnOnCooldown(0.0, 0)</c> function (confirmed: passing <c>InCurrentDay=0</c> makes the
/// function look up "today" itself off the level's <c>DayNightManager</c>, so this one call means
/// exactly "let this spawner fire again right now"). <c>forceSpawn</c> calls the spawner's own
/// <c>TrySpawnNPCNew(false, true, false)</c> (falling back to the older <c>TrySpawnNPC</c> with the
/// same arguments on a build that lacks it) - <c>ForceSuccessByTrigger=true</c> is confirmed from
/// the bytecode to bypass multiple individual spawn-gating checks, but whether the resulting NPC
/// actually appears is NOT confirmed by any live capture, only by this bytecode reading - a call
/// that does not error is reported as requested, not a confirmed spawn.</para>
///
/// <para><b>Round 110:</b> <see cref="SetCooldownRemainingAsync"/> is a genuine, persistent value
/// write (not a momentary toggle): <c>SetSpawnOnCooldown</c>'s own bytecode was fully traced and
/// confirmed to pass its <c>TimeRemaining</c> argument through to
/// <c>AIDirectorSubsystem:SetCooldownForSpawner</c> unchanged (no clamping or zeroing), so calling
/// it with an arbitrary value and <c>InCurrentDay=0</c> sets <c>CooldownRemainingSeconds</c> to
/// exactly that value while the cooldown day resolves to "today" the same way a reset already
/// does. The offline save leaf <c>MinutesPassedCooldownStarted_</c> still has no live counterpart:
/// the function's day argument is whole-day granularity only (fed from the level's
/// <c>DayNightManager.CurrentDay</c>, itself a whole-day counter), with no minutes-within-the-day
/// component to derive or set it from.</para>
/// </summary>
public sealed class LiveNpcSpawnsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveNpcSpawnDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("npcspawns.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var spawners = (wire.Spawners ?? [])
            .Select(s => new LiveNpcSpawn(s.Id, s.Label, s.Controllable, s.OnCooldown, s.CooldownRemainingSeconds,
                s.CooldownDaysRemaining, s.HasSpawnedOnce, s.HasBeenEncounteredOnce, s.SpawnCount, s.X, s.Y, s.Z))
            .ToList();
        return new LiveNpcSpawnDirectory(spawners, wire.IsHost);
    }

    /// <summary>Resets one spawner's cooldown immediately (the game's own <c>SetSpawnOnCooldown(0, 0)</c>).</summary>
    public Task ResetCooldownAsync(string id, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("npcspawns.set",
            new SetWire([new EditWire(id, ResetCooldown: true, ForceSpawn: null, CooldownRemainingSeconds: null)]),
            cancellationToken);

    /// <summary>Attempts to force one spawner to spawn immediately (the game's own
    /// <c>TrySpawnNPCNew</c>/<c>TrySpawnNPC</c> with <c>ForceSuccessByTrigger=true</c>). See the
    /// class remarks: a call that does not error is reported as requested, not a confirmed spawn.</summary>
    public Task ForceSpawnAsync(string id, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("npcspawns.set",
            new SetWire([new EditWire(id, ResetCooldown: null, ForceSpawn: true, CooldownRemainingSeconds: null)]),
            cancellationToken);

    /// <summary>Sets one spawner's cooldown to an exact number of seconds remaining immediately
    /// (round 110; the game's own <c>SetSpawnOnCooldown(seconds, 0)</c> - see the class remarks).
    /// Unlike <see cref="ResetCooldownAsync"/>/<see cref="ForceSpawnAsync"/>, this is a real
    /// persistent value, not a momentary toggle.</summary>
    public Task SetCooldownRemainingAsync(string id, double seconds, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("npcspawns.set",
            new SetWire([new EditWire(id, ResetCooldown: null, ForceSpawn: null, CooldownRemainingSeconds: seconds)]),
            cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<SpawnerWire>? Spawners, bool IsHost);
    private sealed record SpawnerWire(string Id, string Label, bool Controllable, bool? OnCooldown,
        double? CooldownRemainingSeconds, double? CooldownDaysRemaining, bool? HasSpawnedOnce,
        bool? HasBeenEncounteredOnce, double? SpawnCount, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Spawners);
    private sealed record EditWire(string Id, bool? ResetCooldown, bool? ForceSpawn, double? CooldownRemainingSeconds);
}

/// <summary>One loaded NPC spawner actor of any class (see the discovery note on
/// <see cref="LiveNpcSpawnsChannel"/>). <paramref name="Id"/> is the game's full object name for
/// this exact actor; <paramref name="Label"/> is its real class name. <paramref
/// name="Controllable"/> is false for the two additional-root families (no cooldown/count system
/// at all) or any future class reached through either root that turns out not to have it either -
/// every other field is null in that case rather than a guessed value. <paramref
/// name="CooldownDaysRemaining"/>/<paramref name="SpawnCount"/> are carried as <c>double?</c> on
/// the wire (whole numbers in practice) to match the JSON round trip every other numeric field in
/// this protocol already uses.</summary>
public sealed record LiveNpcSpawn(string Id, string Label, bool Controllable, bool? OnCooldown,
    double? CooldownRemainingSeconds, double? CooldownDaysRemaining, bool? HasSpawnedOnce,
    bool? HasBeenEncounteredOnce, double? SpawnCount, double X, double Y, double Z);

/// <summary>Every loaded NPC spawner plus whether this process has host authority to change them.</summary>
public sealed record LiveNpcSpawnDirectory(IReadOnlyList<LiveNpcSpawn> Spawners, bool IsHost);
