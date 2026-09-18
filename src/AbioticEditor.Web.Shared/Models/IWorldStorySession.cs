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

    /// <summary>Whether <see cref="SetStoryChapterAsync"/> is meaningful right now: always true
    /// for a metadata save offline, host authority only live (moving the story means setting or
    /// clearing world flags in the running game - see docs/reference/live-editing-protocol.md,
    /// "story.get / story.set" - the same authority every other live world write needs).</summary>
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

    /// <summary>Whether world recipes can be changed. Live requires host authority,
    /// an updated agent, and a UE4SS runtime with TSet editing support.</summary>
    bool CanEditGlobalRecipes { get; }

    /// <summary>Short reason <see cref="CanEditGlobalRecipes"/> is false, when known (null offline,
    /// where it is always true, and null live once the game reports edits are supported). Live
    /// values include "not-host", "no-replication", and "runtime-unsupported" (an older UE4SS
    /// build lacking TSet editing support - see <c>areas/worldunlocks.lua</c>'s header comment).
    /// The default implementation covers the file session, which never has a reason to show.</summary>
    string? GlobalRecipeEditsUnavailableReason => null;

    Task SetGlobalRecipesAsync(IEnumerable<string> ids, bool unlocked, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This session cannot edit global recipes.");

    // ---------- world-wide seen/read/found lists (round 112) ----------
    //
    // The round-107 live backend (LiveWorldUnlocksChannel.SetGlobalListAsync, LiveStorySession's
    // matching members) predates this: it was built outside this interface because the file
    // session had no browser for these six lists to mirror. It does now (WorldStoryTab's
    // "WORLD-WIDE SEEN" section, next to the recipes browser above), so the members move onto the
    // shared boundary the same way the recipe members already sit here.

    /// <summary>Whether the world-wide seen/read/found browser has anything to show at all: true
    /// for the file session once its save carries an editable <c>GlobalUnlocks</c> struct (see
    /// <see cref="CanEditGlobalLists"/>'s remarks), true live once a world is connected and the
    /// agent reports the six lists - an older agent build without them leaves this false, hiding
    /// the section entirely rather than showing empty lists.</summary>
    bool SupportsGlobalLists { get; }

    /// <summary>Every catalog item id picked up world-wide at least once (the save's
    /// <c>GlobalItemsPickedUp_</c> array).</summary>
    IReadOnlyCollection<string> GlobalItemsPickedUpIds { get; }

    /// <summary>Every email id read world-wide (<c>GlobalEmailsRead_</c>).</summary>
    IReadOnlyCollection<string> GlobalEmailsReadIds { get; }

    /// <summary>Every journal entry id found world-wide (<c>GlobalJournalEntries_</c>).</summary>
    IReadOnlyCollection<string> GlobalJournalEntryIds { get; }

    /// <summary>Every compendium entry id unlocked world-wide through its email section
    /// (<c>GlobalCompendiumEmail_</c>).</summary>
    IReadOnlyCollection<string> GlobalCompendiumEmailIds { get; }

    /// <summary>Every compendium entry id unlocked world-wide through its narrative section
    /// (<c>GlobalCompendiumNarrative_</c>).</summary>
    IReadOnlyCollection<string> GlobalCompendiumNarrativeIds { get; }

    /// <summary>Every compendium entry id unlocked world-wide through its exploration section
    /// (<c>GlobalCompendiumExploration_</c>).</summary>
    IReadOnlyCollection<string> GlobalCompendiumExplorationIds { get; }

    /// <summary>Whether the six lists above can be changed. The file session allows it once its
    /// save carries a <c>GlobalUnlocks</c> struct (the same limitation
    /// <see cref="CanEditGlobalRecipes"/> already has - a save that has never recorded any
    /// world-wide unlock has nothing to add to yet); live requires host authority and
    /// replication-notification support, but no TSet capability (these are plain arrays, unlike
    /// the recipe sets).</summary>
    bool CanEditGlobalLists { get; }

    /// <summary>Short reason <see cref="CanEditGlobalLists"/> is false, when known (null offline,
    /// and null live once the game reports edits are supported). Live values are "not-host" or
    /// "no-replication" - never "runtime-unsupported", since plain array assignment needs no TSet
    /// support. The default implementation covers the file session, which never has a reason to
    /// show.</summary>
    string? GlobalListEditsUnavailableReason => null;

    /// <summary>Adds/removes rows in one of the six lists above. <paramref name="list"/> is the
    /// wire field name (<c>"itemsPickedUp"</c>, <c>"emailsRead"</c>, <c>"journalEntries"</c>,
    /// <c>"compendiumEmail"</c>, <c>"compendiumNarrative"</c>, or
    /// <c>"compendiumExploration"</c>) - shared between both session kinds so the tab needs no
    /// per-kind branching.</summary>
    Task SetGlobalListAsync(string list, IEnumerable<string> ids, bool present, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This session cannot edit world-wide lists.");

    // ---------- whole-session save (file session only; live applies per action) ----------

    bool IsDirty { get; }
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();

    /// <summary>Re-reads from the source of truth. A no-op offline; a fresh read of the running
    /// game's clock/weather/quest live.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
