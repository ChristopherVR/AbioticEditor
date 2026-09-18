using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open BASES editing session (deployables, custom names, bench
/// upgrades), implemented by <see cref="WorldSaveSession"/> (staged, applied on SAVE) and
/// <see cref="LiveBasesSession"/> (immediate, against a running game). Mirrors
/// <c>IPlayerVitalsSession</c>: <see cref="WorldBasesTab"/> (renamed target:
/// <c>Components/World/WorldBasesTab.razor</c>) is rendered by both the file editor and
/// LiveConnect against this one interface, so neither host needs its own copy of the tab.
/// </summary>
public interface IWorldBasesSession
{
    /// <summary>Every deployable known to this session (staged edits included, for the file session).</summary>
    IReadOnlyList<WorldDeployable> Deployables { get; }

    /// <summary>True when a mutator here takes effect in the running game immediately (live);
    /// false when it only stages an edit applied on SAVE (file).</summary>
    bool AppliesImmediately { get; }

    /// <summary>True when this client may change deployables: always for the file session, only
    /// for the hosting player in a live session (the game refuses a client's writes).</summary>
    bool IsHost { get; }

    /// <summary>
    /// False when a live session has no confirmed way to open a bench/crate's contents inline
    /// (the file session always supports this - it shares the CONTAINERS tab's staged slot
    /// model). Live container editing has its own dedicated area/tab; wiring the two together
    /// live is out of scope here, so the tab hides the "open contents" affordance instead of
    /// guessing at a shared write path.
    /// </summary>
    bool SupportsContainerPeek { get; }

    /// <summary>Sets (or clears, with null/blank) a deployable's player-visible custom name.</summary>
    Task SetCustomNameAsync(string deployableId, string? customName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears, with null) a deployable's paint colour. <paramref name="colorValue"/> is
    /// an <c>EPaintColor</c> value from <see cref="AbioticEditor.Core.WorldSaves.DeployablePaintCatalog.Colors"/>;
    /// null resets it to unpainted. Only meaningful when the deployable's class is paintable
    /// (<see cref="AbioticEditor.Core.WorldSaves.WorldDeployable.SupportsPaint"/>).
    /// </summary>
    Task SetPaintColorAsync(string deployableId, int? colorValue, CancellationToken cancellationToken = default);

    /// <summary>True when this deployable can carry bench upgrade modules AND upgrades can
    /// actually be edited right now (see <see cref="BenchHasUpgradeSlot"/> for the weaker,
    /// class-level question). Always equal to <see cref="BenchHasUpgradeSlot"/> for the file
    /// session, which can always stage an edit; live, this also requires host authority and the
    /// connected runtime to support the direct gameplay-tag write (round 111: re-confirmed
    /// grounded, not loosened or tightened - see <c>bench_tags.lua</c>'s header comment).</summary>
    bool BenchSupportsUpgrades(string deployableId);

    /// <summary>
    /// True when this deployable's class carries upgrade slots at all, regardless of whether they
    /// can be edited right now. Lets the shared tab tell "this bench has no upgrade slots" (hide
    /// the section entirely) apart from "this bench has upgrade slots but they cannot be edited on
    /// this connection right now" (show why instead of just vanishing) - see
    /// <see cref="BenchSupportsUpgrades"/>.
    /// </summary>
    bool BenchHasUpgradeSlot(string deployableId);

    /// <summary>The upgrade rows currently installed on a bench.</summary>
    IReadOnlyList<string> BenchInstalledUpgrades(string deployableId);

    /// <summary>
    /// Installs or removes one upgrade module. Neither direction calls the bench's own native
    /// <c>AddUpgrade</c>/<c>"Has Upgrade"</c> functions live (round 79: a fabricated row-handle
    /// struct crashed the game outright) - both install and removal write the bench's own
    /// <c>BenchUpgrade.&lt;Row&gt;</c> gameplay tag directly instead, the same both-sides
    /// technique for both directions, so removal is no longer any more restricted than install -
    /// see <c>Scripts/bench_tags.lua</c>.
    /// </summary>
    Task<bool> SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken = default);
}
