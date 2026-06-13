using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Codex;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One codex row: an email, journal objective, or compendium entry, with the player's
/// read/found state. Emails and journals are editable; compendium is view-only.
/// </summary>
public sealed class CodexItemViewModel : INotifyPropertyChanged
{
    private readonly CodexViewModel _owner;
    private bool _isKnown;

    public CodexItemViewModel(
        CodexViewModel owner, string id, string title, string? subtitle, string body,
        bool known, bool editable, IReadOnlyList<string>? sectionTypes = null)
    {
        _owner = owner;
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Body = body;
        _isKnown = known;
        IsEditable = editable;
        SectionTypes = sectionTypes ?? Array.Empty<string>();
    }

    /// <summary>Compendium only: which save arrays this row belongs to when unlocked.</summary>
    public IReadOnlyList<string> SectionTypes { get; }

    // ---------- kill tracking (compendium entries with a kill-requirement section) ----------

    private int? _killCount;

    /// <summary>How many kills the kill-requirement section needs (null = no such section).</summary>
    public int? KillRequired { get; init; }

    /// <summary>
    /// The player's kill tally for this entity. Null when the save doesn't track it yet:
    /// the array only gains entries on a first in-game kill, so we can't add new ones.
    /// </summary>
    public int? KillCount
    {
        get => _killCount;
        set
        {
            if (_killCount is null || value is null || _killCount == value) return;
            _killCount = Math.Max(0, value.Value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KillCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KillText)));
            _owner.OnItemToggled();
        }
    }

    /// <summary>Seeds the tally without firing change notifications (load time).</summary>
    public void InitKillCount(int count) => _killCount = count;

    public bool HasKillTracking => KillRequired is not null;
    public string KillText => KillRequired is null ? string.Empty
        : _killCount is null ? $"kills: not tracked yet · {KillRequired} required for the bonus section"
        : $"{_killCount} of {KillRequired} kills for the bonus section";
    public bool CanEditKills => _killCount is not null;

    public string Id { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    /// <summary>Full rendered text (email sections with senders / journal note / lore).</summary>
    public string Body { get; }

    public bool IsEditable { get; }

    public bool IsKnown
    {
        get => _isKnown;
        set
        {
            if (!IsEditable || _isKnown == value) return;
            // Progress gate: rows tied to a region the world hasn't reached can't be
            // unlocked (ids carry the same area prefixes the quest flags use).
            if (value && !Services.ProgressContext.CanUnlockRow(Id, out var reason))
            {
                Services.ProgressContext.Notify?.Invoke(reason!);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKnown)));
                return;
            }
            _isKnown = value;
            Core.Diagnostics.EditorLog.Info("Edit", $"Codex row {(value ? "UNLOCKED" : "CLEARED")}: {Id}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKnown)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            _owner.OnItemToggled();
        }
    }

    public string StatusLabel => _isKnown ? "READ" : "UNREAD";

    // ---------- spoiler concealment ----------

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => SpoilerService.Key(SpoilerService.Codex, Id);

    /// <summary>Gated by world progress (its region hasn't been reached) - a future entry.</summary>
    public bool IsGated => !ProgressContext.CanUnlockRow(Id, out _);

    /// <summary>A not-yet-known, region-gated entry is a spoiler until individually revealed.</summary>
    public bool IsConcealed => SpoilerService.ShouldConceal(SpoilerKey, !_isKnown && IsGated);

    public string ShownTitle => SpoilerService.Mask(Title, IsConcealed, SpoilerService.ClassifiedShort);
    public string? ShownSubtitle => IsConcealed ? null : Subtitle;
    public bool ShowSubtitle => HasSubtitle && !IsConcealed;
    public string ShownBody => IsConcealed ? SpoilerService.ClassifiedHint : Body;

    /// <summary>The read checkbox is disabled while sealed - reveal the entry first.</summary>
    public bool CanToggle => IsEditable && !IsConcealed;

    /// <summary>Row icon stays hidden while sealed (fish art spoils the entry).</summary>
    public bool ShowIcon => _iconPath is not null && !IsConcealed;

    private void NotifyConcealment()
    {
        foreach (var n in new[]
        {
            nameof(IsConcealed), nameof(ShownTitle), nameof(ShownSubtitle), nameof(ShowSubtitle),
            nameof(ShownBody), nameof(CanToggle), nameof(ShowIcon),
            nameof(HasFishUnlock), nameof(HasFishRequiredBait), nameof(HasFishCatchRequirements),
            nameof(HasFishXp), nameof(ShowFishDetailDivider),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>Prompts to override clearance; on confirm reveals this entry permanently.</summary>
    public async Task RevealAsync()
    {
        if (!IsConcealed) return;
        if (await SpoilerPrompt.RevealAsync("This codex entry", SpoilerKey))
        {
            NotifyConcealment();
            _owner.OnRevealed();
        }
    }

    // ---------- optional icon (fish use their item icon) ----------

    private string? _iconPath;
    private bool _iconRequested;

    /// <summary>Set at construction for entries with an extractable icon (fish).</summary>
    public Core.Items.ItemCatalogEntry? IconEntry { get; init; }

    public string? IconPath
    {
        get
        {
            EnsureIcon();
            return _iconPath;
        }
    }

    public bool HasIcon => _iconPath is not null;

    private void EnsureIcon()
    {
        if (_iconRequested || IconEntry?.IconAssetPath is null) return;
        _iconRequested = true;
        var provider = GameDataServices.Provider;
        if (provider is null) return;
        var entry = IconEntry;
        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(entry!.IconAssetPath);
                var colorized = raw is null ? null : Core.Items.IconColorizer.Colorize(raw, entry);
                if (colorized is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _iconPath = colorized;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowIcon)));
                    });
                }
            }
            catch
            {
                // Icons are cosmetic.
            }
        });
    }

    // ---------- fish detail: what catching unlocks + what's needed to catch ----------

    /// <summary>Catalog entry for the bait this fish unlocks (drives icon + slot-editor tap), or null.</summary>
    public Core.Items.ItemCatalogEntry? FishUnlockBaitEntry { get; init; }

    /// <summary>Catalog entry for the bait this fish needs to bite (rare variants), or null.</summary>
    public Core.Items.ItemCatalogEntry? FishRequiredBaitEntry { get; init; }

    /// <summary>Plain-language requirements to land this fish (location, flags, time).</summary>
    public IReadOnlyList<string> FishCatchLines { get; init; } = Array.Empty<string>();

    /// <summary>XP awarded for catching it (0 = unknown / not a fish row).</summary>
    public int FishXpGain { get; init; }

    public string? FishUnlockName => FishUnlockBaitEntry?.DisplayName;

    /// <summary>Item id of the unlocked bait — opens it in the slot editor when tapped.</summary>
    public string? FishUnlockBaitItemId => FishUnlockBaitEntry?.Id;

    public string? FishRequiredBaitName => FishRequiredBaitEntry?.DisplayName;
    public string? FishRequiredBaitItemId => FishRequiredBaitEntry?.Id;

    // Sealed/spoiler fish keep their detail hidden until revealed (same gate as the body).
    public bool HasFishUnlock => FishUnlockBaitEntry is not null && !IsConcealed;
    public bool HasFishRequiredBait => FishRequiredBaitEntry is not null && !IsConcealed;
    public bool HasFishCatchRequirements => (FishCatchLines.Count > 0 || HasFishRequiredBait) && !IsConcealed;
    public bool HasFishXp => FishXpGain > 0 && !IsConcealed;
    public bool ShowFishDetailDivider => HasFishUnlock || HasFishCatchRequirements || HasFishXp;
    public string FishXpText => $"+{FishXpGain} XP on first catch";

    private string? _unlockBaitIconPath;
    private bool _unlockBaitIconRequested;
    private string? _requiredBaitIconPath;
    private bool _requiredBaitIconRequested;

    /// <summary>Extracted icon of the unlocked bait, loaded on first read.</summary>
    public string? FishUnlockBaitIconPath
    {
        get
        {
            if (!_unlockBaitIconRequested)
            {
                _unlockBaitIconRequested = true;
                LoadBaitIcon(FishUnlockBaitEntry, p =>
                {
                    _unlockBaitIconPath = p;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FishUnlockBaitIconPath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFishBaitIcon)));
                });
            }
            return _unlockBaitIconPath;
        }
    }

    public bool HasFishBaitIcon => _unlockBaitIconPath is not null;

    /// <summary>Extracted icon of the required bait (rare variants), loaded on first read.</summary>
    public string? FishRequiredBaitIconPath
    {
        get
        {
            if (!_requiredBaitIconRequested)
            {
                _requiredBaitIconRequested = true;
                LoadBaitIcon(FishRequiredBaitEntry, p =>
                {
                    _requiredBaitIconPath = p;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FishRequiredBaitIconPath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFishRequiredBaitIcon)));
                });
            }
            return _requiredBaitIconPath;
        }
    }

    public bool HasFishRequiredBaitIcon => _requiredBaitIconPath is not null;

    private static void LoadBaitIcon(Core.Items.ItemCatalogEntry? entry, Action<string> apply)
    {
        if (entry?.IconAssetPath is null) return;
        var provider = GameDataServices.Provider;
        if (provider is null) return;
        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
                var colorized = raw is null ? null : Core.Items.IconColorizer.Colorize(raw, entry);
                if (colorized is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() => apply(colorized));
                }
            }
            catch
            {
                // Icons are cosmetic.
            }
        });
    }

    // ---------- optional wiki image (fish detail pane) ----------

    private string? _wikiImagePath;
    private bool _wikiImageRequested;
    private bool _isWikiImageLoading;

    /// <summary>
    /// Wiki image file names to try for this entry (set at construction for fish, via
    /// <see cref="Core.Codex.FishWikiImages"/>). Empty = the entry has no wiki image.
    /// </summary>
    public IReadOnlyList<string> WikiImageCandidates { get; init; } = Array.Empty<string>();

    /// <summary>Local cached path of the entry's wiki image, once resolved.</summary>
    public string? WikiImagePath
    {
        get
        {
            RequestWikiImage();
            return _wikiImagePath;
        }
    }

    public bool HasWikiImage => _wikiImagePath is not null;
    public bool IsWikiImageLoading => _isWikiImageLoading;

    /// <summary>
    /// Starts resolving the wiki image (download-once, then cached on disk). Safe to
    /// call repeatedly; only the first call does anything. Resolution failures simply
    /// leave <see cref="HasWikiImage"/> false so the UI collapses the image block.
    /// </summary>
    internal void RequestWikiImage()
    {
        if (_wikiImageRequested || WikiImageCandidates.Count == 0) return;
        _wikiImageRequested = true;
        _isWikiImageLoading = true;
        MainThread.BeginInvokeOnMainThread(() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWikiImageLoading))));
        _ = LoadWikiImageAsync();
    }

    private async Task LoadWikiImageAsync()
    {
        string? path = null;
        try
        {
            path = await Core.Assets.WikiImageCache.Default
                .GetFirstAsync(WikiImageCandidates).ConfigureAwait(false);
        }
        catch
        {
            // The image is cosmetic; the cache already Warn-logged the failure.
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isWikiImageLoading = false;
            _wikiImagePath = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWikiImageLoading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WikiImagePath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWikiImage)));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The lore/codex browser for one player save: every email (full text), journal
/// objective and compendium entry in the game, with this player's read/found state.
/// </summary>
public sealed class CodexViewModel : INotifyPropertyChanged
{
    public enum Section { Emails, Journal, Compendium, Fish }

    private readonly List<CodexItemViewModel> _emails;
    private readonly List<CodexItemViewModel> _journals;
    private readonly List<CodexItemViewModel> _compendium;
    private readonly List<CodexItemViewModel> _fish;
    private readonly Dictionary<string, int> _killBaseline;
    private HashSet<string> _fishBaseline;
    private readonly List<string> _unknownEmails;
    private readonly List<string> _unknownJournals;
    private readonly List<string> _origCompEmail;
    private readonly List<string> _origCompNarrative;
    private readonly List<string> _origCompExploration;
    private HashSet<string> _emailBaseline;
    private HashSet<string> _journalBaseline;
    private HashSet<string> _compendiumBaseline;
    private readonly Action _onChanged;

    private Section _section = Section.Emails;
    private string _searchText = string.Empty;
    private bool _unreadOnly;
    private CodexItemViewModel? _selected;
    private IReadOnlyList<CodexItemViewModel> _visible = Array.Empty<CodexItemViewModel>();

    public CodexViewModel(
        IReadOnlyList<string> emailsRead,
        IReadOnlyList<string> journalsFound,
        IReadOnlyList<string> compendiumEmail,
        IReadOnlyList<string> compendiumNarrative,
        IReadOnlyList<string> compendiumExploration,
        IReadOnlyList<Core.PlayerSaves.KillCount> killCounts,
        IReadOnlyList<string> fishCaught,
        Action onChanged)
    {
        _onChanged = onChanged;
        var killByRow = killCounts.ToDictionary(k => k.CompendiumRow, k => k.Count, StringComparer.Ordinal);
        _killBaseline = new Dictionary<string, int>(killByRow, StringComparer.Ordinal);

        var readSet = new HashSet<string>(emailsRead, StringComparer.Ordinal);
        var foundSet = new HashSet<string>(journalsFound, StringComparer.Ordinal);
        var compSet = new HashSet<string>(
            compendiumEmail.Concat(compendiumNarrative).Concat(compendiumExploration),
            StringComparer.Ordinal);
        _emailBaseline = new HashSet<string>(readSet, StringComparer.Ordinal);
        _journalBaseline = new HashSet<string>(foundSet, StringComparer.Ordinal);
        _compendiumBaseline = new HashSet<string>(compSet, StringComparer.Ordinal);

        // Provenance: journals are mostly unlocked by reading specific emails.
        var journalSources = GameDataServices.Emails
            .SelectMany(e => e.UnlocksJournals.Select(j => (Journal: j, Email: e)))
            .ToLookup(t => t.Journal, t => t.Email, StringComparer.OrdinalIgnoreCase);

        _emails = GameDataServices.Emails
            .Select(e => new CodexItemViewModel(
                this, e.Id, e.Subject, e.FirstSender, RenderEmail(e), readSet.Contains(e.Id), editable: true))
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _journals = GameDataServices.Journals
            .Select(j =>
            {
                var body = j.Note;
                var sources = journalSources[j.Id].ToList();
                body += sources.Count > 0
                    ? "\n\nSOURCE: unlocked by reading the email " +
                      string.Join(" or ", sources.Select(e => $"\"{e.Subject}\" ({e.FirstSender})"))
                    : "\n\nSOURCE: granted directly by story events / area triggers (not email-gated).";
                body += $"\n[id: {j.Id}]";
                return new CodexItemViewModel(
                    this, j.Id, j.Title, j.Id, body, foundSet.Contains(j.Id), editable: true);
            })
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _compendium = GameDataServices.Compendium
            .Select(c =>
            {
                var item = new CodexItemViewModel(
                    this, c.Id, c.Title,
                    string.IsNullOrEmpty(c.Subtitle) ? c.Tag : c.Subtitle,
                    string.Join("\n\n", c.SectionTexts),
                    compSet.Contains(c.Id), editable: true, c.SectionTypes)
                {
                    KillRequired = c.KillRequired,
                };
                if (killByRow.TryGetValue(c.Id, out var kills)) item.InitKillCount(kills);
                return item;
            })
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Fish: titles/descriptions resolve through the crafted-item catalog.
        var fishSet = new HashSet<string>(fishCaught, StringComparer.Ordinal);
        _fishBaseline = new HashSet<string>(fishSet, StringComparer.Ordinal);
        var itemCatalog = GameDataServices.Catalog;
        var fishBaits = new FishBaitResolver(GameDataServices.AllFish, itemCatalog);
        _fish = GameDataServices.AllFish
            .Select(f =>
            {
                var entry = f.ItemId is null ? null : itemCatalog?.Find(f.ItemId);
                var title = entry?.DisplayName ?? f.Id;
                if (f.IsRare) title += " (rare)";
                var detail = fishBaits.Detail(f);
                return new CodexItemViewModel(
                    this, f.Id, title, f.Id, entry?.Description ?? string.Empty,
                    fishSet.Contains(f.Id), editable: true)
                {
                    IconEntry = entry,
                    WikiImageCandidates = FishWikiImages.CandidatesFor(f.Id, entry?.DisplayName),
                    FishUnlockBaitEntry = detail.UnlockBait,
                    FishRequiredBaitEntry = detail.RequiredBait,
                    FishCatchLines = detail.CatchLines,
                    FishXpGain = f.XpGain,
                };
            })
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Save rows the catalog doesn't know (renamed/removed/newer content) are
        // preserved verbatim.
        var knownEmails = new HashSet<string>(_emails.Select(e => e.Id), StringComparer.Ordinal);
        var knownJournals = new HashSet<string>(_journals.Select(j => j.Id), StringComparer.Ordinal);
        var knownFish = new HashSet<string>(_fish.Select(f => f.Id), StringComparer.Ordinal);
        _unknownEmails = emailsRead.Where(e => !knownEmails.Contains(e)).ToList();
        _unknownJournals = journalsFound.Where(j => !knownJournals.Contains(j)).ToList();

        // Unknown fish are shown (raw id) rather than hidden: they stay in the list as
        // caught entries and round-trip unless the user explicitly unticks them.
        foreach (var id in fishCaught.Where(f => !knownFish.Contains(f) && knownFish.Add(f)))
        {
            _fish.Add(new CodexItemViewModel(
                this, id, id, "unknown fish id",
                "This fish id is not in the game's DT_Fish table the editor loaded " +
                "(newer game version, or game data unavailable). It is preserved in the save unless unticked.",
                known: true, editable: true));
        }
        _fish.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title));
        // Compendium: keep the game's original array placement verbatim - saves from
        // older patches park some rows in a different array than current section types
        // suggest, and we must not "fix" entries the user never touched.
        _origCompEmail = compendiumEmail.ToList();
        _origCompNarrative = compendiumNarrative.ToList();
        _origCompExploration = compendiumExploration.ToList();

        ShowEmailsCommand = new RelayCommand(() => ActiveSection = Section.Emails);
        ShowJournalCommand = new RelayCommand(() => ActiveSection = Section.Journal);
        ShowCompendiumCommand = new RelayCommand(() => ActiveSection = Section.Compendium);
        ShowFishCommand = new RelayCommand(() => ActiveSection = Section.Fish);
        MarkAllReadCommand = new RelayCommand(MarkAllRead);
        ApplyFilter();
    }

    public bool IsAvailable => _emails.Count > 0 || _journals.Count > 0 || _compendium.Count > 0;

    private static string RenderEmail(EmailEntry e)
    {
        var parts = e.Sections.Select(s => string.IsNullOrEmpty(s.Sender)
            ? s.Text
            : $"FROM: {s.Sender}\n\n{s.Text}");
        var body = string.Join("\n\n— — —\n\n", parts);
        if (e.AttachmentRecipes.Count > 0)
        {
            body += $"\n\nATTACHMENT UNLOCKS RECIPE: {string.Join(", ", e.AttachmentRecipes)}";
        }
        if (e.UnlocksJournals.Count > 0)
        {
            body += $"\n\nREADING THIS UNLOCKS JOURNAL: {string.Join(", ", e.UnlocksJournals)}";
        }
        body += "\n\nFound at e-mail terminals scattered through the facility (the game does not record which terminal carries which message)."
              + $"\n[id: {e.Id}]";
        return body;
    }

    // ---------- section switching ----------

    public Section ActiveSection
    {
        get => _section;
        set
        {
            if (Set(ref _section, value))
            {
                foreach (var n in new[] { nameof(IsEmailsSection), nameof(IsJournalSection), nameof(IsCompendiumSection), nameof(CountText) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
                Selected = null;
                ApplyFilter();
            }
        }
    }

    public bool IsEmailsSection => _section == Section.Emails;
    public bool IsJournalSection => _section == Section.Journal;
    public bool IsCompendiumSection => _section == Section.Compendium;
    public bool IsFishSection => _section == Section.Fish;

    public ICommand ShowEmailsCommand { get; }
    public ICommand ShowJournalCommand { get; }
    public ICommand ShowCompendiumCommand { get; }
    public ICommand ShowFishCommand { get; }
    public ICommand MarkAllReadCommand { get; }

    private List<CodexItemViewModel> CurrentItems => _section switch
    {
        Section.Journal => _journals,
        Section.Compendium => _compendium,
        Section.Fish => _fish,
        _ => _emails,
    };

    // ---------- filtering + selection ----------

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty)) ApplyFilter();
        }
    }

    // ---------- compendium categories (GATEPal's own apps) ----------

    /// <summary>The in-game GATEPal compendium apps, mapped to DT_Compendium tags.</summary>
    public IReadOnlyList<CompendiumCategory> CompendiumCategories { get; } = new[]
    {
        new CompendiumCategory("ALL", null),
        new CompendiumCategory("ENTITIES", "Entity"),
        new CompendiumCategory("LOCATIONS", "Location"),
        new CompendiumCategory("REGISTRY", "IS"),
        new CompendiumCategory("PEOPLE", "People"),
        new CompendiumCategory("THEORIES", "Theories"),
    };

    private CompendiumCategory? _compendiumCategory;

    public CompendiumCategory? SelectedCompendiumCategory
    {
        get => _compendiumCategory ??= CompendiumCategories[0];
        set
        {
            if (Set(ref _compendiumCategory, value)) ApplyFilter();
        }
    }

    public System.Windows.Input.ICommand SelectCompendiumCategoryCommand
        => _selectCompendiumCategoryCommand ??= new RelayCommand<CompendiumCategory>(c => SelectedCompendiumCategory = c ?? CompendiumCategories[0]);
    private RelayCommand<CompendiumCategory>? _selectCompendiumCategoryCommand;

    public bool UnreadOnly
    {
        get => _unreadOnly;
        set
        {
            if (Set(ref _unreadOnly, value)) ApplyFilter();
        }
    }

    public IReadOnlyList<CodexItemViewModel> VisibleItems
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    public CodexItemViewModel? Selected
    {
        get => _selected;
        set
        {
            // A sealed (future, region-gated) entry can't open in the reading pane; tapping
            // it prompts a clearance override instead and the selection snaps back.
            if (value is { IsConcealed: true })
            {
                _ = value.RevealAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                return;
            }
            if (Set(ref _selected, value))
            {
                // Kick off the (cached) wiki image fetch as soon as the row is picked
                // so the detail pane's placeholder state is deterministic.
                value?.RequestWikiImage();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Re-filters after a row was individually revealed (it may now sort/show differently).</summary>
    internal void OnRevealed() => ApplyFilter();

    public string CountText
    {
        get
        {
            var items = CurrentItems;
            var known = items.Count(i => i.IsKnown);
            var noun = _section switch
            {
                Section.Journal => "found",
                Section.Compendium => "unlocked",
                Section.Fish => "caught",
                _ => "read",
            };
            return $"{known} of {items.Count} {noun}";
        }
    }

    private readonly Dictionary<string, string?> _compendiumTagById = new(StringComparer.Ordinal);

    private void ApplyFilter()
    {
        IEnumerable<CodexItemViewModel> q = CurrentItems;
        if (_section == Section.Compendium && SelectedCompendiumCategory?.TagPart is { } tagPart)
        {
            if (_compendiumTagById.Count == 0)
            {
                foreach (var c in GameDataServices.Compendium) _compendiumTagById[c.Id] = c.Tag;
            }
            q = q.Where(i => _compendiumTagById.TryGetValue(i.Id, out var tag)
                          && tag?.Contains(tagPart, StringComparison.OrdinalIgnoreCase) == true);
        }
        if (_unreadOnly) q = q.Where(i => !i.IsKnown);
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var f = _searchText.Trim();
            q = q.Where(i =>
                i.Title.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                i.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (i.Subtitle?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                i.Body.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        VisibleItems = q.ToList();
    }

    private bool _suppressNotifications;

    private void MarkAllRead()
    {
        _suppressNotifications = true;
        try
        {
            foreach (var i in CurrentItems) i.IsKnown = true;
        }
        finally
        {
            _suppressNotifications = false;
        }
        OnItemToggled();
        ApplyFilter();
    }

    internal void OnItemToggled()
    {
        if (_suppressNotifications) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        _onChanged();
    }

    // ---------- save plumbing ----------

    public bool IsDirty =>
        !CurrentEmailSet().SetEquals(_emailBaseline)
        || !CurrentJournalSet().SetEquals(_journalBaseline)
        || !CurrentCompendiumSet().SetEquals(_compendiumBaseline)
        || !CurrentFishSet().SetEquals(_fishBaseline)
        || AreKillsDirty();

    private bool AreKillsDirty()
        => _compendium.Any(c => c.KillCount is { } k
            && _killBaseline.TryGetValue(c.Id, out var b) && k != b);

    public List<string> CurrentFishCaught()
        => _fish.Where(f => f.IsKnown).Select(f => f.Id).ToList();

    /// <summary>Updated kill tallies (only rows the save already tracks).</summary>
    public List<Core.PlayerSaves.KillCount> CurrentKillCounts()
        => _compendium.Where(c => c.KillCount is not null)
            .Select(c => new Core.PlayerSaves.KillCount(c.Id, c.KillCount!.Value))
            .ToList();

    private HashSet<string> CurrentFishSet()
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in _fish) if (f.IsKnown) s.Add(f.Id);
        return s;
    }

    public List<string> CurrentEmailsRead()
        => _unknownEmails.Concat(_emails.Where(e => e.IsKnown).Select(e => e.Id)).ToList();

    public List<string> CurrentJournals()
        => _unknownJournals.Concat(_journals.Where(j => j.IsKnown).Select(j => j.Id)).ToList();

    /// <summary>
    /// The three compendium arrays to write back. Original placements are preserved for
    /// entries the user didn't touch; re-locked rows are dropped from all arrays; newly
    /// unlocked rows go into the array(s) matching their sections' unlock types
    /// (Email / NarrativeNPC / Exploration; unknown types default to Exploration).
    /// </summary>
    public (List<string> Email, List<string> Narrative, List<string> Exploration) CurrentCompendiumLists()
    {
        var lockedNow = new HashSet<string>(
            _compendium.Where(c => !c.IsKnown).Select(c => c.Id), StringComparer.Ordinal);

        var email = _origCompEmail.Where(r => !lockedNow.Contains(r)).ToList();
        var narrative = _origCompNarrative.Where(r => !lockedNow.Contains(r)).ToList();
        var exploration = _origCompExploration.Where(r => !lockedNow.Contains(r)).ToList();

        var alreadyPlaced = new HashSet<string>(
            email.Concat(narrative).Concat(exploration), StringComparer.Ordinal);

        foreach (var c in _compendium)
        {
            if (!c.IsKnown || alreadyPlaced.Contains(c.Id)) continue;
            var types = c.SectionTypes.Count > 0 ? c.SectionTypes : new[] { "Exploration" };
            foreach (var t in types)
            {
                var target = t.StartsWith("Email", StringComparison.OrdinalIgnoreCase) ? email
                    : t.StartsWith("Narrative", StringComparison.OrdinalIgnoreCase) ? narrative
                    : exploration;
                if (!target.Contains(c.Id, StringComparer.Ordinal)) target.Add(c.Id);
            }
        }
        return (email, narrative, exploration);
    }

    private HashSet<string> CurrentEmailSet()
    {
        var s = new HashSet<string>(_unknownEmails, StringComparer.Ordinal);
        foreach (var e in _emails) if (e.IsKnown) s.Add(e.Id);
        return s;
    }

    private HashSet<string> CurrentJournalSet()
    {
        var s = new HashSet<string>(_unknownJournals, StringComparer.Ordinal);
        foreach (var j in _journals) if (j.IsKnown) s.Add(j.Id);
        return s;
    }

    private HashSet<string> CurrentCompendiumSet()
    {
        var (email, narrative, exploration) = CurrentCompendiumLists();
        return new HashSet<string>(email.Concat(narrative).Concat(exploration), StringComparer.Ordinal);
    }

    public void AcceptCurrentAsBaseline()
    {
        _emailBaseline = CurrentEmailSet();
        _journalBaseline = CurrentJournalSet();
        _compendiumBaseline = CurrentCompendiumSet();
        _fishBaseline = CurrentFishSet();
        _killBaseline.Clear();
        foreach (var k in CurrentKillCounts()) _killBaseline[k.CompendiumRow] = k.Count;
    }

    public void Revert()
    {
        _suppressNotifications = true;
        try
        {
            foreach (var e in _emails) e.IsKnown = _emailBaseline.Contains(e.Id);
            foreach (var j in _journals) j.IsKnown = _journalBaseline.Contains(j.Id);
            foreach (var c in _compendium)
            {
                c.IsKnown = _compendiumBaseline.Contains(c.Id);
                if (c.KillCount is not null && _killBaseline.TryGetValue(c.Id, out var k))
                {
                    c.KillCount = k;
                }
            }
            foreach (var f in _fish) f.IsKnown = _fishBaseline.Contains(f.Id);
        }
        finally
        {
            _suppressNotifications = false;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        ApplyFilter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>One GATEPal compendium app bucket, mapped to a DT_Compendium tag fragment.</summary>
public sealed record CompendiumCategory(string Label, string? TagPart)
{
    public override string ToString() => Label;
}
