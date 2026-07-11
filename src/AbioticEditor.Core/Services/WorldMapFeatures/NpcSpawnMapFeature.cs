using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>NPCSpawnMap</c> (region saves): each NPC spawner actor stores its
/// cooldown state, spawn count, and encounter flags. Editing these fields lets a player
/// reset spawner cooldowns or manipulate spawn counts without waiting for the in-game
/// cooldown to expire.
///
/// <para>Each entry value is a <c>StructProperty → PropertiesStruct</c> with the following
/// editable leaves (blueprint-hash suffix is matched by prefix):</para>
/// <list type="bullet">
///   <item><term><c>CurrentCooldownRemaining_</c></term><description>Seconds until this spawner can fire again (DoubleProperty). Set to 0 to allow an immediate spawn.</description></item>
///   <item><term><c>LastDayOnCooldown_</c></term><description>Game day when the cooldown was last set (IntProperty).</description></item>
///   <item><term><c>SpawnCount_</c></term><description>Total number of times this spawner has spawned (IntProperty).</description></item>
///   <item><term><c>HasSpawnedOnce_</c></term><description>Whether this spawner has ever fired (BoolProperty).</description></item>
///   <item><term><c>MinutesPassedCooldownStarted_</c></term><description>In-game minutes elapsed when the current cooldown started (IntProperty).</description></item>
///   <item><term><c>HasBeenEncounteredOnce_</c></term><description>Whether the player has ever encountered this spawner's NPC (BoolProperty).</description></item>
/// </list>
/// </summary>
public sealed class NpcSpawnMapFeature : WorldMapFeatureBase
{
    // ---------- stable prefix constants ----------

    private const string CooldownRemainingPrefix = "CurrentCooldownRemaining_";
    private const string LastDayOnCooldownPrefix = "LastDayOnCooldown_";
    private const string SpawnCountPrefix = "SpawnCount_";
    private const string HasSpawnedOncePrefix = "HasSpawnedOnce_";
    private const string MinutesCooldownStartedPrefix = "MinutesPassedCooldownStarted_";
    private const string HasBeenEncounteredOncePrefix = "HasBeenEncounteredOnce_";

    // ---------- field id constants ----------

    private const string FieldCooldownRemaining = "cooldownRemaining";
    private const string FieldLastDayOnCooldown = "lastDayOnCooldown";
    private const string FieldSpawnCount = "spawnCount";
    private const string FieldSpawnedOnce = "spawnedOnce";
    private const string FieldMinutesCooldownStarted = "minutesCooldownStarted";
    private const string FieldEncounteredOnce = "encounteredOnce";

    /// <inheritdoc/>
    public override string Id => "npc-spawns";

    /// <inheritdoc/>
    public override string MapName => "NPCSpawnMap";

    /// <inheritdoc/>
    public override string DisplayName => "NPC Spawns";

    /// <inheritdoc/>
    public override string Description =>
        "Reset spawner cooldowns / spawn counts for NPC spawner actors in the region.";

    /// <summary>
    /// NPC spawners are fixed level actors: deleting one's persisted state would strip the
    /// spawner from the world rather than do anything useful, so the per-entry remove action is
    /// disabled. Edit the cooldown/spawn fields instead.
    /// </summary>
    public override bool SupportsRemoval => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var cooldownRemaining = props.GetDouble(CooldownRemainingPrefix);
        var lastDayOnCooldown = props.GetLong(LastDayOnCooldownPrefix);
        var spawnCount = props.GetLong(SpawnCountPrefix);
        var spawnedOnce = props.TryGetBool(HasSpawnedOncePrefix) ?? false;
        var minutesCooldownStarted = props.GetLong(MinutesCooldownStartedPrefix);
        var encounteredOnce = props.TryGetBool(HasBeenEncounteredOncePrefix) ?? false;

        return new[]
        {
            WorldMapField.Number(FieldCooldownRemaining, "Cooldown remaining (s)", cooldownRemaining,
                hint: "Seconds until this spawner can fire again; set 0 to allow an immediate spawn"),
            WorldMapField.Integer(FieldLastDayOnCooldown, "Last day on cooldown", lastDayOnCooldown,
                hint: "In-game day when the cooldown was last set"),
            WorldMapField.Integer(FieldSpawnCount, "Spawn count", spawnCount,
                hint: "Total number of times this spawner has spawned"),
            WorldMapField.Bool(FieldSpawnedOnce, "Has spawned once", spawnedOnce,
                hint: "Whether this spawner has ever fired"),
            WorldMapField.Integer(FieldMinutesCooldownStarted, "Minutes when cooldown started", minutesCooldownStarted,
                hint: "In-game minutes elapsed when the current cooldown started"),
            WorldMapField.Bool(FieldEncounteredOnce, "Has been encountered once", encounteredOnce,
                hint: "Whether the player has ever encountered this spawner's NPC"),
        };
    }

    /// <inheritdoc/>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        return fieldId switch
        {
            FieldCooldownRemaining => ApplyCooldownRemaining(props, value),
            FieldLastDayOnCooldown => ApplyLastDayOnCooldown(props, value),
            FieldSpawnCount => ApplySpawnCount(props, value),
            FieldSpawnedOnce => ApplySpawnedOnce(props, value),
            FieldMinutesCooldownStarted => ApplyMinutesCooldownStarted(props, value),
            FieldEncounteredOnce => ApplyEncounteredOnce(props, value),
            _ => WorldEditResult.Failure(
                $"unknown field '{fieldId}' (expected one of: {FieldCooldownRemaining}, {FieldLastDayOnCooldown}, {FieldSpawnCount}, {FieldSpawnedOnce}, {FieldMinutesCooldownStarted}, {FieldEncounteredOnce})."),
        };
    }

    // ---------- per-field apply helpers ----------

    private static WorldEditResult ApplyCooldownRemaining(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseDouble(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a valid number.");
        }
        var current = props.GetDouble(CooldownRemainingPrefix);
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetDouble(props, CooldownRemainingPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {CooldownRemainingPrefix} field is missing from this entry.");
    }

    private static WorldEditResult ApplyLastDayOnCooldown(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseInt(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a valid integer.");
        }
        var current = (int)props.GetLong(LastDayOnCooldownPrefix);
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetInt(props, LastDayOnCooldownPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {LastDayOnCooldownPrefix} field is missing from this entry.");
    }

    private static WorldEditResult ApplySpawnCount(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseInt(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a valid integer.");
        }
        var current = (int)props.GetLong(SpawnCountPrefix);
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetInt(props, SpawnCountPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {SpawnCountPrefix} field is missing from this entry.");
    }

    private static WorldEditResult ApplySpawnedOnce(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }
        var current = props.TryGetBool(HasSpawnedOncePrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetBool(props, HasSpawnedOncePrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {HasSpawnedOncePrefix} field is missing from this entry.");
    }

    private static WorldEditResult ApplyMinutesCooldownStarted(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseInt(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a valid integer.");
        }
        var current = (int)props.GetLong(MinutesCooldownStartedPrefix);
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetInt(props, MinutesCooldownStartedPrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {MinutesCooldownStartedPrefix} field is missing from this entry.");
    }

    private static WorldEditResult ApplyEncounteredOnce(IList<FPropertyTag> props, string? value)
    {
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }
        var current = props.TryGetBool(HasBeenEncounteredOncePrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetBool(props, HasBeenEncounteredOncePrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"the {HasBeenEncounteredOncePrefix} field is missing from this entry.");
    }
}
