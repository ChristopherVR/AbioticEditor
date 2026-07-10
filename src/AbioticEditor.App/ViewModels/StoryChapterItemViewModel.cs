using System.ComponentModel;
using System.Windows.Input;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One row of the main-quest chapter checklist. "Reached" means the world's current
/// chapter is at or past this one; SET rewinds/advances the story to it.
/// </summary>
public sealed class StoryChapterItemViewModel : INotifyPropertyChanged
{
    private readonly WorldEditorViewModel _owner;

    public StoryChapterItemViewModel(WorldEditorViewModel owner, StoryChapter chapter, int index)
    {
        _owner = owner;
        Chapter = chapter;
        Index = index;
        // SET is the whole operation: pointer + both-direction facility-flag sync.
        SetCommand = new RelayCommand(async () => await _owner.SetChapterAsync(Chapter.Row));
    }

    public StoryChapter Chapter { get; }
    public int Index { get; }

    public string NumberText => $"{Index + 1:D2}";

    /// <summary>Localized chapter title (resx key keyed by <see cref="Chapter"/>'s row id); the
    /// English text in <see cref="StoryChapter.Title"/> stays the source of truth for the CLI and
    /// other non-MAUI consumers.</summary>
    public string Title => Loc[$"WorldStory_ChapterTitle_{Chapter.Row}"];
    public string Row => Chapter.Row;
    public string? Summary => HasSummary ? Loc[$"WorldStory_ChapterSummary_{Chapter.Row}"] : null;
    public bool HasSummary => !string.IsNullOrEmpty(Chapter.Summary);

    /// <summary>The world flag that triggers this chapter (DT_StoryProgression.WorldFlag).</summary>
    public string TriggerFlagText => Chapter.TriggerFlag is null
        ? string.Empty
        : Services.LocalizationResourceManager.Instance.Format("WorldStory_TriggerFlagLabel", Chapter.TriggerFlag);

    public ICommand SetCommand { get; }

    public bool IsCurrent => string.Equals(_owner.SelectedStoryRow, Chapter.Row, StringComparison.OrdinalIgnoreCase);

    public bool IsReached
    {
        get
        {
            var current = StoryProgressionCatalog.IndexOf(_owner.SelectedStoryRow);
            return current >= 0 && Index <= current;
        }
    }

    // ---------- linear progression state (flags + dependencies) ----------

    /// <summary>This chapter's trigger flag is set in the save.</summary>
    public bool IsCompleted => Chapter.TriggerFlag is not null && _owner.HasWorldFlag(Chapter.TriggerFlag);

    /// <summary>Every earlier chapter's trigger flag is set - the story can reach this one.</summary>
    public bool DependenciesMet => MissingDependencies.Count == 0;

    /// <summary>Earlier chapters whose trigger flags are still missing.</summary>
    public IReadOnlyList<StoryChapter> MissingDependencies
        => StoryProgressionCatalog.Chapters
            .Take(Index)
            .Where(c => c.TriggerFlag is not null && !_owner.HasWorldFlag(c.TriggerFlag!))
            .ToList();

    private static Services.LocalizationResourceManager Loc => Services.LocalizationResourceManager.Instance;

    /// <summary>Non-localized discriminator for the XAML status-color <c>DataTrigger</c>s - the
    /// localized <see cref="StatusText"/> can't be matched against a fixed string once translated.</summary>
    public ChapterStatusKind StatusKind => IsCompleted ? ChapterStatusKind.Done : DependenciesMet ? ChapterStatusKind.Ready : ChapterStatusKind.Locked;

    public string StatusText => StatusKind switch
    {
        ChapterStatusKind.Done => Loc["WorldStory_StatusDone"],
        ChapterStatusKind.Ready => Loc["WorldStory_StatusReady"],
        _ => Loc["WorldStory_StatusLocked"],
    };

    public string MissingDependenciesText => MissingDependencies.Count == 0
        ? string.Empty
        : Loc.Format("WorldStory_NeedsFirst", string.Join(" → ", MissingDependencies.Select(c => Loc[$"WorldStory_ChapterTitle_{c.Row}"])));

    public bool HasMissingDependencies => MissingDependencies.Count > 0;

    public string FlagStateText => Chapter.TriggerFlag is null
        ? Loc["WorldStory_NoTriggerFlag"]
        : Loc.Format(IsCompleted ? "WorldStory_FlagSetInSave" : "WorldStory_FlagNotSet", Chapter.TriggerFlag);

    /// <summary>Coarse region derived from the chapter row prefix.</summary>
    public string RegionText
    {
        get
        {
            var r = Chapter.Row;
            if (r.StartsWith("Office", StringComparison.OrdinalIgnoreCase)) return Loc["WorldStory_RegionOfficeSector"];
            if (r is "Flathill" or "PostFlathill") return Loc["WorldStory_RegionPortalFlathill"];
            if (r.StartsWith("MF", StringComparison.OrdinalIgnoreCase)) return Loc["WorldStory_RegionManufacturingMines"];
            if (r == "Pens") return Loc["WorldStory_RegionThePens"];
            if (r is "Labs" or "Containment" or "Helmholtz" or "Tarasque" or "PostLabs") return Loc["WorldStory_RegionCascadeLabs"];
            if (r == "Mycofields") return Loc["WorldStory_RegionPortalMycofields"];
            if (r.StartsWith("Sec", StringComparison.OrdinalIgnoreCase) || r == "EndSecurity") return Loc["WorldStory_RegionSecuritySector"];
            if (r is "ElectricalStation" or "EndDam") return Loc["WorldStory_RegionHydroplantDam"];
            if (r == "Voussoir") return Loc["WorldStory_RegionPortalVoussoir"];
            if (r is "PowerServices" or "AnteverseC") return Loc["WorldStory_RegionPowerServices"];
            if (r.StartsWith("Reactors", StringComparison.OrdinalIgnoreCase) || r == "InqEnd") return Loc["WorldStory_RegionTheReactors"];
            if (r == "Shadowgate") return Loc["WorldStory_RegionPortalShadowgate"];
            if (r.StartsWith("Residence", StringComparison.OrdinalIgnoreCase)
                || r is "Fracture" or "Botanical" or "DarkLens" or "SouthIsland") return Loc["WorldStory_RegionResidenceSector"];
            if (r == "EndGame") return Loc["WorldStory_RegionFinale"];
            return Loc["WorldStory_RegionGateCascadeFacility"];
        }
    }

    // ---------- chapter card art (ServerBrowser/map_*) ----------

    private string? _cardPath;
    private bool _cardRequested;

    public string? CardImagePath
    {
        get
        {
            EnsureCard();
            return _cardPath;
        }
    }

    public bool HasCardImage => _cardPath is not null;

    private void EnsureCard()
    {
        if (_cardRequested || Chapter.CardArt is null) return;
        _cardRequested = true;
        var provider = Services.GameDataServices.Provider;
        if (provider is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                var path = provider.ExtractTextureByGameRef(Chapter.CardArt);
                if (path is null) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _cardPath = path;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardImagePath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCardImage)));
                });
            }
            catch
            {
                // Card art is cosmetic.
            }
        });
    }

    public void NotifyCurrentChanged()
    {
        foreach (var p in new[]
        {
            nameof(IsCurrent), nameof(IsReached), nameof(IsCompleted), nameof(DependenciesMet),
            nameof(StatusKind), nameof(StatusText), nameof(MissingDependenciesText), nameof(HasMissingDependencies),
            nameof(FlagStateText),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Non-localized status discriminator for <see cref="StoryChapterItemViewModel.StatusKind"/>.</summary>
public enum ChapterStatusKind
{
    Locked,
    Ready,
    Done,
}
