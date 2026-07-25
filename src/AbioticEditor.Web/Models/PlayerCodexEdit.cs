using System.Globalization;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>Game-data vocabulary used to turn the save's opaque codex ids into readable rows.</summary>
public sealed record CodexVocabulary(
    IReadOnlyList<EmailEntry> Emails,
    IReadOnlyList<JournalEntry> Journals,
    IReadOnlyList<CompendiumEntry> Compendium,
    IReadOnlyList<FishDefinition> Fish)
{
    public static readonly CodexVocabulary Empty = new([], [], [], []);

    public bool IsEmpty => Emails.Count == 0 && Journals.Count == 0 && Compendium.Count == 0 && Fish.Count == 0;
}

public sealed class PlayerCodexEdit
{
    private readonly HashSet<string> _emailOriginal;
    private readonly HashSet<string> _narrativeOriginal;
    private readonly HashSet<string> _explorationOriginal;
    private Func<string, object?[], string> _localize = FallbackResource;

    private PlayerCodexEdit(IEnumerable<string> emailOriginal, IEnumerable<string> narrativeOriginal, IEnumerable<string> explorationOriginal)
    {
        _emailOriginal = new(emailOriginal, StringComparer.Ordinal);
        _narrativeOriginal = new(narrativeOriginal, StringComparer.Ordinal);
        _explorationOriginal = new(explorationOriginal, StringComparer.Ordinal);
    }

    public IReadOnlyList<CodexRowEdit> Emails { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Journals { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Compendium { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Fish { get; private set; } = [];

    /// <summary>Whether these rows were built with real game-data names (vs. raw save ids only).</summary>
    public bool HasVocabulary { get; private set; }

    public static PlayerCodexEdit Create(PlayerSaveData data, CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        var result = new PlayerCodexEdit(data.CompendiumEmail, data.CompendiumNarrative, data.CompendiumExploration);
        if (localize is not null) result._localize = localize;
        var emailSet = new HashSet<string>(data.EmailsRead, StringComparer.Ordinal);
        var journalSet = new HashSet<string>(data.Journals, StringComparer.Ordinal);
        var compSet = new HashSet<string>(data.CompendiumUnlocked, StringComparer.Ordinal);
        var fishSet = new HashSet<string>(data.FishCaught, StringComparer.Ordinal);
        result.Load(data, vocabulary, emailSet, emailSet, journalSet, journalSet, compSet, compSet, fishSet, fishSet);
        return result;
    }

    /// <summary>
    /// Rebuilds the rows with a vocabulary that finished loading after this edit model was
    /// created (the GATEPal tab loads game data on demand because selecting a save never
    /// scans game paks). Staged read/found/unlocked/caught ticks survive because rows are
    /// re-keyed by id. Returns false when nothing changed (a vocabulary was already applied,
    /// or the given one is empty).
    /// </summary>
    public bool ApplyVocabulary(PlayerSaveData data, CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        if (HasVocabulary || vocabulary.IsEmpty) return false;
        if (localize is not null) _localize = localize;
        Load(data, vocabulary,
            Emails.Select(r => r.Id).ToList(), CurrentEmails(),
            Journals.Select(r => r.Id).ToList(), CurrentJournals(),
            Compendium.Select(r => r.Id).ToList(), CurrentCompendium(),
            Fish.Select(r => r.Id).ToList(), CurrentFish());
        return true;
    }

    private void Load(PlayerSaveData data, CodexVocabulary vocabulary,
        IReadOnlyCollection<string> emailIds, HashSet<string> emailsKnown,
        IReadOnlyCollection<string> journalIds, HashSet<string> journalsKnown,
        IReadOnlyCollection<string> compendiumIds, HashSet<string> compendiumKnown,
        IReadOnlyCollection<string> fishIds, HashSet<string> fishKnown)
    {
        // Save-tracked kill tallies, with any staged edits from a previous build overlaid.
        var kills = data.KillCounts.ToDictionary(k => k.CompendiumRow, k => k.Count, StringComparer.Ordinal);
        foreach (var row in Compendium) if (row.KillCount is { } staged) kills[row.Id] = staged;
        HasVocabulary = !vocabulary.IsEmpty;
        // With game data loaded, ids the data no longer defines are hidden like native (but
        // still saved); without game data every row is a fallback row and must stay visible.
        var saveOnly = HasVocabulary;

        Emails = Merge(vocabulary.Emails.Select(e => new CodexRowEdit(e.Id, e.Subject, e.FirstSender,
            EmailBody(e), emailsKnown.Contains(e.Id), true, [])),
            emailIds, id => new CodexRowEdit(id, id, "Unknown email", "This entry is retained from the save.", emailsKnown.Contains(id), true, []) { SaveOnly = saveOnly });
        Journals = Merge(vocabulary.Journals.Select(j => new CodexRowEdit(j.Id, j.Title, j.Id, j.Note, journalsKnown.Contains(j.Id), true, [])),
            journalIds, id => new CodexRowEdit(id, id, "Unknown journal", "This entry is retained from the save.", journalsKnown.Contains(id), true, []) { SaveOnly = saveOnly });
        Compendium = Merge(vocabulary.Compendium.Select(c => new CodexRowEdit(c.Id, c.Title, c.Subtitle ?? c.Tag,
            string.Join("\n\n", c.SectionTexts), compendiumKnown.Contains(c.Id), true, c.SectionTypes, c.KillRequired,
            kills.TryGetValue(c.Id, out var count) ? count : null) { Tag = c.Tag }), compendiumIds,
            id => new CodexRowEdit(id, id, "Unknown compendium entry", "This entry is retained from the save.", compendiumKnown.Contains(id), true, []) { SaveOnly = saveOnly });
        Fish = Merge(vocabulary.Fish.Select(f => new CodexRowEdit(f.Id, f.Id + (f.IsRare ? " (rare)" : ""), f.Location,
            FishBody(f), fishKnown.Contains(f.Id), true, []) { ItemId = f.ItemId }), fishIds,
            id => new CodexRowEdit(id, id, "Unknown fish", "This entry is retained from the save.", fishKnown.Contains(id), true, []) { SaveOnly = saveOnly });
    }

    public HashSet<string> CurrentEmails() => RowsToSet(Emails);
    public HashSet<string> CurrentJournals() => RowsToSet(Journals);
    public HashSet<string> CurrentCompendium() => RowsToSet(Compendium);
    public HashSet<string> CurrentFish() => RowsToSet(Fish);
    public Dictionary<string, int> CurrentKills() => Compendium.Where(r => r.KillCount is not null).ToDictionary(r => r.Id, r => Math.Max(0, r.KillCount!.Value), StringComparer.Ordinal);

    public (List<string> Email, List<string> Narrative, List<string> Exploration) CompendiumArrays()
    {
        var enabled = CurrentCompendium();
        // Preserve an existing array location; newly unlocked known rows are placed in each
        // section required by their game-data definition.
        var email = new HashSet<string>(_emailOriginal.Where(enabled.Contains), StringComparer.Ordinal);
        var narrative = new HashSet<string>(_narrativeOriginal.Where(enabled.Contains), StringComparer.Ordinal);
        var exploration = new HashSet<string>(_explorationOriginal.Where(enabled.Contains), StringComparer.Ordinal);
        foreach (var row in Compendium.Where(r => r.IsKnown && !email.Contains(r.Id) && !narrative.Contains(r.Id) && !exploration.Contains(r.Id)))
        {
            if (row.SectionTypes.Contains("Email", StringComparer.OrdinalIgnoreCase)) email.Add(row.Id);
            if (row.SectionTypes.Contains("Narrative", StringComparer.OrdinalIgnoreCase)) narrative.Add(row.Id);
            if (row.SectionTypes.Contains("Exploration", StringComparer.OrdinalIgnoreCase)) exploration.Add(row.Id);
            if (row.SectionTypes.Count == 0) exploration.Add(row.Id);
        }
        return (email.OrderBy(x => x, StringComparer.Ordinal).ToList(), narrative.OrderBy(x => x, StringComparer.Ordinal).ToList(), exploration.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    public void SetFrom(ISet<string> emails, ISet<string> journals, ISet<string> compendium, ISet<string> fish, IReadOnlyDictionary<string, int> kills)
    {
        SetRows(Emails, emails); SetRows(Journals, journals); SetRows(Compendium, compendium); SetRows(Fish, fish);
        foreach (var row in Compendium) if (row.KillCount is not null && kills.TryGetValue(row.Id, out var count)) row.KillCount = count;
    }

    private static List<CodexRowEdit> Merge(IEnumerable<CodexRowEdit> known, IEnumerable<string> saved, Func<string, CodexRowEdit> unknown)
    {
        var rows = known.ToList(); var ids = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        rows.AddRange(saved.Where(ids.Add).Select(unknown));
        return rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static HashSet<string> RowsToSet(IEnumerable<CodexRowEdit> rows) => rows.Where(r => r.IsKnown).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
    private static void SetRows(IEnumerable<CodexRowEdit> rows, ISet<string> known) { foreach (var row in rows) row.IsKnown = known.Contains(row.Id); }

    /// <summary>
    /// Renders an email exactly like the native GATEPal: each section prefixed with its
    /// localized FROM line, sections separated by a rule, then the attachment/journal
    /// unlock notes, the terminals footnote, and the raw row id.
    /// </summary>
    private string EmailBody(EmailEntry e)
    {
        var parts = e.Sections.Select(s => string.IsNullOrEmpty(s.Sender)
            ? s.Text
            : $"{_localize("PlayerCodex_EmailFrom", [s.Sender])}\n\n{s.Text}");
        var body = string.Join("\n\n- - -\n\n", parts);
        if (e.AttachmentRecipes.Count > 0)
        {
            body += "\n\n" + _localize("PlayerCodex_EmailAttachmentUnlocksRecipe", [string.Join(", ", e.AttachmentRecipes)]);
        }
        if (e.UnlocksJournals.Count > 0)
        {
            body += "\n\n" + _localize("PlayerCodex_EmailUnlocksJournal", [string.Join(", ", e.UnlocksJournals)]);
        }
        body += "\n\n" + _localize("PlayerCodex_EmailFoundAtTerminals", [])
              + $"\n[id: {e.Id}]";
        return body;
    }

    /// <summary>
    /// Fish carry no prose body. Everything a fish row knows (where it bites, the bait it wants,
    /// the bait it unlocks, the XP) is laid out properly by the tab's WHEN YOU CATCH IT and TO
    /// CATCH IT sections via <c>FishCatchDetails</c>. This used to return an untranslated
    /// "Location: ... / Unlocks recipe: recipe_bait_antefish / XP: 150" dump printed directly
    /// underneath those sections, repeating them and leaking raw ids into the reading pane.
    /// </summary>
    private static string FishBody(FishDefinition fish) => string.Empty;

    // English fallback for hosts without a language service (unit tests, tooling). The
    // values mirror the AppResources.resx defaults so both paths render identical bodies.
    private static readonly Dictionary<string, string> FallbackStrings = new(StringComparer.Ordinal)
    {
        ["PlayerCodex_EmailFrom"] = "FROM: {0}",
        ["PlayerCodex_EmailAttachmentUnlocksRecipe"] = "ATTACHMENT UNLOCKS RECIPE: {0}",
        ["PlayerCodex_EmailUnlocksJournal"] = "READING THIS UNLOCKS JOURNAL: {0}",
        ["PlayerCodex_EmailFoundAtTerminals"] = "Found at e-mail terminals scattered through the facility (the game does not record which terminal carries which message).",
    };

    private static string FallbackResource(string key, object?[] args)
        => FallbackStrings.TryGetValue(key, out var format)
            ? string.Format(CultureInfo.InvariantCulture, format, args)
            : key;
}

public sealed class CodexRowEdit(string id, string title, string? subtitle, string body, bool isKnown, bool editable, IReadOnlyList<string> sectionTypes, int? killRequired = null, int? killCount = null)
{
    public string Id { get; } = id; public string Title { get; } = title; public string? Subtitle { get; } = subtitle; public string Body { get; } = body;
    public bool IsKnown { get; set; } = isKnown; public bool Editable { get; } = editable; public IReadOnlyList<string> SectionTypes { get; } = sectionTypes;
    public int? KillRequired { get; } = killRequired; public int? KillCount { get; set; } = killCount;

    /// <summary>Fish rows only: the catalog item id whose icon/display name represents this catch.</summary>
    public string? ItemId { get; init; }

    /// <summary>Compendium rows only: the DT_Compendium tag driving the GATEPal category apps.</summary>
    public string? Tag { get; init; }

    /// <summary>
    /// True for an id the save tracks but the game's current data no longer defines (an entry
    /// removed by a game update). These rows stay in the model so saving never drops the flag,
    /// but the native app builds its list from game data alone, so they are hidden from view.
    /// </summary>
    public bool SaveOnly { get; init; }
}
