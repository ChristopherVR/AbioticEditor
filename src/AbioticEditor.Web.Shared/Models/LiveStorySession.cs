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
    private LiveStoryState _story;
    private LiveWorldState _world;

    private LiveStorySession(LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel,
        LiveWorldFlagsChannel flagsChannel, LiveStoryState story, LiveWorldState world)
    {
        _storyChannel = storyChannel;
        _worldChannel = worldChannel;
        _flagsChannel = flagsChannel;
        _story = story;
        _world = world;
    }

    public static async Task<LiveStorySession> ConnectAsync(
        LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel, LiveWorldFlagsChannel flagsChannel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storyChannel);
        ArgumentNullException.ThrowIfNull(worldChannel);
        ArgumentNullException.ThrowIfNull(flagsChannel);
        var story = await storyChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        var world = await worldChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveStorySession(storyChannel, worldChannel, flagsChannel, story, world);
    }

    public bool AppliesImmediately => true;
    public bool IsHost => _world.IsHost;
    public string? Status { get; private set; }

    // ---------- story chapter / progression (read-only live) ----------

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
        var (flagsToSet, flagsToClear) = ComputeFlagPlan(row, currentlySet);

        await _storyChannel.SetAsync(row, flagsToSet, flagsToClear, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
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
        string targetRow, IReadOnlyCollection<string> currentlySet)
    {
        var targetIndex = StoryProgressionCatalog.IndexOf(targetRow);
        if (targetIndex < 0) throw new InvalidOperationException($"Unknown chapter '{targetRow}'.");

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
        var flagsToSet = triggersThroughTarget.Where(f => !haveSet.Contains(f)).ToList();

        var forwardTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = targetIndex + 1; i < StoryProgressionCatalog.Chapters.Count; i++)
        {
            if (StoryProgressionCatalog.Chapters[i].TriggerFlag is { } trigger) forwardTriggers.Add(trigger);
        }
        var toClear = new HashSet<string>(FlagGate.DependentsOf(forwardTriggers, currentlySet), StringComparer.OrdinalIgnoreCase);
        toClear.UnionWith(FlagGate.FlagsPastChapter(targetIndex, currentlySet));
        var flagsToClear = toClear.Where(haveSet.Contains).ToList();

        return (flagsToSet, flagsToClear);
    }

    public int? MinutesPassed => null;
    public bool CanSetMinutesPassed => false;

    public Task SetMinutesPassedAsync(int minutes, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Total playtime is not exposed by the live agent.");

    public string? LastPlayedText => null;

    // ---------- world clock ----------

    public double? WorldTimeSeconds => _world.TimeSeconds;
    public int? WorldDay => _world.Day;
    public bool CanSetWorldClock => _world.IsHost;

    public async Task SetWorldClockAsync(double seconds, int day, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(TimeSeconds: seconds, Day: day), cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------- weather (live only) ----------

    public bool SupportsWeather => true;
    public string? CurrentWeather => _world.CurrentWeather;
    public IReadOnlyList<string> WeatherOptions => _world.WeatherOptions;

    public async Task TriggerWeatherAsync(string weather, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(Weather: weather), cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task QueueWeatherAsync(string weather, CancellationToken cancellationToken = default)
    {
        await _worldChannel.SetAsync(new LiveWorldStateEdit(NextWeather: weather), cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------- recipes (file session only) ----------

    public bool SupportsRecipes => false;

    // ---------- whole-session save (file session only; live applies per action) ----------

    public bool IsDirty => false;
    public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Revert() { }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _story = await _storyChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        await RefreshWorldAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshWorldAsync(CancellationToken cancellationToken)
    {
        _world = await _worldChannel.GetAsync(cancellationToken).ConfigureAwait(false);
    }
}
