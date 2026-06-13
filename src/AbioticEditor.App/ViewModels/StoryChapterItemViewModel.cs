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
    public string Title => Chapter.Title;
    public string Row => Chapter.Row;
    public string? Summary => Chapter.Summary;
    public bool HasSummary => !string.IsNullOrEmpty(Chapter.Summary);

    /// <summary>The world flag that triggers this chapter (DT_StoryProgression.WorldFlag).</summary>
    public string TriggerFlagText => Chapter.TriggerFlag is null ? string.Empty : $"flag: {Chapter.TriggerFlag}";

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

    public string StatusText => IsCompleted ? "DONE" : DependenciesMet ? "READY" : "LOCKED";

    public string MissingDependenciesText => MissingDependencies.Count == 0
        ? string.Empty
        : "Needs first: " + string.Join(" → ", MissingDependencies.Select(c => c.Title));

    public bool HasMissingDependencies => MissingDependencies.Count > 0;

    public string FlagStateText => Chapter.TriggerFlag is null
        ? "No trigger flag (state chapter)."
        : IsCompleted
            ? $"{Chapter.TriggerFlag} - SET in this save"
            : $"{Chapter.TriggerFlag} - not set";

    /// <summary>Coarse region derived from the chapter row prefix.</summary>
    public string RegionText
    {
        get
        {
            var r = Chapter.Row;
            if (r.StartsWith("Office", StringComparison.OrdinalIgnoreCase)) return "Office Sector";
            if (r is "Flathill" or "PostFlathill") return "Portal World: Flathill";
            if (r.StartsWith("MF", StringComparison.OrdinalIgnoreCase)) return "Manufacturing West / Mines";
            if (r == "Pens") return "The Pens";
            if (r is "Labs" or "Containment" or "Helmholtz" or "Tarasque" or "PostLabs") return "Cascade Laboratories";
            if (r == "Mycofields") return "Portal World: Mycofields";
            if (r.StartsWith("Sec", StringComparison.OrdinalIgnoreCase) || r == "EndSecurity") return "Security Sector";
            if (r is "ElectricalStation" or "EndDam") return "Hydroplant / Dam";
            if (r == "Voussoir") return "Portal World: Voussoir";
            if (r is "PowerServices" or "AnteverseC") return "Power Services";
            if (r.StartsWith("Reactors", StringComparison.OrdinalIgnoreCase) || r == "InqEnd") return "The Reactors";
            if (r == "Shadowgate") return "Portal World: Shadowgate";
            if (r.StartsWith("Residence", StringComparison.OrdinalIgnoreCase)
                || r is "Fracture" or "Botanical" or "DarkLens" or "SouthIsland") return "Residence Sector";
            if (r == "EndGame") return "Finale";
            return "GATE Cascade Facility";
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
            nameof(StatusText), nameof(MissingDependenciesText), nameof(HasMissingDependencies),
            nameof(FlagStateText),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
