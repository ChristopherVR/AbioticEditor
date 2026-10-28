using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live GATEPal editing through the shared player interface. Supported sections unlock
/// through game RPCs and can be cleared by an updated host agent. Kill-only entries remain
/// read-only. Bulk unlocks group all supported sections in one request.
/// </summary>
public sealed class LivePlayerCodexSession : IPlayerCodexSession, IPlayerQuestsSession
{
    private LiveQuestsChannel? _quests;
    private AbioticEditor.Core.LiveEditing.World.LiveWorldFlagsChannel? _flags;
    private LiveQuestState? _questState;
    // Ids are game FNames, which compare case-insensitively: the running game reports a name in
    // whatever casing was registered first (e.g. Email_Signal_Sun_14), which can differ from the
    // data table's row key (email_Signal_Sun_14). Matching case-sensitively made those rows look
    // unread again right after they were marked.
    private readonly LivePlayerCodexChannel _channel;
    private readonly object _targetGate = new();
    private string? _playerId;
    private long _targetGeneration;
    private long _historyGeneration;
    private HashSet<string> _emailIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _journalIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _fishIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _compendiumIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasVocabulary;
    private bool _canUnlockKillSections;

    private LivePlayerCodexSession(LivePlayerCodexChannel channel, string? playerId, LiveCodexDirectory directory)
    {
        _channel = channel;
        _playerId = playerId;
        LoadDirectory(directory);
        Rebuild(CodexVocabulary.Empty);
    }

    /// <summary>Connects and reads which e-mails/notes/fish/compendium entries the running
    /// character already knows, for <paramref name="playerId"/> (or the local player when
    /// omitted). Row titles/bodies are just the raw ids until <see cref="ApplyCodexVocabulary"/>
    /// supplies real names, exactly like the file session before its own on-demand vocabulary
    /// load finishes.</summary>
    public static async Task<LivePlayerCodexSession> ConnectAsync(
        LivePlayerCodexChannel channel, string? playerId = null,
        LiveQuestsChannel? quests = null, AbioticEditor.Core.LiveEditing.World.LiveWorldFlagsChannel? flags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        var session = new LivePlayerCodexSession(channel, playerId, directory) { _quests = quests, _flags = flags };
        await session.RefreshQuestsAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    // ---------- quests (Community Update 5 multi-quest) ----------

    bool IPlayerQuestsSession.SupportsMultipleQuests => _questState is not null;
    bool IPlayerQuestsSession.CanTrackQuestLines => _questState?.CanTrack == true;
    bool IPlayerQuestsSession.AppliesImmediately => true;
    string? IPlayerQuestsSession.TrackedQuestLine => _questState?.TrackedLine;
    // The personal tutorial objective is not reported live; tutorial rows stay "unknown".
    bool IPlayerQuestsSession.CompletedIntro => false;
    string? IPlayerQuestsSession.CurrentTutorialQuest => null;
    string? IPlayerQuestsSession.SavePath => null;

    /// <summary>An agent build without <c>quests.get</c> (or a momentary read failure) leaves the
    /// quests view unavailable instead of failing the whole GATEPal connection.</summary>
    private async Task RefreshQuestsAsync(CancellationToken cancellationToken)
    {
        if (_quests is null) return;
        string? playerId;
        lock (_targetGate) playerId = _playerId;
        try { _questState = await _quests.GetAsync(playerId, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _questState = null; }
    }

    async Task<QuestWorldView?> IPlayerQuestsSession.LoadWorldViewAsync()
    {
        await RefreshQuestsAsync(CancellationToken.None).ConfigureAwait(false);
        IReadOnlySet<string>? flags = null;
        if (_flags is not null)
        {
            try
            {
                var directory = await _flags.GetAsync().ConfigureAwait(false);
                flags = directory.Flags.Where(f => f.IsSet).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* the view falls back to unknown */ }
        }
        return new QuestWorldView(flags, _questState?.ActiveQuests);
    }

    async Task IPlayerQuestsSession.TrackQuestLineAsync(string? id, AbioticEditor.Core.Codex.QuestCatalogData catalog)
    {
        if (_quests is null || _questState?.CanTrack != true)
            throw new InvalidOperationException("Quest tracking can only be changed for your own character, with an updated agent.");
        if (id is not null && !catalog.Lines.Any(line => line.Id == id)) return;
        string? playerId;
        lock (_targetGate) playerId = _playerId;
        var state = await _quests.TrackAsync(id, playerId).ConfigureAwait(false);
        _questState = state;
        if (!string.Equals(state.TrackedLine, id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The game did not accept the quest line change.");
        Changed?.Invoke();
    }

    public IReadOnlyList<CodexRowEdit> Emails { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Journals { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Compendium { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Fish { get; private set; } = [];
    public bool AppliesImmediately => true;
    public bool CanUnsetKnown { get; private set; }
    public string? Status { get; private set; }
    /// <summary>Whether the current read/catch history has finished loading for the selected player.</summary>
    public bool HasCurrentHistory { get { lock (_targetGate) return _historyGeneration == _targetGeneration; } }
    public string? SelectedPlayerId { get { lock (_targetGate) return _playerId; } }

    /// <summary>Always false: a codex unlock already reached the running game by the time it
    /// returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> and after every unlock.</summary>
    public event Action? Changed;

    public bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        lock (_targetGate)
        {
            if (_hasVocabulary || vocabulary.IsEmpty) return false;
            _hasVocabulary = true;
            Rebuild(vocabulary);
            return true;
        }
    }

    /// <summary>Changes one supported entry. Clearing requires the host agent capability.</summary>
    public async Task SetKnownAsync(CodexRowEdit row, bool known)
    {
        string? playerId;
        long generation;
        CodexSection section;
        CompendiumUnlock[] pairs = [];
        lock (_targetGate)
        {
            RequireCurrentHistory();
            playerId = _playerId;
            generation = _targetGeneration;
            if (!row.Editable)
                throw new InvalidOperationException(
                    "This entry can't be changed live - the running game's unlock function for this section could not be safely grounded.");
            section = FindSection(row);
            if (!known && !CanUnsetKnown)
                throw new InvalidOperationException("Clearing codex entries requires an updated agent with host authority.");
            if (known && section == CodexSection.Compendium)
            {
                pairs = row.SectionTypes.Select(type => new CompendiumUnlock(row.Id, type)).ToArray();
                if (pairs.Length == 0)
                    throw new InvalidOperationException(
                        "This entry has no known section type to unlock (it may only have a kill-requirement section, unlocked by kill tracking).");
            }
        }
        if (!known)
        {
            await _channel.ClearAsync(section.ToString().ToLowerInvariant(), [row.Id], playerId).ConfigureAwait(false);
        }
        else
        {
            // Ownership, not the row's contents, selects the RPC field. Capture that ownership
            // with the player before the await so a switch cannot redirect the operation.
            switch (section)
            {
                case CodexSection.Emails:
                    await _channel.SetKnownAsync(emails: [row.Id], playerId: playerId).ConfigureAwait(false);
                    break;
                case CodexSection.Journals:
                    await _channel.SetKnownAsync(journals: [row.Id], playerId: playerId).ConfigureAwait(false);
                    break;
                case CodexSection.Fish:
                    await _channel.SetKnownAsync(fish: [row.Id], playerId: playerId).ConfigureAwait(false);
                    break;
                case CodexSection.Compendium:
                    await _channel.SetKnownAsync(compendium: pairs, playerId: playerId).ConfigureAwait(false);
                    break;
            }
        }
        lock (_targetGate)
        {
            if (generation != _targetGeneration) return;
            ApplyKnown(row, section, known);
            Status = null;
            Changed?.Invoke();
        }
    }

    public void MarkChanged() { }

    public async Task SetKnownManyAsync(IEnumerable<CodexRowEdit> rows)
    {
        string? playerId;
        long generation;
        (CodexRowEdit Row, CodexSection Section)[] pending;
        lock (_targetGate)
        {
            RequireCurrentHistory();
            playerId = _playerId;
            generation = _targetGeneration;
            pending = rows.Where(row => row.Editable && !row.IsKnown).Distinct()
                .Select(row => (Row: row, Section: FindSection(row))).ToArray();
            foreach (var (row, section) in pending)
                if (section == CodexSection.Compendium && row.SectionTypes.Count == 0)
                    throw new InvalidOperationException("This entry has no known section type to unlock.");
        }
        if (pending.Length == 0) return;
        await _channel.SetKnownAsync(
            emails: pending.Where(edit => edit.Section == CodexSection.Emails).Select(edit => edit.Row.Id).ToArray(),
            journals: pending.Where(edit => edit.Section == CodexSection.Journals).Select(edit => edit.Row.Id).ToArray(),
            fish: pending.Where(edit => edit.Section == CodexSection.Fish).Select(edit => edit.Row.Id).ToArray(),
            compendium: pending.Where(edit => edit.Section == CodexSection.Compendium)
                .SelectMany(edit => edit.Row.SectionTypes.Select(type => new CompendiumUnlock(edit.Row.Id, type))).Distinct().ToArray(),
            playerId: playerId).ConfigureAwait(false);
        lock (_targetGate)
        {
            if (generation != _targetGeneration) return;
            foreach (var (row, section) in pending) ApplyKnown(row, section, true);
            Status = null;
            Changed?.Invoke();
        }
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a new codex entry seen in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Re-reads the live player's known e-mails/notes/fish/compendium entries.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? playerId;
        long generation;
        lock (_targetGate) { generation = _targetGeneration; playerId = _playerId; }
        var directory = await _channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        await RefreshQuestsAsync(cancellationToken).ConfigureAwait(false);
        lock (_targetGate)
        {
            if (generation != _targetGeneration) return;
            var entriesChanged = _historyGeneration != generation
                || !_emailIds.SetEquals(directory.Emails)
                || !_journalIds.SetEquals(directory.Journals)
                || !_fishIds.SetEquals(directory.Fish)
                || !_compendiumIds.SetEquals(directory.Compendium);
            LoadDirectory(directory);
            if (entriesChanged) Rebuild(_hasVocabulary ? _lastVocabulary : CodexVocabulary.Empty);
            _historyGeneration = generation;
            Status = null;
            // The target cannot change between committing this snapshot and publishing it.
            Changed?.Invoke();
        }
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's codex state.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        lock (_targetGate)
        {
            _targetGeneration++;
            _playerId = playerId;
        }
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LoadDirectory(LiveCodexDirectory directory)
    {
        CanUnsetKnown = directory.CanUnsetKnown;
        _canUnlockKillSections = directory.CanUnlockKillSections;
        _emailIds = directory.Emails.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _journalIds = directory.Journals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _fishIds = directory.Fish.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _compendiumIds = directory.Compendium.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private CodexVocabulary _lastVocabulary = CodexVocabulary.Empty;

    private void Rebuild(CodexVocabulary vocabulary)
    {
        _lastVocabulary = vocabulary;
        Emails = BuildRows(
            vocabulary.Emails.Select(e => (e.Id, e.Subject, e.FirstSender,
                string.Join("\n\n", e.Sections.Select(s => s.Text)))),
            _emailIds, editable: true);
        Journals = BuildRows(
            vocabulary.Journals.Select(j => (j.Id, j.Title, (string?)j.Id, j.Note)),
            _journalIds, editable: true);
        Fish = BuildRows(
            vocabulary.Fish.Select(f => (f.Id, f.Id + (f.IsRare ? " (rare)" : ""), f.Location, string.Empty)),
            _fishIds, editable: true);
        Compendium = BuildCompendiumRows(vocabulary.Compendium, _compendiumIds, _canUnlockKillSections);
    }

    private static List<CodexRowEdit> BuildRows(
        IEnumerable<(string Id, string Title, string? Subtitle, string Body)> known, HashSet<string> knownIds, bool editable)
    {
        var rows = known.Select(row => new CodexRowEdit(
            row.Id, row.Title, row.Subtitle, row.Body, knownIds.Contains(row.Id), editable, [])).ToList();
        var seen = rows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in knownIds.Where(seen.Add))
            rows.Add(new CodexRowEdit(id, id, null, string.Empty, true, editable, []));
        return rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Compendium rows carry <see cref="CodexRowEdit.SectionTypes"/> (the grounded
    /// <c>ECompendiumUnlockType</c> names - see <see cref="LivePlayerCodexChannel"/>'s remarks) so
    /// <see cref="SetKnownAsync"/> knows which section(s) to unlock. A row with no known section
    /// type stays read-only, same as the file session shows it - except, when
    /// <paramref name="canUnlockKillSections"/> is true (round 106: a connected agent that maps
    /// the RPC's kill-requirement value - see <see cref="LivePlayerCodexChannel"/>'s remarks), a
    /// row whose entry has a kill-requirement section (<see cref="CompendiumEntry.KillRequired"/>)
    /// gets an extra "KillRequirement" section type here, live-only: the shared
    /// <see cref="CodexCatalog"/> model never adds it to <see cref="CompendiumEntry.SectionTypes"/>
    /// itself, since the offline file session unlocks a kill-only row through its own kill-count
    /// field instead, not through this RPC.</summary>
    private static List<CodexRowEdit> BuildCompendiumRows(
        IReadOnlyList<CompendiumEntry> known, HashSet<string> knownIds, bool canUnlockKillSections)
    {
        var rows = known.Select(c =>
        {
            IReadOnlyList<string> sectionTypes = canUnlockKillSections && c.KillRequired is not null
                ? [.. c.SectionTypes, "KillRequirement"]
                : c.SectionTypes;
            return new CodexRowEdit(
                c.Id, c.Title, c.Subtitle ?? c.Tag, string.Join("\n\n", c.SectionTexts),
                knownIds.Contains(c.Id), editable: sectionTypes.Count > 0, sectionTypes) { Tag = c.Tag };
        }).ToList();
        var seen = rows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in knownIds.Where(seen.Add))
            rows.Add(new CodexRowEdit(id, id, null, string.Empty, true, false, []));
        return rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private enum CodexSection { Emails, Journals, Fish, Compendium }

    private void RequireCurrentHistory()
    {
        if (_historyGeneration != _targetGeneration)
            throw new InvalidOperationException("The selected player's codex is still loading.");
    }

    private CodexSection FindSection(CodexRowEdit row)
        => Emails.Contains(row) ? CodexSection.Emails : Journals.Contains(row) ? CodexSection.Journals
            : Fish.Contains(row) ? CodexSection.Fish : Compendium.Contains(row) ? CodexSection.Compendium
            : throw new InvalidOperationException("Unknown codex section.");

    // Called only under the target gate. A same-player refresh may have rebuilt row objects
    // while the RPC was in flight, so update both the caller's row and its current counterpart.
    private void ApplyKnown(CodexRowEdit row, CodexSection section, bool known)
    {
        var ids = section switch
        {
            CodexSection.Emails => _emailIds, CodexSection.Journals => _journalIds,
            CodexSection.Fish => _fishIds, _ => _compendiumIds,
        };
        var current = section switch
        {
            CodexSection.Emails => Emails, CodexSection.Journals => Journals,
            CodexSection.Fish => Fish, _ => Compendium,
        };
        if (known) ids.Add(row.Id); else ids.Remove(row.Id);
        row.IsKnown = known;
        var currentRow = current.FirstOrDefault(candidate => string.Equals(candidate.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (currentRow is not null) currentRow.IsKnown = known;
    }
}
