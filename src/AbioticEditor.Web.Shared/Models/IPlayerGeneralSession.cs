namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-general editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>PlayerGeneralTab.razor</c> uses, extracted from
/// <see cref="PlayerSaveSession"/>'s existing account/bulk-unlock slice, so that widget binds to
/// either the file-backed session or <c>LivePlayerGeneralSession</c> with no changes beyond its
/// parameter's declared type. Recipe count/unlock-all is deliberately NOT part of this interface:
/// the tab takes an <see cref="IPlayerRecipesSession"/> separately and reuses it, the same object
/// the RECIPES tab itself edits, rather than duplicating recipe-unlock logic here.
/// </summary>
public interface IPlayerGeneralSession
{
    /// <summary>The save's own account/owner id (a SteamID64, a Game Pass XUID, or a hand-made
    /// id). Always readable, even live, so the readout still shows who this is.</summary>
    string? OwnerId { get; }

    bool IsSteamOwnerId { get; }

    /// <summary>False live: renaming which save file a character belongs to is purely a
    /// file-system operation with no live-game equivalent. The tab hides the CHANGE button (and
    /// says so) rather than offering an action that can never do anything live.</summary>
    bool CanChangeOwnerId { get; }

    IPlayerDiscoverySection ItemsSeen { get; }

    /// <summary>Read-only live: the running game tracks crafted items automatically but exposes
    /// no function to mark one crafted on demand (see <c>LivePlayerGeneralChannel</c>'s
    /// remarks) - <see cref="IPlayerDiscoverySection.CanDiscoverAll"/> is false here live.</summary>
    IPlayerDiscoverySection ItemsCrafted { get; }

    IPlayerDiscoverySection Maps { get; }

    /// <summary>The chosen background/PhD row name (e.g. <c>PhD_HumanBio</c>), or null/empty when
    /// none is set. Always readable.</summary>
    string? Background { get; }

    /// <summary>True when <see cref="SetBackgroundAsync"/> can actually change something right
    /// now. Always true for the file session (a staged edit); true live once a connected player
    /// is resolved - a grounded write path exists (see <c>LivePlayerGeneralChannel</c>'s remarks:
    /// <c>Abiotic_PlayerState_C.PhD</c> is a direct, no-<c>OnRep</c> property write).</summary>
    bool CanChangeBackground { get; }

    /// <summary>Applies a new background/PhD row name. The file session only stages the change;
    /// the live session writes it to the running character immediately.</summary>
    Task SetBackgroundAsync(string? background);

    /// <summary>The chosen trait row names. Read-only through this interface even for the file
    /// session (full add/remove lives on the CHARACTER tab, <c>PlayerCharacterTab.razor</c>,
    /// which binds to <c>PlayerSaveSession.Traits</c> directly and is not available live) - this
    /// is just a readout so a live session, which has no such tab, can still show what a
    /// character actually has (see <c>LivePlayerGeneralChannel</c>'s remarks for why there is no
    /// live write path for a single trait).</summary>
    IReadOnlyList<string> Traits { get; }
}

/// <summary>One bulk-discovery row (items seen, items crafted, maps): how many are already known
/// and whether "discover all" can act on more of them right now.</summary>
public interface IPlayerDiscoverySection
{
    /// <summary>The ids already known. A collection (not just a count) so the tab can union it
    /// against the installed game's own vocabulary without double-counting overlap, the same way
    /// the file editor's recipe/map "X of Y" readouts already do.</summary>
    IReadOnlyCollection<string> Known { get; }
    bool CanDiscoverAll { get; }
    Task DiscoverAllAsync(IEnumerable<string> vocabulary);
}

/// <summary>Trivial <see cref="IPlayerDiscoverySection"/> built from plain delegates, so
/// <see cref="PlayerSaveSession"/> and <c>LivePlayerGeneralSession</c> can each wire up their own
/// known-set/discover behaviour without three near-identical little classes apiece.</summary>
internal sealed class DelegateDiscoverySection(
    Func<IReadOnlyCollection<string>> known, bool canDiscoverAll, Func<IEnumerable<string>, Task> discoverAll)
    : IPlayerDiscoverySection
{
    public IReadOnlyCollection<string> Known => known();
    public bool CanDiscoverAll => canDiscoverAll;
    public Task DiscoverAllAsync(IEnumerable<string> vocabulary) => discoverAll(vocabulary);
}
