using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// View-model wrapper for a single world-flag string with metadata from
/// <see cref="QuestFlagCatalog"/>: friendly name, parsed area, category.
/// </summary>
public sealed class FlagItemViewModel
{
    public FlagItemViewModel(string rawFlag, bool isActive = true, int missingPrereqCount = 0)
    {
        RawName = rawFlag;
        Info = QuestFlagCatalog.Lookup(rawFlag);
        IsActive = isActive;
        MissingPrereqCount = missingPrereqCount;
        StoryChapter = StoryProgressionCatalog.ChapterForFlag(rawFlag);
    }

    public string RawName { get; }
    public FlagInfo Info { get; }
    public bool IsActive { get; }
    public bool IsInactive => !IsActive;
    public string FriendlyName => Info.FriendlyName;

    /// <summary>How many prerequisite flags are still unset (computed at list build).</summary>
    public int MissingPrereqCount { get; }

    /// <summary>Inactive AND gated - show the lock chip.</summary>
    public bool IsLocked => !IsActive && MissingPrereqCount > 0;

    public string LockText => $"🔒 {MissingPrereqCount} PREREQ";

    // ---------- spoiler concealment ----------

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => Services.SpoilerService.Key(Services.SpoilerService.Flag, RawName);

    /// <summary>A locked (gated, not-yet-reachable) flag describes a future story event.</summary>
    public bool IsConcealed => Services.SpoilerService.ShouldConceal(SpoilerKey, IsLocked);

    public string ShownFriendlyName => Services.SpoilerService.Mask(FriendlyName, IsConcealed, Services.SpoilerService.ClassifiedShort);
    public string ShownRawName => IsConcealed ? Services.SpoilerService.Redacted : RawName;
    public string ShownDescription => IsConcealed ? Services.SpoilerService.ClassifiedHint : DescriptionText;
    public string ShownStoryChapterText => IsConcealed ? string.Empty : StoryChapterText;

    /// <summary>The STORY chapter chip is itself a spoiler, so hide it while sealed.</summary>
    public bool ShowStoryTrigger => IsStoryTrigger && !IsConcealed;

    /// <summary>TOGGLE is disabled while sealed - reveal the flag before acting on it.</summary>
    public bool CanAct => !IsConcealed;

    /// <summary>
    /// The main-quest chapter this flag triggers (per DT_StoryProgression), or null for
    /// ordinary flags. Lets the flags tab show which entries actually advance the story.
    /// </summary>
    public StoryChapter? StoryChapter { get; }

    public bool IsStoryTrigger => StoryChapter is not null;
    public string StoryChapterText => StoryChapter is null
        ? string.Empty
        : $"STORY: {StoryChapter.Title}";
    public string Area => Info.Area;
    public FlagCategory Category => Info.Category;
    public string CategoryLabel => Category.ToString().ToUpperInvariant();
    public string StatusLabel => IsActive ? "ACTIVE" : "MISSING";
    public string StatusColor => IsActive ? "#7BB351" /* green */ : "#6E6655" /* muted */;

    /// <summary>
    /// Plain-language explanation of what the flag records: the chapter summary for
    /// story triggers, otherwise an event description derived from the name's verb.
    /// </summary>
    public string DescriptionText
    {
        get
        {
            if (StoryChapter is { } ch)
            {
                return ch.Summary ?? $"Story trigger for chapter \"{ch.Title}\".";
            }

            var n = RawName;
            string what =
                n.Contains("Completed", StringComparison.OrdinalIgnoreCase) ? "Records that this objective/event was completed" :
                n.Contains("Opened", StringComparison.OrdinalIgnoreCase) ? "Records that a door, gate or route here was opened" :
                n.Contains("Entered", StringComparison.OrdinalIgnoreCase) ? "Records that the players entered this place for the first time" :
                n.Contains("Met", StringComparison.OrdinalIgnoreCase) ? "Records a first meeting with this character" :
                n.Contains("Reached", StringComparison.OrdinalIgnoreCase) ? "Records that this point was reached" :
                n.Contains("Found", StringComparison.OrdinalIgnoreCase) ? "Records that this thing was found" :
                n.Contains("Unlock", StringComparison.OrdinalIgnoreCase) ? "Unlocks content (recipes, stock or access) tied to it" :
                n.Contains("Fixed", StringComparison.OrdinalIgnoreCase) || n.Contains("Repaired", StringComparison.OrdinalIgnoreCase) ? "Records that this machine/system was repaired" :
                n.Contains("Defeated", StringComparison.OrdinalIgnoreCase) || n.Contains("Killed", StringComparison.OrdinalIgnoreCase) ? "Records that this enemy was defeated" :
                "One-way world event marker the game checks to gate content";
            return $"{what} - {Area} region. Setting it can skip the event; removing it can replay it (where the game re-checks).";
        }
    }

    /// <summary>Per-category accent colour used in the UI chips.</summary>
    public string CategoryColor => Category switch
    {
        FlagCategory.Tutorial  => "#7BB351",  // green
        FlagCategory.Quest     => "#E37A22",  // orange
        FlagCategory.Discovery => "#56A8C4",  // cyan
        FlagCategory.Unlock    => "#F2C82E",  // yellow
        FlagCategory.Meta      => "#9F9582",  // muted
        _                      => "#6E6655",
    };
}
