using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live story/world-clock/weather session: implements the same <see cref="IWorldStorySession"/>
/// boundary the shared <c>WorldStoryTab</c> widget uses for the file editor. Wraps three channels -
/// <see cref="LiveStoryChannel"/> for the current-quest indicator and the chapter setter,
/// <see cref="LiveWorldFlagsChannel"/> to read the running world's current flag set (needed to work
/// out which flags a chapter move actually has to touch), and <see cref="LiveWorldStateChannel"/>
/// (folded in here rather than kept as its own tab/session, per the "one shared component" goal)
/// for the clock and weather, which apply immediately.
/// </summary>
/// <remarks>
/// The story chapter is a function of world flags: <see cref="SetStoryChapterAsync"/> computes the
/// same flag lists the offline <c>WorldSaveSession</c>/<c>StoryFlagSync</c> path does (every
/// chapter trigger flag up to and including the target, plus the curated
/// <see cref="FlagGate.PrerequisitesFor"/> closure, mirroring <c>WorldStoryTab</c>'s "unlock story
/// through here" action; and, for the flags a backward move leaves stranded,
/// <see cref="FlagGate.DependentsOf"/> + <see cref="FlagGate.FlagsPastChapter"/>, mirroring
/// <c>StoryFlagSync.PlanClearForwardFlags</c>) and sends them to the mod in one request, which
/// applies them through the same native <c>UWorldFlagSubsystem::SetWorldFlag</c> call
/// <c>flags.set</c> uses and then nudges the replicated <c>CurrentQuest</c> row as a
/// belt-and-braces extra - see <c>areas/story.lua</c>'s header comment for the full grounding.
/// </remarks>
public sealed class LiveStorySession : IWorldStorySession
{
    private readonly LiveStoryChannel _storyChannel;
    private readonly LiveWorldStateChannel _worldChannel;
    private readonly LiveWorldFlagsChannel _flagsChannel;
    private readonly LiveWorldUnlocksChannel _unlocksChannel;
    private LiveStoryState _story;
    private LiveWorldState _world;
    private LiveWorldUnlocks? _unlocks;

    private LiveStorySession(LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel,
        LiveWorldFlagsChannel flagsChannel, LiveWorldUnlocksChannel unlocksChannel, LiveStoryState story, LiveWorldState world, LiveWorldUnlocks? unlocks)
    {
        _storyChannel = storyChannel;
        _worldChannel = worldChannel;
        _flagsChannel = flagsChannel;
        _unlocksChannel = unlocksChannel;
        _story = story;
        _world = world;
        _unlocks = unlocks;
    }

    public static async Task<LiveStorySession> ConnectAsync(
        LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel, LiveWorldFlagsChannel flagsChannel, LiveWorldUnlocksChannel unlocksChannel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storyChannel);
        ArgumentNullException.ThrowIfNull(worldChannel);
        ArgumentNullException.ThrowIfNull(flagsChannel);
        ArgumentNullException.ThrowIfNull(unlocksChannel);
        var story = await storyChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        var world = await worldChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        // World-level unlocks are a nice-to-have next to the clock/weather/story read above:
        // an agent build too old to know "worldunlocks.get" (or a momentary read failure) should
        // not stop the whole story tab from connecting - it just shows no world-recipes browser
        // (SupportsRecipes stays false) instead of failing the connection outright.
        LiveWorldUnlocks? unlocks = null;
        try { unlocks = await unlocksChannel.GetAsync(cancellationToken).ConfigureAwait(false); }
        catch (AbioticEditor.Core.LiveEditing.LiveAgentException) { /* see remarks above */ }
        return new LiveStorySession(storyChannel, worldChannel, flagsChannel, unlocksChannel, story, world, unlocks);
    }

    public bool AppliesImmediately => true;
    public bool IsHost => _world.IsHost;
    public string? Status { get; private set; }

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (chapter, clock, or weather), so the tab can redraw without knowing which.</summary>
    public event Action? Changed;

    // ---------- story chapter / progression ----------

    public bool CanShowStory => true;

    /// <summary>The live game's current-quest row, fed into the same
    /// <c>StoryProgressionCatalog</c> lookup the file editor uses - a row it does not recognise
    /// simply renders as "unknown chapter", the existing graceful fallback.</summary>
    public string? StoryProgressionRow => string.Equals(_story.CurrentQuestRow, "None", StringComparison.Ordinal) ? null : _story.CurrentQuestRow;

    public bool CanSetStoryChapter => IsHost;

    /// <summary>
    /// Moves the running world's story chapter to <paramref name="row"/> by computing the same
    /// flag lists the offline editor's chapter SET action does (see the class remarks) from the
    /// running world's own current flag set, then sending them to the mod in one request.
    /// </summary>
    public async Task SetStoryChapterAsync(string row, CancellationToken cancellationToken = default)
    {
        var directory = await _flagsChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        var currentlySet = directory.Flags.Where(f => f.IsSet).Select(f => f.Name).ToList();
        // The running game's own flag table is the truth about which names exist: anything the
        // curated catalogs name that the game does not know is left out of the request rather
        // than sent for the game to reject (a mistyped prerequisite once failed the whole
        // chapter change this way). The mod skips such names too, as a second line of defence.
        var known = new HashSet<string>(directory.Flags.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var (flagsToSet, flagsToClear) = ComputeFlagPlan(row, currentlySet, known);

        var skipped = await _storyChannel.SetAsync(row, flagsToSet, flagsToClear, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        Status = LiveWorldFlagsSession.SkippedFlagsStatus(skipped);
    }

    /// <summary>
    /// Pure flag-list computation for moving the story to <paramref name="targetRow"/>, factored
    /// out of <see cref="SetStoryChapterAsync"/> so it is directly testable without a live
    /// connection. Mirrors the offline editor's chapter SET action (<c>StoryFlagSync.PlanSyncToChapter</c>
    /// / <c>PlanClearForwardFlags</c>, and <c>WorldStoryTab</c>'s "unlock story through here" action
    /// for the prerequisite closure): forward, every chapter trigger flag through the target plus
    /// the curated <see cref="FlagGate.PrerequisitesFor"/> closure, excluding anything already set;
    /// backward, every chapter/quest flag that belongs strictly after the target and is currently
    /// set (<see cref="FlagGate.DependentsOf"/> + <see cref="FlagGate.FlagsPastChapter"/>) - a
    /// no-op when moving forward, since none of those flags are set yet.
    /// </summary>
    public static (IReadOnlyList<string> FlagsToSet, IReadOnlyList<string> FlagsToClear) ComputeFlagPlan(
        string targetRow, IReadOnlyCollection<string> currentlySet, IReadOnlySet<string>? knownFlags = null)
    {
        var targetIndex = StoryProgressionCatalog.IndexOf(targetRow);
        if (targetIndex < 0) throw new InvalidOperationException($"Unknown chapter '{targetRow}'.");
        // When the caller knows which flag names the game actually has (the live directory),
        // the plan is limited to those; null means "trust the catalogs" (tests, offline).
        bool IsKnown(string flag) => knownFlags is null || knownFlags.Contains(flag);

        var haveSet = new HashSet<string>(currentlySet, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var triggersThroughTarget = new List<string>();
        for (var i = 0; i <= targetIndex; i++)
        {
            if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } trigger && seen.Add(trigger))
                triggersThroughTarget.Add(trigger);
        }
        // Snapshot before appending: FlagGate.PrerequisitesFor is walked over the triggers found
        // so far, not the prerequisites being appended onto the same list as we go.
        foreach (var prereq in triggersThroughTarget.ToList().SelectMany(FlagGate.PrerequisitesFor))
        {
            if (seen.Add(prereq)) triggersThroughTarget.Add(prereq);
        }
        var flagsToSet = triggersThroughTarget.Where(f => !haveSet.Contains(f) && IsKnown(f)).ToList();

        var forwardTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = targetIndex + 1; i < StoryProgressionCatalog.Chapters.Count; i++)
        {
            if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } trigger) forwardTriggers.Add(trigger);
        }
        var toClear = new HashSet<string>(FlagGate.DependentsOf(forwardTriggers, currentlySet), StringComparer.OrdinalIgnoreCase);
        toClear.UnionWith(FlagGate.FlagsPastChapter(targetIndex, currentlySet));
        var flagsToClear = toClear.Where(f => haveSet.Contains(f) && IsKnown(f)).ToList();

        return (flagsToSet, flagsToClear);
    }

    public int? MinutesPassed => _world.MinutesPassed;
    public bool CanSetMinutesPassed => IsHost && _world.CanSetMinutesPassed;

    public async Task SetMinutesPassedAsync(int minutes, CancellationToken cancellationToken = default)
    {
        if (!CanSetMinutesPassed) throw new InvalidOperationException("World playtime editing is unavailable from the connected agent.");
        await _worldChannel.SetMinutesPassedAsync(minutes, cancellationToken).ConfigureAwait(false);
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public string? LastPlayedText => null;

    // ---------- world clock ----------

    public double? WorldTimeSeconds => _world.TimeSeconds;
    public int? WorldDay => _world.Day;
    public bool CanSetWorldClock => _world.IsHost;

    public async Task SetWorldClockAsync(double seconds, int day, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(TimeSeconds: seconds, Day: day), cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    // ---------- weather (live only) ----------

    public bool SupportsWeather => true;
    public string? CurrentWeather => _world.CurrentWeather;
    public IReadOnlyList<string> WeatherOptions => _world.WeatherOptions;

    public async Task TriggerWeatherAsync(string weather, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(Weather: weather), cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task QueueWeatherAsync(string weather, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(NextWeather: weather), cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    // ---------- world recipes (host capability required) ----------

    public bool SupportsRecipes => _unlocks is not null;
    public IReadOnlyCollection<string> GlobalRecipeIds => _unlocks?.RecipesUnlocked ?? [];
    public bool CanEditGlobalRecipes => IsHost && _unlocks?.CanEditRecipes == true;

    /// <summary>Short reason <see cref="CanEditGlobalRecipes"/> is false, straight from the agent
    /// (<c>worldunlocks.get</c>'s <c>globalRecipeEditsUnavailableReason</c>) so <c>WorldStoryTab</c>
    /// can show a specific, localized explanation instead of just disabling the control.</summary>
    public string? GlobalRecipeEditsUnavailableReason => _unlocks?.GlobalRecipeEditsUnavailableReason;

    public async Task SetGlobalRecipesAsync(IEnumerable<string> ids, bool unlocked, CancellationToken cancellationToken = default)
    {
        if (!CanEditGlobalRecipes) throw new InvalidOperationException("Global recipes require host authority and UE4SS TSet support.");
        var edits = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .Select(id => new LiveWorldRecipeEdit(id, unlocked)).ToArray();
        if (edits.Length == 0) return;
        await _unlocksChannel.SetRecipesAsync(edits, cancellationToken).ConfigureAwait(false);
        _unlocks = await _unlocksChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        Status = null;
        Changed?.Invoke();
    }

    // ---------- world-wide item/codex lists (round 106; host capability required) ----------
    //
    // Not part of IWorldStorySession: the offline file session (WorldSaveSession) has no UI for
    // these six lists either (only world recipes get an offline WORLD RECIPES browser today), so
    // widening the shared interface would force an offline implementation with nothing to mirror.
    // These are plain members on the concrete live session instead, ready for a future
    // WorldStoryTab surface without touching the file-session boundary.

    /// <summary>Round 112: whether the "WORLD-WIDE SEEN" section of the shared <c>WorldStoryTab</c>
    /// has anything to show at all, mirroring <see cref="SupportsRecipes"/> - false only when the
    /// connected agent build predates <c>worldunlocks.get</c>'s six list fields (or the read
    /// failed), in which case the section stays hidden rather than showing empty lists.</summary>
    public bool SupportsGlobalLists => _unlocks is not null;

    /// <summary>Whether the six world-wide item/codex lists below can be edited: host authority
    /// and replication-notification support, same as recipes but without the extra TSet-capability
    /// check (these are plain <c>FArrayProperty</c> lists, not TSets) - see
    /// <see cref="LiveWorldUnlocksChannel"/>'s remarks.</summary>
    public bool CanEditGlobalLists => IsHost && _unlocks?.CanEditGlobalLists == true;

    /// <summary>Short reason <see cref="CanEditGlobalLists"/> is false, straight from the agent
    /// (<c>worldunlocks.get</c>'s <c>globalListEditsUnavailableReason</c>).</summary>
    public string? GlobalListEditsUnavailableReason => _unlocks?.GlobalListEditsUnavailableReason;

    public IReadOnlyCollection<string> GlobalItemsPickedUpIds => _unlocks?.ItemsPickedUp ?? [];
    public IReadOnlyCollection<string> GlobalEmailsReadIds => _unlocks?.EmailsRead ?? [];
    public IReadOnlyCollection<string> GlobalJournalEntryIds => _unlocks?.JournalEntries ?? [];
    public IReadOnlyCollection<string> GlobalCompendiumEmailIds => _unlocks?.CompendiumEmail ?? [];
    public IReadOnlyCollection<string> GlobalCompendiumNarrativeIds => _unlocks?.CompendiumNarrative ?? [];
    public IReadOnlyCollection<string> GlobalCompendiumExplorationIds => _unlocks?.CompendiumExploration ?? [];

    /// <summary>Adds/removes rows in one of the six world-wide lists. <paramref name="list"/> is
    /// the wire field name (<c>"itemsPickedUp"</c>, <c>"emailsRead"</c>, <c>"journalEntries"</c>,
    /// <c>"compendiumEmail"</c>, <c>"compendiumNarrative"</c>, or
    /// <c>"compendiumExploration"</c>) - see <see cref="LiveWorldUnlocksChannel.SetGlobalListAsync"/>.</summary>
    public async Task SetGlobalListAsync(string list, IEnumerable<string> ids, bool present, CancellationToken cancellationToken = default)
    {
        if (!CanEditGlobalLists) throw new InvalidOperationException("Global lists require host authority and replication notification support.");
        var edits = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .Select(id => new LiveWorldListEdit(id, present)).ToArray();
        if (edits.Length == 0) return;
        await _unlocksChannel.SetGlobalListAsync(list, edits, cancellationToken).ConfigureAwait(false);
        _unlocks = await _unlocksChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        Status = null;
        Changed?.Invoke();
    }

    // ---------- whole-session save (file session only; live applies per action) ----------

    public bool IsDirty => false;
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Revert() { }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so story progress made in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _story = await _storyChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
        try { _unlocks = await _unlocksChannel.GetAsync(cancellationToken).ConfigureAwait(false); }
        catch { /* see ConnectAsync's remarks - a failed refresh just keeps the last known list */ }
        Changed?.Invoke();
    }

    private async Task RefreshWorldAsync(CancellationToken cancellationToken)
    {
        _world = await _worldChannel.GetAsync(cancellationToken).ConfigureAwait(false);
    }
}
