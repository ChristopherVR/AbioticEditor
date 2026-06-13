using System.CommandLine;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;

namespace AbioticEditor.Cli;

/// <summary>
/// Assembles the <c>abioticeditor</c> command tree. Each built-in command is a thin wrapper
/// over AbioticEditor.Core - no save/ini parsing logic lives in this assembly. Plugins can
/// add their own subcommands (see <see cref="RegisterPluginCommands"/>).
/// </summary>
public static class CommandTree
{
    public static RootCommand Build()
    {
        var quiet = new Option<bool>("--quiet", "-q")
        {
            Description = "Suppress informational output (data and warnings still print).",
            Recursive = true,
        };

        var root = new RootCommand(
            "Abiotic Factor save editor (headless). Reads and edits Player_*.sav / WorldSave_*.sav "
            + "files and the ini files next to them. Every write keeps a .bak of the previous file.");
        root.Options.Add(quiet);

        root.Subcommands.Add(ScanCommand.Build(quiet));
        root.Subcommands.Add(InfoCommand.Build(quiet));
        root.Subcommands.Add(CompareCommand.Build(quiet));
        root.Subcommands.Add(JsonCommands.BuildExport(quiet));
        root.Subcommands.Add(JsonCommands.BuildImport(quiet));
        root.Subcommands.Add(FlagsCommands.Build(quiet));
        root.Subcommands.Add(SteamIdCommand.Build(quiet));
        root.Subcommands.Add(IniCommands.Build(quiet));
        root.Subcommands.Add(VersionCommand.Build());
        root.Subcommands.Add(UpdateCommand.Build(quiet));

        // Built-in plugin management ('plugins list/info/run') plus any verbs plugins add.
        root.Subcommands.Add(PluginsCommands.Build(quiet));
        RegisterPluginCommands(root);
        return root;
    }

    /// <summary>
    /// Loads plugins and grafts each plugin <see cref="Plugins.Cli.IConsoleCommand"/> onto the
    /// root as a top-level verb, so <c>abioticeditor &lt;plugin-command&gt;</c> just works.
    /// A plugin command whose name collides with a built-in (or another plugin) is skipped
    /// with a warning - a plugin can never shadow a shipped command. The whole step is
    /// best-effort: a plugin that fails to load is recorded and ignored, never fatal, and the
    /// CLI works fully even with no plugins. Set <c>ABIOTIC_NO_PLUGINS=1</c> to skip entirely.
    /// </summary>
    private static void RegisterPluginCommands(RootCommand root)
    {
        if (IsTruthy(Environment.GetEnvironmentVariable("ABIOTIC_NO_PLUGINS")))
        {
            return;
        }

        try
        {
            // The CLI can use save operations, console commands, and event handlers, but not
            // UI surfaces (editor tools / menu actions). Skip loading the code of plugins that
            // declare ONLY UI capabilities; load the rest (and any that declare none).
            var uiOnly = new[] { PluginCapabilities.EditorTool, PluginCapabilities.WebTool, PluginCapabilities.MenuAction };
            PluginManager.Shared.EnsureLoaded("cli", manifest =>
                manifest.Capabilities.Count == 0
                || manifest.Capabilities.Any(c =>
                    !uiOnly.Contains(c, StringComparer.OrdinalIgnoreCase)));

            var taken = new HashSet<string>(
                root.Subcommands.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var capability in PluginManager.Shared.ConsoleCommands)
            {
                var command = capability.Value;
                if (!taken.Add(command.Name))
                {
                    EditorLog.Warn("Plugins",
                        $"command '{command.Name}' from plugin '{capability.Plugin.Id}' collides with an "
                        + "existing command and was skipped.");
                    continue;
                }
                var host = capability.Plugin.Host;
                if (host is not null)
                {
                    root.Subcommands.Add(PluginCliBridge.Adapt(command, host));
                }
            }
        }
        catch (Exception ex)
        {
            // Plugin wiring must never stop the built-in CLI from running.
            EditorLog.Error("Plugins", "Failed to register plugin commands", ex);
        }
    }

    private static bool IsTruthy(string? value)
        => value is "1" or "true" or "TRUE" or "yes" or "YES";
}
