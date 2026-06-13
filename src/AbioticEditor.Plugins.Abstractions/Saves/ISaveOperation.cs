namespace AbioticEditor.Plugins.Saves;

/// <summary>
/// A read/modify operation over a single save file - the workhorse capability that covers
/// both "scripted edits" (max skills, refill needs, grant recipes) and "fixes" (repair a
/// corrupted field a game patch introduced). The host loads the save, hands it to
/// <see cref="ExecuteAsync"/> via an <see cref="ISaveOperationContext"/>, and - only if the
/// operation marks the context changed - backs up and rewrites the file.
///
/// <para>
/// The same operation instance is reused by every host: the CLI invokes it through
/// <c>plugins run &lt;id&gt;</c>, the GUI lists it in the Plugins panel and runs it against
/// the currently open save. Keep implementations pure (no static state) so concurrent or
/// repeated runs are safe.
/// </para>
/// </summary>
public interface ISaveOperation
{
    /// <summary>
    /// Stable, unique-within-the-plugin id used on the command line and as a dictionary
    /// key. Lower-kebab-case by convention, e.g. <c>max-skills</c>.
    /// </summary>
    string Id { get; }

    /// <summary>Short human label shown in the GUI list and CLI help.</summary>
    string DisplayName { get; }

    /// <summary>One or two sentences describing what the operation does and any caveats.</summary>
    string Description { get; }

    /// <summary>
    /// Which save category this applies to. The host filters the operation out for
    /// non-matching saves (and the CLI refuses to run it on the wrong kind) so a player-only
    /// edit can never be aimed at a world save.
    /// </summary>
    SaveKind AppliesTo { get; }

    /// <summary>
    /// Optional named inputs the operation understands (e.g. <c>level=10</c>). The CLI
    /// exposes each as <c>--param name=value</c> and the GUI can prompt for them. Return an
    /// empty list if the operation takes none.
    /// </summary>
    IReadOnlyList<SaveOperationParameter> Parameters => Array.Empty<SaveOperationParameter>();

    /// <summary>
    /// Runs the operation. Inspect and mutate <see cref="ISaveOperationContext.Save"/>, call
    /// <see cref="ISaveOperationContext.MarkChanged"/> if (and only if) you altered it, and
    /// return a result describing the outcome. Must not write to disk itself - persistence,
    /// backup, and the dry-run gate are the host's job.
    /// </summary>
    Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Declares one named input a <see cref="ISaveOperation"/> accepts.</summary>
/// <param name="Name">Parameter key (kebab-case), surfaced as <c>--param Name=...</c>.</param>
/// <param name="Description">Help text shown by the CLI and GUI.</param>
/// <param name="Required">If true, the host refuses to run the operation without it.</param>
/// <param name="DefaultValue">Value used when the parameter is omitted (ignored if required).</param>
public sealed record SaveOperationParameter(
    string Name,
    string Description,
    bool Required = false,
    string? DefaultValue = null);
