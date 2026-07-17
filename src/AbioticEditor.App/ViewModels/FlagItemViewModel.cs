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

    public string LockText => Services.LocalizationResourceManager.Instance.Format("WorldFlags_PrereqLock", MissingPrereqCount);

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
        : Services.LocalizationResourceManager.Instance.Format("WorldFlags_StoryChapterFormat", StoryChapter.Title);
    public string Area => Info.Area;
    public FlagCategory Category => Info.Category;
    public string CategoryLabel => Category switch
    {
        FlagCategory.Tutorial  => Services.LocalizationResourceManager.Instance["WorldFlags_CategoryTutorial"],
        FlagCategory.Quest     => Services.LocalizationResourceManager.Instance["WorldFlags_CategoryQuest"],
        FlagCategory.Discovery => Services.LocalizationResourceManager.Instance["WorldFlags_CategoryDiscovery"],
        FlagCategory.Unlock    => Services.LocalizationResourceManager.Instance["Slot_Unlock"],
        FlagCategory.Meta      => Services.LocalizationResourceManager.Instance["WorldFlags_CategoryMeta"],
        _                      => Services.LocalizationResourceManager.Instance["WorldFlags_CategoryOther"],
    };
    public string StatusLabel => IsActive
        ? Services.LocalizationResourceManager.Instance["WorldFlags_StatusActive"]
        : Services.LocalizationResourceManager.Instance["WorldFlags_StatusMissing"];
    public string StatusColor => IsActive ? "#7BB351" /* green */ : "#6E6655" /* muted */;

    /// <summary>
    /// Plain-language explanation of what the flag records: the chapter summary for
    /// story triggers, otherwise an event description derived from the name's verb.
    /// </summary>
    public string DescriptionText
    {
        get
        {
            var loc = Services.LocalizationResourceManager.Instance;
            if (StoryChapter is { } ch)
            {
                return ch.Summary ?? loc.Format("WorldFlags_StoryTriggerForChapter", ch.Title);
            }

            var n = RawName;
            string what =
                n.Contains("Completed", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescCompleted"] :
                n.Contains("Opened", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescOpened"] :
                n.Contains("Entered", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescEntered"] :
                n.Contains("Met", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescMet"] :
                n.Contains("Reached", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescReached"] :
                n.Contains("Found", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescFound"] :
                n.Contains("Unlock", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescUnlock"] :
                n.Contains("Fixed", StringComparison.OrdinalIgnoreCase) || n.Contains("Repaired", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescRepaired"] :
                n.Contains("Defeated", StringComparison.OrdinalIgnoreCase) || n.Contains("Killed", StringComparison.OrdinalIgnoreCase) ? loc["WorldFlags_DescDefeated"] :
                loc["WorldFlags_DescGeneric"];
            return loc.Format("WorldFlags_DescriptionFormat", what, Area);
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
