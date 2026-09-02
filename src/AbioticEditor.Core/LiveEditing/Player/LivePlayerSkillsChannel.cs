using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// The live-agent analog of <c>PlayerSaveReader.ReadSkills</c>/<c>PlayerSaveWriter.ApplySkills</c>:
/// reads and writes the same positional <see cref="PlayerSkill"/> list the file writer already
/// uses, sourced from a running game over <see cref="ILiveGameChannel"/> instead of the
/// <c>Skills_</c> array property.
/// </summary>
public sealed class LivePlayerSkillsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the live player's current skill XP/multiplier list, one row per skill.</summary>
    public async Task<IReadOnlyList<PlayerSkill>> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<IReadOnlyList<SkillWire>>("skills.get", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return wire.Select(w => new PlayerSkill(w.Index, w.Xp, w.XpMultiplier)).ToList();
    }

    /// <summary>Applies new skill XP/multiplier values to the live player immediately.</summary>
    public Task SetAsync(IReadOnlyList<PlayerSkill> skills, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("skills.set",
            skills.Select(s => new SkillWire(s.Index, s.Xp, s.XpMultiplier)).ToList(), cancellationToken);

    private sealed record SkillWire(int Index, float Xp, float XpMultiplier);
}
