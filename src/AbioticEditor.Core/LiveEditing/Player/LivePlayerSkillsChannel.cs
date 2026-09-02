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

    /// <summary>Reads the current skill XP/multiplier list, one row per skill, for
    /// <paramref name="playerId"/> (or the local player when omitted).</summary>
    public async Task<IReadOnlyList<PlayerSkill>> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<IReadOnlyList<SkillWire>>("skills.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return wire.Select(w => new PlayerSkill(w.Index, w.Xp, w.XpMultiplier)).ToList();
    }

    /// <summary>Applies new skill XP/multiplier values to <paramref name="playerId"/> (or the
    /// local player when omitted) immediately.</summary>
    public Task SetAsync(IReadOnlyList<PlayerSkill> skills, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("skills.set",
            new SetWire(playerId, skills.Select(s => new SkillWire(s.Index, s.Xp, s.XpMultiplier)).ToList()),
            cancellationToken);

    private sealed record SkillWire(int Index, float Xp, float XpMultiplier);

    /// <summary>The skill rows live under a nested <c>skills</c> field (not the whole payload)
    /// so <c>playerId</c> can sit alongside them, matching every other live-editing command's
    /// shape now that player selection exists.</summary>
    private sealed record SetWire(string? PlayerId, IReadOnlyList<SkillWire> Skills);
}
