using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live GATEPal editing through the shared player interface. Supported sections unlock
/// through game RPCs and can be cleared by an updated host agent. Kill-only entries remain
/// read-only. Bulk unlocks group all supported sections in one request.
/// </summary>
public sealed class LivePlayerCodexSession : IPlayerCodexSession
{
    private readonly LivePlayerCodexChannel _channel;
    private string? _playerId;
    private HashSet<string> _emailIds = new(StringComparer.Ordinal);
    private HashSet<string> _journalIds = new(StringComparer.Ordinal);
    private HashSet<string> _fishIds = new(StringComparer.Ordinal);
    private HashSet<string> _compendiumIds = new(StringComparer.Ordinal);
    private bool _hasVocabulary;

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
        LivePlayerCodexChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return new LivePlayerCodexSession(channel, playerId, directory);
    }

    public IReadOnlyList<CodexRowEdit> Emails { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Journals { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Compendium { get; private set; } = [];
    public IReadOnlyList<CodexRowEdit> Fish { get; private set; } = [];
    public bool AppliesImmediately => true;
    public bool CanUnsetKnown { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Always false: a codex unlock already reached the running game by the time it
    /// returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> and after every unlock.</summary>
    public event Action? Changed;

    public bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        if (_hasVocabulary || vocabulary.IsEmpty) return false;
        _hasVocabulary = true;
        Rebuild(vocabulary);
        return true;
    }

    /// <summary>Changes one supported entry. Clearing requires the host agent capability.</summary>
    public async Task SetKnownAsync(CodexRowEdit row, bool known)
    {
        if (!row.Editable)
        {
            throw new InvalidOperationException(
                "This entry can't be changed live - the running game's unlock function for this section could not be safely grounded.");
        }
        if (!known)
        {
            if (!CanUnsetKnown) throw new InvalidOperationException("Clearing codex entries requires an updated agent with host authority.");
            var owner = FindOwner(row);
            var section = ReferenceEquals(owner, Emails) ? "emails" : ReferenceEquals(owner, Journals) ? "journals"
                : ReferenceEquals(owner, Fish) ? "fish" : ReferenceEquals(owner, Compendium) ? "compendium"
                : throw new InvalidOperationException("Unknown codex section.");
            await _channel.ClearAsync(section, [row.Id], _playerId).ConfigureAwait(false);
            row.IsKnown = false;
            FindOwnerIds(row)?.Remove(row.Id);
            Status = null;
            Changed?.Invoke();
            return;
        }

        // Which category owns this row is decided by which of the four writable lists it came
        // from, not its content - matches the separate wire fields in codex.set.
        if (ReferenceEquals(FindOwner(row), Emails)) await _channel.SetKnownAsync(emails: [row.Id], playerId: _playerId).ConfigureAwait(false);
        else if (ReferenceEquals(FindOwner(row), Journals)) await _channel.SetKnownAsync(journals: [row.Id], playerId: _playerId).ConfigureAwait(false);
        else if (ReferenceEquals(FindOwner(row), Fish)) await _channel.SetKnownAsync(fish: [row.Id], playerId: _playerId).ConfigureAwait(false);
        else if (ReferenceEquals(FindOwner(row), Compendium))
        {
            // A compendium entry can span more than one section type (e.g. an entry unlocked
            // partly by an email, partly by exploring somewhere) - one RPC call per section type
            // fully unlocks the row.
            var pairs = row.SectionTypes.Select(sectionType => new CompendiumUnlock(row.Id, sectionType)).ToList();
            if (pairs.Count == 0)
            {
                throw new InvalidOperationException(
                    "This entry has no known section type to unlock (it may only have a kill-requirement section, unlocked by kill tracking).");
            }
            await _channel.SetKnownAsync(compendium: pairs, playerId: _playerId).ConfigureAwait(false);
        }
        else throw new InvalidOperationException("Unknown codex section.");

        row.IsKnown = true;
        (FindOwnerIds(row))?.Add(row.Id);
        Status = null;
        Changed?.Invoke();
    }

    public void MarkChanged() { }

    public async Task SetKnownManyAsync(IEnumerable<CodexRowEdit> rows)
    {
        var pending = rows.Where(row => row.Editable && !row.IsKnown).Distinct().ToArray();
        if (pending.Length == 0) return;
        var emails = Emails.ToHashSet();
        var journals = Journals.ToHashSet();
        var fish = Fish.ToHashSet();
        var compendium = Compendium.ToHashSet();
        foreach (var row in pending)
        {
            if (!emails.Contains(row) && !journals.Contains(row) && !fish.Contains(row) && !compendium.Contains(row))
                throw new InvalidOperationException("Unknown codex section.");
            if (compendium.Contains(row) && row.SectionTypes.Count == 0)
                throw new InvalidOperationException("This entry has no known section type to unlock.");
        }
        await _channel.SetKnownAsync(
            emails: pending.Where(emails.Contains).Select(row => row.Id).ToArray(),
            journals: pending.Where(journals.Contains).Select(row => row.Id).ToArray(),
            fish: pending.Where(fish.Contains).Select(row => row.Id).ToArray(),
            compendium: pending.Where(compendium.Contains)
                .SelectMany(row => row.SectionTypes.Select(type => new CompendiumUnlock(row.Id, type))).Distinct().ToArray(),
            playerId: _playerId).ConfigureAwait(false);
        foreach (var row in pending)
        {
            row.IsKnown = true;
            var ids = emails.Contains(row) ? _emailIds : journals.Contains(row) ? _journalIds
                : fish.Contains(row) ? _fishIds : _compendiumIds;
            ids.Add(row.Id);
        }
        Status = null;
        Changed?.Invoke();
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a new codex entry seen in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Re-reads the live player's known e-mails/notes/fish/compendium entries.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        var entriesChanged = !_emailIds.SetEquals(directory.Emails)
            || !_journalIds.SetEquals(directory.Journals)
            || !_fishIds.SetEquals(directory.Fish)
            || !_compendiumIds.SetEquals(directory.Compendium);
        LoadDirectory(directory);
        if (entriesChanged) Rebuild(_hasVocabulary ? _lastVocabulary : CodexVocabulary.Empty);
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's codex state.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LoadDirectory(LiveCodexDirectory directory)
    {
        CanUnsetKnown = directory.CanUnsetKnown;
        _emailIds = directory.Emails.ToHashSet(StringComparer.Ordinal);
        _journalIds = directory.Journals.ToHashSet(StringComparer.Ordinal);
        _fishIds = directory.Fish.ToHashSet(StringComparer.Ordinal);
        _compendiumIds = directory.Compendium.ToHashSet(StringComparer.Ordinal);
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
        Compendium = BuildCompendiumRows(vocabulary.Compendium, _compendiumIds);
    }

    private static List<CodexRowEdit> BuildRows(
        IEnumerable<(string Id, string Title, string? Subtitle, string Body)> known, HashSet<string> knownIds, bool editable)
    {
        var rows = known.Select(row => new CodexRowEdit(
            row.Id, row.Title, row.Subtitle, row.Body, knownIds.Contains(row.Id), editable, [])).ToList();
        var seen = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in knownIds.Where(seen.Add))
            rows.Add(new CodexRowEdit(id, id, null, string.Empty, true, editable, []));
        return rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Compendium rows carry <see cref="CodexRowEdit.SectionTypes"/> (the grounded
    /// <c>ECompendiumUnlockType</c> names - see <see cref="LivePlayerCodexChannel"/>'s remarks) so
    /// <see cref="SetKnownAsync"/> knows which section(s) to unlock. A row with no known section
    /// type (only a kill-requirement section, unlocked by kill tracking rather than this RPC)
    /// stays read-only, same as the file session shows it.</summary>
    private static List<CodexRowEdit> BuildCompendiumRows(IReadOnlyList<CompendiumEntry> known, HashSet<string> knownIds)
    {
        var rows = known.Select(c => new CodexRowEdit(
            c.Id, c.Title, c.Subtitle ?? c.Tag, string.Join("\n\n", c.SectionTexts),
            knownIds.Contains(c.Id), editable: c.SectionTypes.Count > 0, c.SectionTypes) { Tag = c.Tag }).ToList();
        var seen = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in knownIds.Where(seen.Add))
            rows.Add(new CodexRowEdit(id, id, null, string.Empty, true, false, []));
        return rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<CodexRowEdit>? FindOwner(CodexRowEdit row)
        => Emails.Contains(row) ? Emails : Journals.Contains(row) ? Journals : Fish.Contains(row) ? Fish
            : Compendium.Contains(row) ? Compendium : null;

    private HashSet<string>? FindOwnerIds(CodexRowEdit row)
        => ReferenceEquals(FindOwner(row), Emails) ? _emailIds
            : ReferenceEquals(FindOwner(row), Journals) ? _journalIds
            : ReferenceEquals(FindOwner(row), Fish) ? _fishIds
            : ReferenceEquals(FindOwner(row), Compendium) ? _compendiumIds : null;
}
