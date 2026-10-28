using AbioticEditor.Core.Codex;

namespace AbioticEditor.Web.Models;

/// <summary>What the running world says about quest progress: every world flag that is set, and
/// (when the game could be asked) the objectives it reports active.</summary>
public sealed record QuestWorldView(IReadOnlySet<string>? Flags, IReadOnlySet<string>? ActiveQuests);

/// <summary>
/// Narrow boundary for the GATEPal quests view (Community Update 5 multi-quest), so the same
/// widget works for a staged file session and a live game. The file session stages tracking until
/// Save; the live one applies it in the running game at once.
/// </summary>
public interface IPlayerQuestsSession
{
    /// <summary>False when quest lines cannot be shown or tracked (a pre-1.5 save, or an agent
    /// build that predates quest support).</summary>
    bool SupportsMultipleQuests { get; }
    bool CanTrackQuestLines { get; }
    bool AppliesImmediately { get; }
    string? TrackedQuestLine { get; }
    bool CompletedIntro { get; }
    string? CurrentTutorialQuest { get; }

    /// <summary>The personal tutorial objective, worked out from <paramref name="catalog"/> when the
    /// session cannot read it directly (a live Community Update 5 game keeps it nowhere readable).</summary>
    string? TutorialQuestFor(QuestCatalogData catalog) => CurrentTutorialQuest;

    /// <summary>The save file whose world flags should be read, or null for a live game.</summary>
    string? SavePath { get; }

    /// <summary>The live world's flags and active objectives; null for a file session.</summary>
    Task<QuestWorldView?> LoadWorldViewAsync();

    Task TrackQuestLineAsync(string? id, QuestCatalogData catalog);
}

/// <summary>Presents a staged <see cref="PlayerSaveSession"/> through <see cref="IPlayerQuestsSession"/>.</summary>
public sealed class FileQuestsSession(PlayerSaveSession session) : IPlayerQuestsSession
{
    public bool SupportsMultipleQuests => session.SupportsMultipleQuests;
    public bool CanTrackQuestLines => session.SupportsMultipleQuests;
    public bool AppliesImmediately => false;
    public string? TrackedQuestLine => session.TrackedQuestLine;
    public bool CompletedIntro => session.CompletedIntro;
    public string? CurrentTutorialQuest => session.CurrentTutorialQuest;
    public string? SavePath => session.Path;
    public Task<QuestWorldView?> LoadWorldViewAsync() => Task.FromResult<QuestWorldView?>(null);

    public Task TrackQuestLineAsync(string? id, QuestCatalogData catalog)
    {
        session.TrackQuestLine(id, catalog);
        return Task.CompletedTask;
    }
}
