namespace AbioticEditor.Plugins.Cli;

/// <summary>
/// A command-line subcommand contributed by a plugin. Declared in framework-neutral terms
/// (no System.CommandLine dependency, so the SDK stays host-agnostic); the CLI host adapts
/// it to a real subcommand and the GUI ignores it. This is the "add a new scripting/CLI
/// feature" extension point - whole new verbs the base tool never shipped.
/// </summary>
public interface IConsoleCommand
{
    /// <summary>
    /// The verb as typed, e.g. <c>backup-prune</c>. Must not collide with a built-in
    /// command; the host skips and warns on collision rather than overriding a built-in.
    /// </summary>
    string Name { get; }

    /// <summary>One-line help shown in <c>--help</c>.</summary>
    string Description { get; }

    /// <summary>Positional arguments, in order. Empty if the command takes none.</summary>
    IReadOnlyList<PluginCommandArgument> Arguments => Array.Empty<PluginCommandArgument>();

    /// <summary>Named options (<c>--name value</c> or, for flags, bare <c>--name</c>).</summary>
    IReadOnlyList<PluginCommandOption> Options => Array.Empty<PluginCommandOption>();

    /// <summary>
    /// Runs the command. Write user output to <see cref="IConsoleCommandContext.Out"/> /
    /// <c>Error</c> and return a process exit code (0 = success, 1 = user error, 2 =
    /// unexpected - matching the host's convention).
    /// </summary>
    Task<int> InvokeAsync(IConsoleCommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>A positional argument declaration.</summary>
/// <param name="Name">Display name in help and the key in <see cref="IConsoleCommandContext.Arguments"/>.</param>
/// <param name="Description">Help text.</param>
/// <param name="Required">If true the host rejects invocations that omit it.</param>
public sealed record PluginCommandArgument(string Name, string Description, bool Required = true);

/// <summary>A named option declaration.</summary>
/// <param name="Name">Long name without dashes, e.g. <c>output</c> for <c>--output</c>.</param>
/// <param name="Description">Help text.</param>
/// <param name="IsFlag">True for a boolean switch (presence = true, no value taken).</param>
/// <param name="Required">If true the host rejects invocations that omit it.</param>
/// <param name="DefaultValue">Value used when omitted (non-flag options only).</param>
public sealed record PluginCommandOption(
    string Name,
    string Description,
    bool IsFlag = false,
    bool Required = false,
    string? DefaultValue = null);
