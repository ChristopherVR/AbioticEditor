using UeSaveGame;

namespace AbioticEditor.Plugins.Saves;

/// <summary>
/// Everything a <see cref="ISaveOperation"/> needs for one run, supplied by the host. The
/// operation reads and mutates <see cref="Save"/> in place; the host decides whether to
/// persist based on <see cref="MarkChanged"/>, which keeps the dangerous part (writing
/// bytes + backups + dry-run) out of plugin hands.
/// </summary>
public interface ISaveOperationContext
{
    /// <summary>Absolute path of the save file on disk (read-only; do not write to it).</summary>
    string FilePath { get; }

    /// <summary>The detected category of <see cref="FilePath"/>.</summary>
    SaveKind Kind { get; }

    /// <summary>
    /// The loaded save model. Mutate <see cref="SaveGame.Properties"/> directly to edit the
    /// file. The host has already ensured the Abiotic save classes are registered, so typed
    /// access works. After the operation returns, the host writes this back only if
    /// <see cref="MarkChanged"/> was called.
    /// </summary>
    SaveGame Save { get; }

    /// <summary>Resolved parameter values (defaults applied) keyed by parameter name.</summary>
    IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>
    /// When true the host will not write the file no matter what the operation reports.
    /// Operations should still do their full computation (so the reported change count is
    /// accurate) but may skip expensive side effects. Set by the CLI <c>--dry-run</c> flag
    /// and the GUI preview path.
    /// </summary>
    bool IsDryRun { get; }

    /// <summary>Plugin-scoped logger (same as <c>Host.Log</c>).</summary>
    IPluginLog Log { get; }

    /// <summary>
    /// Signals that <see cref="Save"/> was modified and should be persisted. Call it only
    /// when something actually changed; an operation that finds nothing to do must leave the
    /// context unmarked so the host skips the write (and the backup) entirely.
    /// </summary>
    void MarkChanged();

    /// <summary>True once <see cref="MarkChanged"/> has been called.</summary>
    bool HasChanges { get; }

    /// <summary>Convenience reader for a parameter, returning <paramref name="fallback"/> if absent/blank.</summary>
    string GetParameter(string name, string fallback = "")
        => Parameters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
