using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

using UeSaveGame;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Loads a save, and when the normal parse fails (a newer/unknown game version), gives
/// registered <see cref="ISaveUpgrader"/> plugins a chance to up-convert the raw bytes before
/// giving up. This is the forward-compatibility path: a game update that the shipped editor
/// can't read can be made readable by dropping in an upgrader plugin, no app rebuild needed.
///
/// <para>
/// The host owns the dangerous part (deciding whether to persist the upgraded bytes), exactly
/// like <see cref="SaveOperationRunner"/> - an upgrader only ever returns bytes.
/// </para>
/// </summary>
public static class SaveUpgradeService
{
    private const uint Magic = 0x53415647; // "GVAS"

    /// <summary>The outcome of an upgrade-aware load.</summary>
    /// <param name="Save">The loaded (possibly upgraded) save.</param>
    /// <param name="WasUpgraded">True if an upgrader produced the bytes that loaded.</param>
    /// <param name="UpgraderId">Id of the upgrader that handled it, or null.</param>
    /// <param name="Message">The upgrader's message, or null.</param>
    /// <param name="Persisted">True if upgraded bytes were written back to disk.</param>
    public sealed record LoadResult(
        SaveGame Save, bool WasUpgraded, string? UpgraderId, string? Message, bool Persisted);

    /// <summary>
    /// Loads <paramref name="filePath"/>, attempting registered upgraders if the normal parse
    /// throws. When <paramref name="persist"/> is true and an upgrade succeeds, the corrected
    /// bytes are written back after a <c>.preupgrade.bak</c> of the original.
    /// </summary>
    /// <exception cref="Exception">
    /// Rethrows the original load failure when no upgrader can recover the save.
    /// </exception>
    public static async Task<LoadResult> LoadAsync(
        string filePath,
        IReadOnlyList<ISaveUpgrader> upgraders,
        IPluginLog log,
        bool persist = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(upgraders);
        ArgumentNullException.ThrowIfNull(log);

        AbioticSaveClasses.EnsureLoaded();
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Happy path: the save loads as-is.
        if (TryLoad(bytes, out var save, out _))
        {
            return new LoadResult(save!, false, null, null, false);
        }

        // The parse failed - this is where an upgrader earns its keep.
        TryLoad(bytes, out _, out var loadError);
        var probe = BuildProbe(filePath, bytes, loadError);

        foreach (var upgrader in upgraders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SafeCanUpgrade(upgrader, probe, log))
            {
                continue;
            }

            var context = new SaveUpgradeContext(filePath, bytes, probe, log);
            SaveUpgradeResult result;
            try
            {
                result = await upgrader.UpgradeAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"upgrader '{upgrader.Id}' threw", ex);
                continue;
            }

            if (!result.Handled || result.UpgradedBytes is null)
            {
                continue;
            }

            if (!TryLoad(result.UpgradedBytes, out var upgraded, out var stillBroken))
            {
                log.Warn($"upgrader '{upgrader.Id}' produced bytes that still don't load: {stillBroken}");
                continue;
            }

            var persisted = false;
            if (persist)
            {
                // Keep the pre-upgrade original under a distinct suffix (separate from the
                // editor's normal .bak) so a bad upgrade is always recoverable.
                File.Copy(filePath, filePath + ".preupgrade.bak", overwrite: true);
                await File.WriteAllBytesAsync(filePath, result.UpgradedBytes, cancellationToken).ConfigureAwait(false);
                persisted = true;
            }

            EditorLog.Info("Plugins", $"Upgrader '{upgrader.Id}' recovered {Path.GetFileName(filePath)}: {result.Message}");
            return new LoadResult(upgraded!, true, upgrader.Id, result.Message, persisted);
        }

        // No upgrader could help: surface the real reason the save wouldn't load.
        throw new InvalidDataException(
            $"could not load '{Path.GetFileName(filePath)}' and no plugin upgrader handled it: {loadError}");
    }

    private static bool TryLoad(byte[] bytes, out SaveGame? save, out string? error)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            save = SaveGame.LoadFrom(ms);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or InvalidDataException or EndOfStreamException or NotImplementedException)
        {
            save = null;
            error = ex.Message;
            return false;
        }
    }

    private static bool SafeCanUpgrade(ISaveUpgrader upgrader, SaveUpgradeProbe probe, IPluginLog log)
    {
        try
        {
            return upgrader.CanUpgrade(probe);
        }
        catch (Exception ex)
        {
            log.Error($"upgrader '{upgrader.Id}' CanUpgrade threw", ex);
            return false;
        }
    }

    /// <summary>Reads header facts from the bytes (no full parse) so an upgrader can claim the save.</summary>
    private static SaveUpgradeProbe BuildProbe(string filePath, byte[] bytes, string? loadError)
    {
        int saveGameVersion = 0;
        uint ue4 = 0, ue5 = 0;
        if (bytes.Length >= 12 && BitConverter.ToUInt32(bytes, 0) == Magic)
        {
            saveGameVersion = BitConverter.ToInt32(bytes, 4);
            ue4 = BitConverter.ToUInt32(bytes, 8);
            if (saveGameVersion >= 3 && bytes.Length >= 16)
            {
                ue5 = BitConverter.ToUInt32(bytes, 12);
            }
        }

        // Save class + ABF version are a deeper header read; best-effort (it may itself fail
        // on the very header that broke the load).
        string? saveClass = null;
        int? abfVersion = null;
        try
        {
            (saveClass, abfVersion) = SaveFolderScanner.ReadHeaderInfo(filePath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or NotSupportedException)
        {
            // Header unreadable; the version fields above are all an upgrader gets.
        }

        return new SaveUpgradeProbe(
            filePath, bytes.LongLength, saveClass, saveGameVersion, ue4, ue5, abfVersion, loadError);
    }
}

/// <summary>Host-side <see cref="ISaveUpgradeContext"/> for one upgrade attempt.</summary>
internal sealed class SaveUpgradeContext : ISaveUpgradeContext
{
    public SaveUpgradeContext(string filePath, byte[] originalBytes, SaveUpgradeProbe probe, IPluginLog log)
    {
        FilePath = filePath;
        OriginalBytes = originalBytes;
        Probe = probe;
        Log = log;
    }

    public string FilePath { get; }

    public byte[] OriginalBytes { get; }

    public SaveUpgradeProbe Probe { get; }

    public IPluginLog Log { get; }
}
