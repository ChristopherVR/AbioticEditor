using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>One active effect as the Vitals tab shows it.</summary>
/// <param name="Row">Buff-table row, e.g. <c>Debuff_BrokenLeg</c>.</param>
/// <param name="Limb">Limb label (<c>EBodyLimbs::Head</c>), or null for the whole body.</param>
/// <param name="Permanent">True when it stays until something removes it.</param>
/// <param name="SecondsLeft">How long it still runs, when known.</param>
public sealed record PlayerEffectEdit(string Row, string? Limb, bool Permanent, double? SecondsLeft)
{
    /// <summary>The session's own entry this row stands for (a saved <c>ActiveBuff</c> or a live effect).</summary>
    public object? Source { get; init; }

    /// <summary>True for a harmful effect: the game names those <c>Debuff…</c> (being underwater
    /// is a state, not an ailment, and is left out).</summary>
    public bool IsDebuff => IsDebuffRow(Row);

    public static bool IsDebuffRow(string row) =>
        row.StartsWith("Debuff", StringComparison.Ordinal) && row.Length > 6
        && !string.Equals(row, "Debuff_Underwater", StringComparison.Ordinal);
}

/// <summary>
/// Host-neutral boundary for a character's active effects, shared by the file session (edits
/// stage until SAVE) and the live session (each change goes straight to the running game).
/// </summary>
public interface IPlayerEffectsSession
{
    IReadOnlyList<PlayerEffectEdit> Effects { get; }

    /// <summary>False for a file (staged until SAVE), true live.</summary>
    bool AppliesImmediately { get; }

    /// <summary>False when effects can be viewed but not changed (a guest in someone else's world).</summary>
    bool CanEditEffects { get; }

    /// <summary>False when this source cannot take new effects right now (live, for a guest).</summary>
    bool CanAddEffects { get; }

    /// <summary>True when only effects the game keeps in a save can be added (a file): any other
    /// effect would be dropped the next time the game saves.</summary>
    bool OnlySavedEffects { get; }

    /// <summary>Adds <paramref name="effect"/> for its normal duration (or permanently, when the
    /// game never lets it run out).</summary>
    Task AddEffectAsync(BuffInfo effect);

    Task RemoveEffectAsync(PlayerEffectEdit effect);

    /// <summary>Removes every debuff (see <see cref="PlayerEffectEdit.IsDebuff"/>), keeping buffs.</summary>
    Task ClearDebuffsAsync();

    /// <summary>True live, where new debuffs can be stopped as they happen.</summary>
    bool SupportsDebuffBlocking { get; }

    bool BlockNewDebuffs { get; }

    Task SetBlockNewDebuffsAsync(bool enabled);
}

/// <summary>
/// Live-only survival toggles (no hunger, no radiation, invincible, ...): switches on the
/// running character that the game does not save. Keys are <see cref="Core.LiveEditing.Player.LiveSurvivalToggles"/>.
/// </summary>
public interface IPlayerTogglesSession
{
    /// <summary>Every toggle this game version has, and whether it is on.</summary>
    IReadOnlyDictionary<string, bool> Toggles { get; }

    bool CanEditToggles { get; }

    Task SetToggleAsync(string name, bool enabled);
}
