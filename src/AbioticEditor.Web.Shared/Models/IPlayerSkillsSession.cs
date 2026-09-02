namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-skills editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>PlayerSkillsTab.razor</c> uses, extracted from
/// <see cref="PlayerSaveSession"/>'s existing skills slice, so that widget binds to either the
/// file-backed session or <c>LivePlayerSkillsSession</c> with no changes beyond its parameter's
/// declared type.
/// </summary>
public interface IPlayerSkillsSession
{
    IReadOnlyList<PlayerSkillEdit> Skills { get; }
    bool IsDirty { get; }
    string? Status { get; }
    void MarkChanged();
    void MaxAllSkills();
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();
}
