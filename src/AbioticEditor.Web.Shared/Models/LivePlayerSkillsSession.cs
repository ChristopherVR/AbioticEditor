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

    private LivePlayerSkillsSession(LivePlayerSkillsChannel channel, IReadOnlyList<PlayerSkillEdit> skills)
    {
        _channel = channel;
        Skills = skills;
    }

    /// <summary>Connects and reads the live player's current skills to seed the session.</summary>
    public static async Task<LivePlayerSkillsSession> ConnectAsync(
        LivePlayerSkillsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var skills = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        var edits = skills.OrderBy(skill => skill.Index)
            .Select(skill => new PlayerSkillEdit(skill, SkillDefinitionFor(skill.Index)))
            .ToList();
        return new LivePlayerSkillsSession(channel, edits);
    }

    public IReadOnlyList<PlayerSkillEdit> Skills { get; }

    public bool IsDirty => Skills.Any(skill => skill.IsDirty);

    public string? Status { get; private set; }

    public void MarkChanged() => Status = IsDirty ? "Unsaved changes" : null;

    public void MaxAllSkills()
    {
        foreach (var skill in Skills) skill.Level = SkillCatalog.MaxLevel;
        MarkChanged();
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _channel.SetAsync(Skills.Select(skill => skill.ToPlayerSkill()).ToList(), cancellationToken)
            .ConfigureAwait(false);
        foreach (var skill in Skills) skill.AcceptCurrentAsBaseline();
        Status = "Applied live - this took effect in the running game immediately.";
    }

    public void Revert()
    {
        foreach (var skill in Skills) skill.Revert();
        Status = "Changes reverted.";
    }

    private static SkillDefinition SkillDefinitionFor(int index)
        => SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, index + 1)[index];
}
