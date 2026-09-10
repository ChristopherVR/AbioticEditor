using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s skills slice: implements
/// <see cref="IPlayerSkillsSession"/> so <c>PlayerSkillsTab</c> binds to it unchanged, exactly
/// how <see cref="LivePlayerVitalsSession"/> already does for vitals. Like that class, there is
/// no local "staged until Save" backup - <see cref="SaveAsync"/> pushes straight to the live game.
/// </summary>
public sealed class LivePlayerSkillsSession : IPlayerSkillsSession
{
    private readonly LivePlayerSkillsChannel _channel;
    private string? _playerId;

    private LivePlayerSkillsSession(LivePlayerSkillsChannel channel, string? playerId, IReadOnlyList<PlayerSkillEdit> skills)
    {
        _channel = channel;
        _playerId = playerId;
        Skills = skills;
    }

    /// <summary>Connects and reads the current skills for <paramref name="playerId"/> (or the
    /// local player when omitted) to seed the session.</summary>
    public static async Task<LivePlayerSkillsSession> ConnectAsync(
        LivePlayerSkillsChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var skills = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        var edits = skills.OrderBy(skill => skill.Index)
            .Select(skill => new PlayerSkillEdit(skill, SkillDefinitionFor(skill.Index)))
            .ToList();
        return new LivePlayerSkillsSession(channel, playerId, edits);
    }

    public IReadOnlyList<PlayerSkillEdit> Skills { get; private set; }

    public bool IsDirty => Skills.Any(skill => skill.IsDirty);

    public string? Status { get; private set; }

    public void MarkChanged() => Status = IsDirty ? "Unsaved changes" : null;

    public void MaxAllSkills()
    {
        foreach (var skill in Skills) skill.Level = SkillCatalog.MaxLevel;
        MarkChanged();
    }

    /// <summary>Pushes only the skills the player actually changed - not the whole positional
    /// list - to the live game. This mattered for a real reason, not just efficiency: the live
    /// write is a remove-then-add RPC pair (see <see cref="LivePlayerSkillsChannel.SetAsync"/>'s
    /// own doc comment), so sending every skill on every save briefly zeroed and re-maxed every
    /// UNTOUCHED skill too. Fishing sits last in file order (<see cref="SkillCatalog.CanonicalOrder"/>),
    /// so it was always the last skill re-applied in that batch - the game's own level-up popup
    /// for that redundant zero-then-restore cycle is what a player actually saw and reported as
    /// "editing any skill says Fishing unlocked", even though the skill they edited was written
    /// correctly underneath. Sending only the dirty rows stops every untouched skill (Fishing
    /// included) from being re-applied at all, so its own popup can no longer be the one that wins.</summary>
    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dirty = Skills.Where(skill => skill.IsDirty).Select(skill => skill.ToPlayerSkill()).ToList();
        if (dirty.Count > 0)
        {
            await _channel.SetAsync(dirty, _playerId, cancellationToken).ConfigureAwait(false);
        }
        foreach (var skill in Skills) skill.AcceptCurrentAsBaseline();
        Status = null;
    }

    public void Revert()
    {
        foreach (var skill in Skills) skill.Revert();
        Status = "Changes reverted.";
    }

    /// <summary>Re-reads the live player's skills, discarding any unsaved local edits. Mirrors
    /// <see cref="LivePlayerVitalsSession.RefreshAsync"/> - used to pick up progress made in the
    /// running game (levelling up, say) while this tab is open and nothing is being edited right
    /// now.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var skills = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        Skills = skills.OrderBy(skill => skill.Index)
            .Select(skill => new PlayerSkillEdit(skill, SkillDefinitionFor(skill.Index)))
            .ToList();
        Status = null;
    }

    /// <summary>Switches which connected player this session edits (discarding any unsaved local
    /// edits for the previous one) and re-reads that player's skills.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SkillDefinition SkillDefinitionFor(int index)
        => SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, index + 1)[index];
}
