namespace AbioticEditor.Plugins.Saves;

/// <summary>
/// The forward-compatibility extension point: a plugin that up-converts a save the host
/// cannot otherwise read. When a new game version ships and <c>SaveGame.LoadFrom</c> throws
/// (unsupported version) - or a save is flagged as a newer-than-known version - the host
/// probes its registered upgraders and hands the raw bytes to the first one that claims it.
///
/// <para>
/// This is the difference between "the editor breaks on a game update" and "drop in a small
/// plugin and keep working". Unlike <see cref="ISaveOperation"/> (which edits an
/// already-loaded save), an upgrader works on the raw file bytes precisely because loading
/// may have failed - it returns corrected bytes the host then loads normally.
/// </para>
/// </summary>
public interface ISaveUpgrader
{
    /// <summary>Stable, unique-within-the-plugin id (kebab-case).</summary>
    string Id { get; }

    /// <summary>Short human label shown in logs and the GUI.</summary>
    string DisplayName { get; }

    /// <summary>One or two sentences describing which saves it upgrades and how.</summary>
    string Description => string.Empty;

    /// <summary>
    /// Cheap, header-only claim check: returns true if this upgrader believes it can handle
    /// the probed save. Must not be expensive - it is called for every registered upgrader in
    /// turn. Inspect <paramref name="probe"/>'s version fields; reach into the bytes only in
    /// <see cref="UpgradeAsync"/>.
    /// </summary>
    bool CanUpgrade(SaveUpgradeProbe probe);

    /// <summary>
    /// Attempts the upgrade. Read <see cref="ISaveUpgradeContext.OriginalBytes"/>, produce
    /// corrected save bytes, and return them via <see cref="SaveUpgradeResult.Ok"/>. The host
    /// then loads those bytes and (optionally, with the user's consent) persists them after a
    /// <c>.preupgrade.bak</c>. Return <see cref="SaveUpgradeResult.NotHandled"/> to decline.
    /// </summary>
    Task<SaveUpgradeResult> UpgradeAsync(ISaveUpgradeContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Header-only facts about a save, read by the host without a full parse (so it works even
/// when the save can't be loaded). Handed to <see cref="ISaveUpgrader.CanUpgrade"/>.
/// </summary>
/// <param name="FilePath">Absolute path of the save on disk.</param>
/// <param name="FileLength">Size of the file in bytes.</param>
/// <param name="SaveClass">The GVAS save-class string, or null if it couldn't be read.</param>
/// <param name="SaveGameVersion">The save-game format version (the field that gates LoadFrom).</param>
/// <param name="PackageVersionUE4">UE4 object/package serialization version.</param>
/// <param name="PackageVersionUE5">UE5 object/package serialization version (0 when absent).</param>
/// <param name="AbfVersion">The ABF_SAVE_VERSION from the custom header, or null if not present.</param>
/// <param name="LoadError">The message LoadFrom failed with, or null if the save loaded fine.</param>
public sealed record SaveUpgradeProbe(
    string FilePath,
    long FileLength,
    string? SaveClass,
    int SaveGameVersion,
    uint PackageVersionUE4,
    uint PackageVersionUE5,
    int? AbfVersion,
    string? LoadError);

/// <summary>Everything an <see cref="ISaveUpgrader"/> gets for one upgrade attempt.</summary>
public interface ISaveUpgradeContext
{
    /// <summary>Absolute path of the save being upgraded (read-only; the host owns writing).</summary>
    string FilePath { get; }

    /// <summary>The original on-disk bytes (the host already read them).</summary>
    byte[] OriginalBytes { get; }

    /// <summary>The header facts the host probed.</summary>
    SaveUpgradeProbe Probe { get; }

    /// <summary>Plugin-scoped logger.</summary>
    IPluginLog Log { get; }
}

/// <summary>The outcome of an <see cref="ISaveUpgrader.UpgradeAsync"/> attempt.</summary>
/// <param name="Handled">True if this upgrader produced corrected bytes.</param>
/// <param name="Message">One-line summary for the user/log.</param>
/// <param name="UpgradedBytes">The corrected save bytes (null when not handled).</param>
public sealed record SaveUpgradeResult(bool Handled, string Message, byte[]? UpgradedBytes = null)
{
    /// <summary>The upgrade succeeded; <paramref name="upgradedBytes"/> is a loadable save.</summary>
    public static SaveUpgradeResult Ok(byte[] upgradedBytes, string message)
        => new(true, message, upgradedBytes);

    /// <summary>This upgrader can't handle the save after all (host tries the next one).</summary>
    public static SaveUpgradeResult NotHandled(string message)
        => new(false, message, null);
}
