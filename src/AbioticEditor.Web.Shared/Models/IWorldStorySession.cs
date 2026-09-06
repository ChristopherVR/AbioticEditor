namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for the shared story-progression / world-clock-and-weather tab
/// (<c>WorldStoryTab</c>), implemented by <see cref="WorldSaveSession"/> (staged, written on
/// SAVE) and <see cref="LiveStorySession"/> (clock and weather apply immediately through
/// <c>world.get</c>/<c>world.set</c>; the story chapter itself is read-only live - see
/// <see cref="CanSetStoryChapter"/>). Same pattern as <see cref="IPlayerVitalsSession"/> and
/// <see cref="IWorldFlagsSession"/>: one shared component renders both hosts.
/// </summary>
public interface IWorldStorySession
{
    /// <summary>True only for the live session: clock/weather changes take effect in the running
    /// game immediately instead of staging for SAVE, so the tab hides its SAVE/REVERT bar.</summary>
    bool AppliesImmediately { get; }

    /// <summary>Whether this session is currently allowed to change the clock/weather (always
    /// true for the file session; the live session needs host authority).</summary>
    bool IsHost { get; }

    string? Status { get; }

    // ---------- story chapter / progression ----------

    /// <summary>Whether this session has story data to show at all (a metadata save offline; the
    /// live session once a world is loaded).</summary>
    bool CanShowStory { get; }

    /// <summary>The current story-progression row (<c>StoryProgressionCatalog.Chapters[].Row</c>)
    /// if it matches a known chapter; a value the catalog does not recognise renders as "unknown
    /// chapter" the same way the file editor already handles an unfamiliar row.</summary>
    string? StoryProgressionRow { get; }

    /// <summary>Whether <see cref="SetStoryChapterAsync"/> is meaningful right now. False for the
    /// live session: no grounded write path moves the story chapter directly (see
    /// docs/reference/live-editing-protocol.md, "story.get / story.set"); the flags tab remains
    /// the live way to earn a chapter's trigger flags.</summary>
    bool CanSetStoryChapter { get; }

    Task SetStoryChapterAsync(string row, CancellationToken cancellationToken = default);

    /// <summary>Total playtime in minutes; null where the concept doesn't exist (live has no
    /// equivalent counter exposed by the agent).</summary>
    int? MinutesPassed { get; }
    bool CanSetMinutesPassed { get; }
    Task SetMinutesPassedAsync(int minutes, CancellationToken cancellationToken = default);

    /// <summary>Human-readable last-played timestamp; null when not applicable (always null live).</summary>
    string? LastPlayedText { get; }

    // ---------- world clock ----------

    double? WorldTimeSeconds { get; }
    int? WorldDay { get; }
    bool CanSetWorldClock { get; }
    Task SetWorldClockAsync(double seconds, int day, CancellationToken cancellationToken = default);

    // ---------- weather (live only; not part of the save file) ----------

    /// <summary>True only for the live session - weather is not stored in any save, so the file
    /// session never has anything to show here.</summary>
    bool SupportsWeather { get; }
    string? CurrentWeather { get; }
    IReadOnlyList<string> WeatherOptions { get; }
    Task TriggerWeatherAsync(string weather, CancellationToken cancellationToken = default);
    Task QueueWeatherAsync(string weather, CancellationToken cancellationToken = default);

    // ---------- world recipes ----------

    /// <summary>Whether the world-recipes browser has anything to show at all: true for the file
    /// session when its save carries an editable <c>GlobalUnlocks</c> list, true live once a world
    /// is connected (see <see cref="GlobalRecipeIds"/>/<see cref="CanEditGlobalRecipes"/>).</summary>
    bool SupportsRecipes { get; }

    /// <summary>Every recipe row id currently unlocked world-wide: the file session's own staged
    /// <c>GlobalUnlocks</c> list, or, live, the running game's replicated
    /// <c>GlobalRecipesUnlocked</c> set (read via <c>worldunlocks.get</c>).</summary>
    IReadOnlyCollection<string> GlobalRecipeIds { get; }

    /// <summary>Whether <see cref="GlobalRecipeIds"/> can be changed from here. Always false live:
    /// the running game exposes no function and no confirmed direct-write technique for
    /// <c>GlobalRecipesUnlocked</c> (a replicated <c>TSet</c>) - see
    /// docs/reference/live-editing-protocol.md, "worldunlocks.get / worldunlocks.set". True for the
    /// file session whenever <see cref="SupportsRecipes"/> is.</summary>
    bool CanEditGlobalRecipes { get; }

    // ---------- whole-session save (file session only; live applies per action) ----------

    bool IsDirty { get; }
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();

    /// <summary>Re-reads from the source of truth. A no-op offline; a fresh read of the running
    /// game's clock/weather/quest live.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
