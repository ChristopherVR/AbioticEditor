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
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<VitalsWire>("vitals.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return (wire.ToStats(), wire.ToHealth());
    }

    /// <summary>Applies new survival stats and limb health to <paramref name="playerId"/> (or the
    /// local player when omitted) immediately.</summary>
    public Task SetAsync(CharacterStats stats, LimbHealth health, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("vitals.set", VitalsWire.From(stats, health, playerId), cancellationToken);

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
        string? PlayerId = null)
    {
        public CharacterStats ToStats() => new(Hunger, Thirst, Sanity, Fatigue, Continence, Money);
        public LimbHealth ToHealth() => new(Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg);

        public static VitalsWire From(CharacterStats stats, LimbHealth health, string? playerId) => new(
            stats.Hunger, stats.Thirst, stats.Sanity, stats.Fatigue, stats.Continence, stats.Money,
            health.Head, health.Torso, health.LeftArm, health.RightArm, health.LeftLeg, health.RightLeg,
            playerId);
    }
}
