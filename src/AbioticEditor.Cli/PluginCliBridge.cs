using System.CommandLine;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;

namespace AbioticEditor.Cli;

/// <summary>
/// Adapts a plugin's framework-neutral <see cref="IConsoleCommand"/> into a real
/// System.CommandLine <see cref="Command"/>. The SDK stays free of any CLI-library
/// dependency; this assembly - the only one that knows about System.CommandLine - does the
/// translation. Arguments and options declared by the plugin become first-class CLI tokens
/// with help, so a plugin command is indistinguishable from a built-in one at the prompt.
/// </summary>
internal static class PluginCliBridge
{
    /// <summary>Builds a CLI command that invokes <paramref name="command"/> from a plugin.</summary>
    public static Command Adapt(IConsoleCommand command, IPluginHost host)
    {
        var cmd = new Command(command.Name, command.Description);

        var argEntries = new List<(string Name, Argument<string?> Arg)>();
        foreach (var declared in command.Arguments)
        {
            var arg = new Argument<string?>(declared.Name)
            {
                Description = declared.Description,
                Arity = declared.Required ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne,
            };
            cmd.Arguments.Add(arg);
            argEntries.Add((declared.Name, arg));
        }

        var flagEntries = new List<(string Name, Option<bool> Opt)>();
        var valueEntries = new List<(string Name, Option<string?> Opt)>();
        foreach (var declared in command.Options)
        {
            if (declared.IsFlag)
            {
                var opt = new Option<bool>($"--{declared.Name}") { Description = declared.Description };
                cmd.Options.Add(opt);
                flagEntries.Add((declared.Name, opt));
            }
            else
            {
                var opt = new Option<string?>($"--{declared.Name}")
                {
                    Description = declared.Description,
                    Required = declared.Required,
                    DefaultValueFactory = declared.DefaultValue is null ? null : _ => declared.DefaultValue,
                };
                cmd.Options.Add(opt);
                valueEntries.Add((declared.Name, opt));
            }
        }

        cmd.SetAction((parseResult, ct) =>
        {
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, arg) in argEntries)
            {
                var value = parseResult.GetValue(arg);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    arguments[name] = value;
                }
            }

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, opt) in flagEntries)
            {
                options[name] = parseResult.GetValue(opt) ? "true" : "false";
            }
            foreach (var (name, opt) in valueEntries)
            {
                options[name] = parseResult.GetValue(opt);
            }

            var context = new PluginConsoleCommandContext(arguments, options, host);
            return InvokeAsync(command, context, ct);
        });

        return cmd;
    }

    /// <summary>
    /// Runs the plugin command under the CLI's exit-code contract (1 = handled user error,
    /// 2 = unexpected) so plugin commands behave like built-ins for scripts.
    /// </summary>
    private static async Task<int> InvokeAsync(IConsoleCommand command, PluginConsoleCommandContext context, CancellationToken ct)
    {
        try
        {
            return await command.InvokeAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("error: cancelled.");
            return Cli.UserError;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Cli.UserError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unexpected error in plugin command '{command.Name}': {ex.Message}");
            return Cli.UnexpectedError;
        }
    }
}

/// <summary>CLI-side <see cref="IConsoleCommandContext"/> backed by the parsed invocation.</summary>
internal sealed class PluginConsoleCommandContext : IConsoleCommandContext
{
    public PluginConsoleCommandContext(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string?> options,
        IPluginHost host)
    {
        Arguments = arguments;
        Options = options;
        Host = host;
    }

    public IReadOnlyDictionary<string, string> Arguments { get; }

    public IReadOnlyDictionary<string, string?> Options { get; }

    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public IPluginHost Host { get; }
}
