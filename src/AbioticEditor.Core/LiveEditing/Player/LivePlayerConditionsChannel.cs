namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// A running character's active effects (buffs and debuffs) and survival toggles, over the live
/// agent's <c>effects.*</c> and <c>toggles.*</c> commands. Effects are added and removed through
/// the game's own buff component calls (<c>Server_ApplyBuff</c> / <c>Server_RemoveBuff</c>), the
/// same ones its save loader and items use; toggles are switches on the character the game reads
/// every tick and does not save. Both need the host.
/// </summary>
public sealed class LivePlayerConditionsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveEffectsState> GetEffectsAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<EffectsWire>("effects.list", Target(playerId), cancellationToken)
            .ConfigureAwait(false);
        var effects = (wire.Effects ?? []).Select(e => new LiveEffect(e.Row, e.Limb, e.ExpireTime)).ToList();
        return new LiveEffectsState(effects, wire.Now, wire.BlockDebuffs, wire.IsHost);
    }

    /// <summary>Applies an effect; <paramref name="seconds"/> null keeps the game's own duration.</summary>
    public Task AddEffectAsync(string row, double? seconds, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(row);
        return _channel.RequestAsync<object?>("effects.add", new AddWire(playerId, row, seconds), cancellationToken);
    }

    public Task RemoveEffectAsync(string row, int limb, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(row);
        return _channel.RequestAsync<object?>("effects.remove", new RemoveWire(playerId, row, limb), cancellationToken);
    }

    /// <summary>Removes every current debuff (rows named <c>Debuff…</c>, except being underwater).</summary>
    public async Task<int> ClearDebuffsAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<ClearWire>("effects.clear", Target(playerId), cancellationToken)
            .ConfigureAwait(false);
        return wire.Removed;
    }

    /// <summary>While on, new debuffs on this player run out at once; switching on also clears current ones.</summary>
    public async Task<bool> SetBlockDebuffsAsync(bool enabled, string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<BlockWire>("effects.block", new BlockRequest(playerId, enabled), cancellationToken)
            .ConfigureAwait(false);
        return wire.BlockDebuffs;
    }

    /// <summary>Every toggle this game version has, by name (see <see cref="LiveSurvivalToggles"/>), and whether it is on.</summary>
    public async Task<IReadOnlyDictionary<string, bool>> GetTogglesAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<TogglesWire>("toggles.get", Target(playerId), cancellationToken)
            .ConfigureAwait(false);
        return wire.Toggles ?? new Dictionary<string, bool>();
    }

    public async Task<IReadOnlyDictionary<string, bool>> SetTogglesAsync(IReadOnlyDictionary<string, bool> toggles,
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toggles);
        var wire = await _channel.RequestAsync<TogglesWire>("toggles.set", new TogglesRequest(playerId, toggles), cancellationToken)
            .ConfigureAwait(false);
        return wire.Toggles ?? new Dictionary<string, bool>();
    }

    private static TargetWire? Target(string? playerId) => playerId is null ? null : new TargetWire(playerId);

    private sealed record TargetWire(string PlayerId);
    private sealed record AddWire(string? PlayerId, string Row, double? Seconds);
    private sealed record RemoveWire(string? PlayerId, string Row, int Limb);
    private sealed record BlockRequest(string? PlayerId, bool Enabled);
    private sealed record TogglesRequest(string? PlayerId, IReadOnlyDictionary<string, bool> Toggles);
    private sealed record EffectWire(string Row, int Limb, double ExpireTime);
    private sealed record EffectsWire(IReadOnlyList<EffectWire>? Effects, double? Now, bool BlockDebuffs, bool IsHost);
    private sealed record ClearWire(int Removed);
    private sealed record BlockWire(bool BlockDebuffs);
    private sealed record TogglesWire(Dictionary<string, bool>? Toggles);
}

/// <summary>One effect on a running character: its buff-table row, the limb number it sits on
/// (0 is the whole body) and the game's expiry value (negative when it never runs out).</summary>
public sealed record LiveEffect(string Row, int Limb, double ExpireTime);

/// <summary>A running character's effects, the game clock they count against (null when unreadable),
/// whether new debuffs are blocked, and whether this machine is the host.</summary>
public sealed record LiveEffectsState(IReadOnlyList<LiveEffect> Effects, double? Now, bool BlockDebuffs, bool IsHost)
{
    /// <summary>Seconds until <paramref name="effect"/> runs out, or null when it is permanent or
    /// its expiry is not on the readable clock.</summary>
    public double? SecondsLeft(LiveEffect effect)
        => effect.ExpireTime > 0 && Now is { } now && effect.ExpireTime >= now ? effect.ExpireTime - now : null;
}

/// <summary>The toggle names <c>toggles.get</c>/<c>toggles.set</c> use, in display order.</summary>
public static class LiveSurvivalToggles
{
    public const string Invincible = "invincible";
    public const string InfiniteStamina = "infiniteStamina";
    public const string NoHunger = "noHunger";
    public const string NoThirst = "noThirst";
    public const string NoFatigue = "noFatigue";
    public const string NoContinence = "noContinence";
    public const string NoRadiation = "noRadiation";
    public const string PerfectTemperature = "perfectTemperature";
    public const string InfiniteOxygen = "infiniteOxygen";
    public const string Untargetable = "untargetable";
    public const string NoFallDamage = "noFallDamage";
    public const string MasterKey = "masterKey";

    public static IReadOnlyList<string> All { get; } =
    [
        Invincible, InfiniteStamina, NoHunger, NoThirst, NoFatigue, NoContinence,
        NoRadiation, PerfectTemperature, InfiniteOxygen, Untargetable, NoFallDamage, MasterKey,
    ];
}
