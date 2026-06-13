using System.CommandLine;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Events;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>plugins list/info/run</c> - inspect installed plugins and run their save operations.
/// The plugins' own console commands are registered separately, at the root, by
/// <see cref="CommandTree"/>; this group is the built-in management surface.
/// </summary>
internal static class PluginsCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("plugins", "List installed plugins and run their save operations.");
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildInfo());
        cmd.Subcommands.Add(BuildRun(quiet));
        return cmd;
    }

    private static Command BuildList()
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit a JSON array instead of the table." };
        var cmd = new Command("list", "List every discovered plugin and its load status.");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parseResult => Cli.Run(() => List(parseResult.GetValue(jsonOpt))));
        return cmd;
    }

    private static Command BuildInfo()
    {
        var idArg = new Argument<string>("id") { Description = "Plugin id (see 'plugins list')." };
        var cmd = new Command("info", "Show a plugin's metadata and the capabilities it registered.");
        cmd.Arguments.Add(idArg);
        cmd.SetAction(parseResult => Cli.Run(() => Info(parseResult.GetValue(idArg))));
        return cmd;
    }

    private static Command BuildRun(Option<bool> quiet)
    {
        var opArg = new Argument<string>("operation") { Description = "Save-operation id (see 'plugins info' or 'plugins list')." };
        var saveArg = new Argument<string>("save") { Description = "Path to the .sav file to operate on (a .bak is kept on write)." };
        var paramOpt = new Option<string[]>("--param", "-p")
        {
            Description = "Operation parameter as name=value. Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };
        var dryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Compute and report changes but never write the file.",
        };

        var cmd = new Command("run", "Run a plugin save operation against a save file.");
        cmd.Arguments.Add(opArg);
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(paramOpt);
        cmd.Options.Add(dryRunOpt);
        cmd.SetAction((parseResult, ct) => Run(
            parseResult.GetValue(opArg),
            parseResult.GetValue(saveArg),
            parseResult.GetValue(paramOpt) ?? Array.Empty<string>(),
            parseResult.GetValue(dryRunOpt),
            parseResult.GetValue(quiet),
            ct));
        return cmd;
    }

    private static int List(bool json)
    {
        PluginManager.Shared.EnsureLoaded("cli");
        var descriptors = PluginManager.Shared.Descriptors;

        if (json)
        {
            Cli.WriteJson(descriptors.Select(d => new
            {
                id = d.Id,
                name = d.Manifest.Name,
                version = d.Manifest.Version,
                author = d.Manifest.Author,
                state = d.State.ToString(),
                enabled = d.Manifest.Enabled,
                error = d.LoadError,
                saveOperations = d.SaveOperations.Select(o => o.Id),
                consoleCommands = d.ConsoleCommands.Select(c => c.Name),
                editorTools = d.EditorTools.Select(t => t.Id),
            }));
            return Cli.Ok;
        }

        if (descriptors.Count == 0)
        {
            Console.WriteLine("No plugins installed.");
            Console.WriteLine($"Drop plugin folders under: {PluginPaths.UserPluginsDirectory}");
            return Cli.Ok;
        }

        Console.WriteLine($"{"STATE",-10} {"VERSION",-9} {"ID",-32} CAPABILITIES");
        foreach (var d in descriptors)
        {
            Console.WriteLine($"{d.State,-10} {d.Manifest.Version,-9} {d.Id,-32} {d.CapabilitySummary()}");
        }
        Console.WriteLine($"{descriptors.Count} plugin(s). Roots: {string.Join("; ", PluginPaths.Roots())}");
        return Cli.Ok;
    }

    private static int Info(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new CliUserErrorException("missing plugin id.");
        }
        PluginManager.Shared.EnsureLoaded("cli");
        var d = PluginManager.Shared.Descriptors
            .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliUserErrorException($"no plugin with id '{id}'. Run 'plugins list'.");

        var m = d.Manifest;
        Console.WriteLine($"{m.Name}  (v{m.Version})");
        Console.WriteLine($"  id        {m.Id}");
        if (!string.IsNullOrWhiteSpace(m.Author)) Console.WriteLine($"  author    {m.Author}");
        if (!string.IsNullOrWhiteSpace(m.Description)) Console.WriteLine($"  about     {m.Description}");
        Console.WriteLine($"  folder    {d.Folder}");
        Console.WriteLine($"  state     {d.State}{(d.LoadError is null ? "" : $" - {d.LoadError}")}");

        if (d.SaveOperations.Count > 0)
        {
            Console.WriteLine("  save operations:");
            foreach (var op in d.SaveOperations)
            {
                Console.WriteLine($"    {op.Id,-24} [{op.AppliesTo}] {op.DisplayName}");
                if (!string.IsNullOrWhiteSpace(op.Description)) Console.WriteLine($"        {op.Description}");
                foreach (var p in op.Parameters)
                {
                    var req = p.Required ? "required" : $"default '{p.DefaultValue}'";
                    Console.WriteLine($"        --param {p.Name}=...  ({req}) {p.Description}");
                }
            }
        }
        if (d.ConsoleCommands.Count > 0)
        {
            Console.WriteLine("  console commands:");
            foreach (var c in d.ConsoleCommands)
            {
                Console.WriteLine($"    {c.Name,-24} {c.Description}");
            }
        }
        if (d.EditorTools.Count > 0)
        {
            Console.WriteLine("  editor tools (GUI only):");
            foreach (var t in d.EditorTools)
            {
                Console.WriteLine($"    {t.Id,-24} {t.Title}");
            }
        }
        return Cli.Ok;
    }

    private static async Task<int> Run(string? operationId, string? save, string[] rawParams, bool dryRun, bool quiet, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new CliUserErrorException("missing operation id.");
            }
            var savePath = Cli.RequireFile(save, "save file");

            PluginManager.Shared.EnsureLoaded("cli");
            var match = PluginManager.Shared.SaveOperations
                .FirstOrDefault(c => string.Equals(c.Value.Id, operationId, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliUserErrorException(
                    $"no save operation '{operationId}'. Run 'plugins list' to see what is installed.");

            var parameters = ParseParameters(rawParams);
            var log = match.Plugin.Host?.Log ?? new NullPluginLog();

            var outcome = await SaveOperationRunner
                .RunAsync(match.Value, savePath, parameters, log, dryRun, ct)
                .ConfigureAwait(false);

            if (!outcome.Result.Success)
            {
                Console.Error.WriteLine($"error: {outcome.Result.Message}");
                return Cli.UserError;
            }

            if (outcome.Wrote)
            {
                // Let event-handler plugins react to the write (e.g. log, post-process).
                PluginManager.Shared.RaiseEvent(PluginEvents.SaveWritten, new Dictionary<string, object?>
                {
                    ["savePath"] = savePath,
                    ["saveKind"] = outcome.Kind,
                    ["operationId"] = match.Value.Id,
                });
            }

            var verb = dryRun ? "(dry run) would change" : outcome.Wrote ? "changed" : "no change";
            Cli.Info(quiet,
                $"{match.Value.Id}: {outcome.Result.Message} [{verb}; {outcome.Result.ChangeCount} edit(s)]"
                + (outcome.Wrote ? $"; wrote {Path.GetFileName(savePath)} (.bak kept)" : ""));
            return Cli.Ok;
        }
        catch (CliUserErrorException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Cli.UserError;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return Cli.UserError;
        }
    }

    private static Dictionary<string, string> ParseParameters(IEnumerable<string> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
        {
            var eq = entry.IndexOf('=');
            if (eq <= 0)
            {
                throw new CliUserErrorException($"--param must be name=value, got '{entry}'.");
            }
            result[entry[..eq].Trim()] = entry[(eq + 1)..];
        }
        return result;
    }

    /// <summary>Fallback log when an operation's plugin host is somehow unavailable.</summary>
    private sealed class NullPluginLog : IPluginLog
    {
        public void Info(string message) { }
        public void Warn(string message) => Console.Error.WriteLine($"warning: {message}");
        public void Error(string message, Exception? exception = null) => Console.Error.WriteLine($"error: {message}");
    }
}
