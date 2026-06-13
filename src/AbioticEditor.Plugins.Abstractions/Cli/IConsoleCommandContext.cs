namespace AbioticEditor.Plugins.Cli;

/// <summary>
/// The parsed invocation handed to <see cref="IConsoleCommand.InvokeAsync"/>: resolved
/// argument and option values plus output writers. Abstracting stdout/stderr behind
/// <see cref="TextWriter"/> keeps commands testable (a test passes a StringWriter) and lets
/// the host redirect or decorate output.
/// </summary>
public interface IConsoleCommandContext
{
    /// <summary>
    /// Positional argument values keyed by the declared <see cref="PluginCommandArgument.Name"/>.
    /// Missing optional arguments are absent from the dictionary.
    /// </summary>
    IReadOnlyDictionary<string, string> Arguments { get; }

    /// <summary>
    /// Option values keyed by <see cref="PluginCommandOption.Name"/>. Flags are present with
    /// value <c>"true"</c> when set; non-flag options carry their string value (or default).
    /// </summary>
    IReadOnlyDictionary<string, string?> Options { get; }

    /// <summary>Standard output writer for command results.</summary>
    TextWriter Out { get; }

    /// <summary>Standard error writer for warnings and errors.</summary>
    TextWriter Error { get; }

    /// <summary>Host services (logging, data directory, versions).</summary>
    IPluginHost Host { get; }

    /// <summary>Reads a flag option, defaulting to false when unset.</summary>
    bool GetFlag(string name)
        => Options.TryGetValue(name, out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads an option value, returning <paramref name="fallback"/> when unset/blank.</summary>
    string GetOption(string name, string fallback = "")
        => Options.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;

    /// <summary>Reads a required positional argument, throwing a clear message if missing.</summary>
    string RequireArgument(string name)
        => Arguments.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"missing required argument '{name}'.");
}
