using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live story/world-clock/weather session: implements the same <see cref="IWorldStorySession"/>
/// boundary the shared <c>WorldStoryTab</c> widget uses for the file editor. Wraps two channels -
/// <see cref="LiveStoryChannel"/> for the read-only current-quest indicator and
/// <see cref="LiveWorldStateChannel"/> (folded in here rather than kept as its own tab/session,
/// per the "one shared component" goal) for the clock and weather, which apply immediately.
/// There is no grounded live write path for the story chapter itself - see
/// <c>areas/story.lua</c> - so <see cref="CanSetStoryChapter"/> is always false here and the tab
/// hides the SET controls instead of offering something that cannot work.
/// </summary>
public sealed class LiveStorySession : IWorldStorySession
{
    private readonly LiveStoryChannel _storyChannel;
    private readonly LiveWorldStateChannel _worldChannel;
    private LiveStoryState _story;
    private LiveWorldState _world;

    private LiveStorySession(LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel,
        LiveStoryState story, LiveWorldState world)
    {
        _storyChannel = storyChannel;
        _worldChannel = worldChannel;
        _story = story;
        _world = world;
    }

    public static async Task<LiveStorySession> ConnectAsync(
        LiveStoryChannel storyChannel, LiveWorldStateChannel worldChannel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storyChannel);
        ArgumentNullException.ThrowIfNull(worldChannel);
        var story = await storyChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        var world = await worldChannel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveStorySession(storyChannel, worldChannel, story, world);
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

    public bool CanSetStoryChapter => false;

    public Task SetStoryChapterAsync(string row, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "The story chapter cannot be set from outside the game; set its trigger flags on the quest flags tab instead.");

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
