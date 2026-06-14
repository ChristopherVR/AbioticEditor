using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Runs a plugin <see cref="ISaveOperation"/> against a save file end to end: load, kind
/// check, execute, then - and only if the operation marked a change and it is not a dry run
/// - back up and rewrite the file. This is the single dangerous-side path, kept in the host
/// (not in plugins) so backups and the dry-run gate are enforced uniformly for every
/// operation regardless of who wrote it. Both the CLI (<c>plugins run</c>) and the GUI use
/// this; behaviour is therefore identical across hosts.
/// </summary>
public static class SaveOperationRunner
{
    /// <summary>The result of a full run, including whether the file was actually written.</summary>
    /// <param name="Result">The operation's own reported outcome.</param>
    /// <param name="Wrote">True if the save file was backed up and rewritten.</param>
    /// <param name="Kind">The detected kind of the target save.</param>
    public sealed record RunOutcome(SaveOperationResult Result, bool Wrote, SaveKind Kind);

    /// <summary>
    /// Executes <paramref name="operation"/> on <paramref name="filePath"/>.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <param name="filePath">Absolute path to the target <c>.sav</c>.</param>
    /// <param name="parameters">Raw parameter values (defaults are applied here).</param>
    /// <param name="log">Plugin logger to pass into the context.</param>
    /// <param name="dryRun">When true the file is never written, even on a marked change.</param>
    /// <param name="cancellationToken">Cancellation for long operations.</param>
    public static async Task<RunOutcome> RunAsync(
        ISaveOperation operation,
        string filePath,
        IReadOnlyDictionary<string, string>? parameters,
        IPluginLog log,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(log);

        if (!File.Exists(filePath))
        {
            return new RunOutcome(SaveOperationResult.Failed($"save file not found: {filePath}"), false, SaveKind.Any);
        }

        var kind = SaveKindDetector.Detect(filePath);
        if (!SaveKindDetector.Matches(operation.AppliesTo, kind))
        {
            return new RunOutcome(
                SaveOperationResult.Failed(
                    $"operation '{operation.Id}' targets {operation.AppliesTo} saves but '{Path.GetFileName(filePath)}' is a {kind} save."),
                false,
                kind);
        }

        var resolved = ResolveParameters(operation, parameters, out var paramError);
        if (paramError is not null)
        {
            return new RunOutcome(SaveOperationResult.Failed(paramError), false, kind);
        }

        // Load the save (the Abiotic custom save classes must be registered for typed reads).
        AbioticSaveClasses.EnsureLoaded();
        SaveGame save;
        using (var fs = File.OpenRead(filePath))
        {
            save = SaveGame.LoadFrom(fs);
        }

        var context = new SaveOperationContext(filePath, kind, save, resolved, dryRun, log);

        SaveOperationResult result;
        try
        {
            result = await operation.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Error($"operation '{operation.Id}' threw", ex);
            return new RunOutcome(SaveOperationResult.Failed($"operation threw: {ex.Message}"), false, kind);
        }

        var shouldWrite = result.Success && context.HasChanges && !dryRun;
        if (shouldWrite)
        {
            // Backup + atomic temp-then-replace: a failure during WriteTo can never truncate the
            // live save, and a missing .bak is therefore no longer catastrophic.
            SaveBackup.WriteWithBackup(filePath, save.WriteTo);
        }

        return new RunOutcome(result, shouldWrite, kind);
    }

    /// <summary>
    /// Applies declared defaults and enforces required parameters, producing the dictionary
    /// the context exposes. Returns a non-null <paramref name="error"/> when a required
    /// parameter is missing.
    /// </summary>
    private static Dictionary<string, string> ResolveParameters(
        ISaveOperation operation,
        IReadOnlyDictionary<string, string>? supplied,
        out string? error)
    {
        error = null;
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Start from what the caller supplied (case-insensitive keys).
        if (supplied is not null)
        {
            foreach (var (k, v) in supplied)
            {
                resolved[k] = v;
            }
        }

        foreach (var declared in operation.Parameters)
        {
            if (resolved.TryGetValue(declared.Name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (declared.DefaultValue is not null)
            {
                resolved[declared.Name] = declared.DefaultValue;
            }
            else if (declared.Required)
            {
                error = $"missing required parameter '{declared.Name}' for operation '{operation.Id}'.";
                return resolved;
            }
        }

        return resolved;
    }
}
