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

    /// <summary>True when this deployable can carry bench upgrade modules.</summary>
    bool BenchSupportsUpgrades(string deployableId);

    /// <summary>The upgrade rows currently installed on a bench.</summary>
    IReadOnlyList<string> BenchInstalledUpgrades(string deployableId);

    /// <summary>
    /// Installs or removes one upgrade module. Live installs are grounded in the bench's own
    /// <c>AddUpgrade</c> function; live removal has no evidenced game-side call and throws
    /// <see cref="NotSupportedException"/> there instead of guessing at a raw tag-container edit.
    /// </summary>
    Task<bool> SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken = default);
}
