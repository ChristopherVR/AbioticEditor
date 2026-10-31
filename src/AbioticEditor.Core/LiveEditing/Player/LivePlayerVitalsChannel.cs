using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// The live-agent analog of <c>PlayerSaveReader</c>/<c>PlayerSaveWriter</c>'s stats/limb-health
/// pair: reads and writes the same <see cref="CharacterStats"/>/<see cref="LimbHealth"/> domain
/// shapes the file writer's <c>ApplyStats</c>/<c>ApplyLimbHealth</c> already use, sourced from a
/// running game over <see cref="ILiveGameChannel"/> instead of an <c>FPropertyTag</c> list.
/// </summary>
public sealed class LivePlayerVitalsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>
    /// Reads the current survival stats and limb health for <paramref name="playerId"/> (as
    /// listed by <see cref="LivePlayerDirectoryChannel"/>), or the local player when omitted -
    /// unchanged from before player selection existed, and still the only path this repo's tests
    /// exercise against the real game.
    /// </summary>
    public async Task<(CharacterStats Stats, LimbHealth Health)> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        var reading = await ReadAsync(playerId, cancellationToken).ConfigureAwait(false);
        return (reading.Stats, reading.Health);
    }

    /// <summary>
    /// As <see cref="GetAsync"/>, plus whether the agent reported radiation and body temperature
    /// (an agent older than those fields leaves them out) and the character's own comfortable
    /// temperature, which a save file does not store.
    /// </summary>
    public async Task<LiveVitalsReading> ReadAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<VitalsWire>("vitals.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return new LiveVitalsReading(wire.ToStats(), wire.ToHealth(),
            wire.Radiation is not null && wire.BodyTemperature is not null, wire.NormalBodyTemperature ?? 0);
    }

    /// <summary>Applies new survival stats and limb health to <paramref name="playerId"/> (or the
    /// local player when omitted) immediately.</summary>
    public Task SetAsync(CharacterStats stats, LimbHealth health, string? playerId = null,
        CancellationToken cancellationToken = default)
        => SetAsync(stats, health, playerId, includeConditions: true, cancellationToken);

    /// <summary>As the other overload; <paramref name="includeConditions"/> false leaves radiation
    /// and body temperature untouched in the game, for an agent that did not report them.</summary>
    public Task SetAsync(CharacterStats stats, LimbHealth health, string? playerId, bool includeConditions,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("vitals.set", VitalsWire.From(stats, health, playerId, includeConditions), cancellationToken);

    /// <summary>
    /// The wire shape for <c>vitals.get</c>/<c>vitals.set</c>, flat rather than nesting
    /// <see cref="CharacterStats"/>/<see cref="LimbHealth"/> so the C++ agent only has to
    /// populate a single flat struct from the live PlayerState/PawnHealthComponent properties.
    /// <see cref="PlayerId"/> is omitted from the wire entirely when null (the live-agent mod
    /// then targets the local player, exactly as it did before player selection existed).
    /// </summary>
    private sealed record VitalsWire(
        double Hunger, double Thirst, double Sanity, double Fatigue, double Continence, int Money,
        double Head, double Torso, double LeftArm, double RightArm, double LeftLeg, double RightLeg,
        string? PlayerId = null, double? Radiation = null, double? BodyTemperature = null,
        double? NormalBodyTemperature = null)
    {
        public CharacterStats ToStats() => new(Hunger, Thirst, Sanity, Fatigue, Continence, Money,
            Radiation ?? 0, BodyTemperature ?? 0);
        public LimbHealth ToHealth() => new(Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg);

        public static VitalsWire From(CharacterStats stats, LimbHealth health, string? playerId, bool includeConditions) => new(
            stats.Hunger, stats.Thirst, stats.Sanity, stats.Fatigue, stats.Continence, stats.Money,
            health.Head, health.Torso, health.LeftArm, health.RightArm, health.LeftLeg, health.RightLeg,
            playerId,
            includeConditions ? stats.Radiation : null,
            includeConditions ? stats.BodyTemperature : null);
    }
}

/// <summary>One live <c>vitals.get</c> read: the shared stat and limb shapes, whether radiation
/// and body temperature were reported, and the character's comfortable temperature.</summary>
public sealed record LiveVitalsReading(CharacterStats Stats, LimbHealth Health, bool HasConditions, double NormalBodyTemperature);

