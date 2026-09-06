using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s codex slice: implements the
/// same <see cref="IPlayerCodexSession"/> boundary <c>PlayerCodexTab</c> ("GATEPal") already binds
/// to (see <c>IPlayerCodexSession.cs</c>), so that widget needs zero changes to work against a
/// running game instead of a loaded file. EMAIL/NOTES/FISH/COMPENDIUM all mark known immediately
/// and can never be un-known again (<see cref="CanUnsetKnown"/> is always false - see
/// <c>LivePlayerCodexChannel</c>'s remarks). A COMPENDIUM row is only editable when its entry has
/// at least one grounded <c>ECompendiumUnlockType</c> section (<see cref="CodexRowEdit.SectionTypes"/>);
/// a row with only a kill-requirement section stays read-only, since that unlocks itself from kill
/// tracking, never from this RPC.
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
    public bool CanUnsetKnown => false;
    public string? Status { get; private set; }

    public bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null)
    {
        if (_hasVocabulary || vocabulary.IsEmpty) return false;
        _hasVocabulary = true;
        Rebuild(vocabulary);
        return true;
    }

    /// <summary>Marks one row known. Refused for a COMPENDIUM row (not <see cref="CodexRowEdit.Editable"/>)
    /// or when asked to un-know an already-known row (<see cref="CanUnsetKnown"/> is always false) -
    /// the tab disables both instead of ever calling this in either case.</summary>
    public async Task SetKnownAsync(CodexRowEdit row, bool known)
    {
        if (!row.Editable)
        {
            throw new InvalidOperationException(
                "This entry can't be changed live - the running game's unlock function for this section could not be safely grounded.");
        }
        if (!known)
        {
            throw new InvalidOperationException(
                "This entry can't be un-known while the game is running - there is no game function to do it.");
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
        Status = "Applied live - this took effect in the running game immediately.";
    }

    public void MarkChanged() { }

    /// <summary>Re-reads the live player's known e-mails/notes/fish/compendium entries.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        LoadDirectory(directory);
        Rebuild(_hasVocabulary ? _lastVocabulary : CodexVocabulary.Empty);
        Status = "Refreshed from the running game.";
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
