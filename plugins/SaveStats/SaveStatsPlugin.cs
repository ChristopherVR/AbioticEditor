using System.Globalization;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;

namespace AbioticEditor.Samples.SaveStats;

/// <summary>
/// Entry point: registers one new CLI verb. Demonstrates the "add a whole command" extension
/// point - the base tool never shipped <c>save-stats</c>; this plugin grafts it on, and it
/// then behaves exactly like a built-in command (help, exit codes, --quiet).
/// </summary>
public sealed class SaveStatsPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
        => registry.AddConsoleCommand(new SaveStatsCommand());
}

/// <summary>
/// <c>save-stats &lt;save&gt; [--json]</c> - prints a one-glance summary of a save file
/// (kind, version, and a few headline numbers per kind). Read-only; never writes.
/// </summary>
public sealed class SaveStatsCommand : IConsoleCommand
{
    public string Name => "save-stats";

    public string Description => "Print a quick summary of a player or world save file.";

    public IReadOnlyList<PluginCommandArgument> Arguments { get; } = new[]
    {
        new PluginCommandArgument("save", "Path to the .sav file to summarize."),
    };

    public IReadOnlyList<PluginCommandOption> Options { get; } = new[]
    {
        new PluginCommandOption("json", "Emit JSON instead of text.", IsFlag: true),
    };

    public Task<int> InvokeAsync(IConsoleCommandContext context, CancellationToken cancellationToken = default)
    {
        var path = context.RequireArgument("save");
        if (!File.Exists(path))
        {
            context.Error.WriteLine($"error: save file not found: {path}");
            return Task.FromResult(1);
        }

        var kind = SaveKindDetector.Detect(path);
        var json = context.GetFlag("json");

        switch (kind)
        {
            case SaveKind.Player:
                Summarize(context, path, ReadPlayer(path), json);
                break;
            case SaveKind.World or SaveKind.Metadata:
                Summarize(context, path, ReadWorld(path), json);
                break;
            default:
                context.Out.WriteLine(json
                    ? $"{{ \"file\": \"{Escape(Path.GetFileName(path))}\", \"kind\": \"{kind}\" }}"
                    : $"{Path.GetFileName(path)}: {kind} save (no detailed summary available).");
                break;
        }

        return Task.FromResult(0);
    }

    private static (string File, string[] Lines, (string Key, string Value)[] Pairs) ReadPlayer(string path)
    {
        var data = PlayerSaveReader.ReadFromFile(path);
        var topLevel = data.Skills.Count == 0 ? 0 : data.Skills.Max(s => s.Level);
        var pairs = new[]
        {
            ("kind", "player"),
            ("abfVersion", data.AbfVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"),
            ("money", data.Stats.Money.ToString(CultureInfo.InvariantCulture)),
            ("hunger", Fmt(data.Stats.Hunger)),
            ("thirst", Fmt(data.Stats.Thirst)),
            ("sanity", Fmt(data.Stats.Sanity)),
            ("skills", data.Skills.Count.ToString(CultureInfo.InvariantCulture)),
            ("topSkillLevel", topLevel.ToString(CultureInfo.InvariantCulture)),
            ("recipes", data.Recipes.Count.ToString(CultureInfo.InvariantCulture)),
            ("traits", data.Traits.Count.ToString(CultureInfo.InvariantCulture)),
        };
        var lines = new[]
        {
            $"money {data.Stats.Money}   hunger {Fmt(data.Stats.Hunger)}   thirst {Fmt(data.Stats.Thirst)}   sanity {Fmt(data.Stats.Sanity)}",
            $"skills {data.Skills.Count} (top level {topLevel})   recipes {data.Recipes.Count}   traits {data.Traits.Count}",
        };
        return ($"{Path.GetFileName(path)}  [PLAYER, ABF v{data.AbfVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"}]", lines, pairs);
    }

    private static (string File, string[] Lines, (string Key, string Value)[] Pairs) ReadWorld(string path)
    {
        var data = WorldSaveReader.ReadFromFile(path);
        var pairs = new[]
        {
            ("kind", "world"),
            ("abfVersion", data.AbfVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"),
            ("flags", data.Flags.Count.ToString(CultureInfo.InvariantCulture)),
            ("containers", data.Containers.Count.ToString(CultureInfo.InvariantCulture)),
            ("doors", data.Doors.Count.ToString(CultureInfo.InvariantCulture)),
            ("droppedItems", data.DroppedItems.Count.ToString(CultureInfo.InvariantCulture)),
            ("npcs", data.Npcs.Count.ToString(CultureInfo.InvariantCulture)),
            ("minutesPassed", data.MinutesPassed?.ToString(CultureInfo.InvariantCulture) ?? "?"),
        };
        var lines = new[]
        {
            $"flags {data.Flags.Count}   containers {data.Containers.Count}   doors {data.Doors.Count}",
            $"dropped {data.DroppedItems.Count}   npcs {data.Npcs.Count}   minutes played {data.MinutesPassed?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
        };
        return ($"{Path.GetFileName(path)}  [WORLD, ABF v{data.AbfVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"}]", lines, pairs);
    }

    private static void Summarize(
        IConsoleCommandContext context,
        string path,
        (string File, string[] Lines, (string Key, string Value)[] Pairs) summary,
        bool json)
    {
        if (json)
        {
            var body = string.Join(",\n", summary.Pairs.Select(p =>
                $"  \"{p.Key}\": \"{Escape(p.Value)}\""));
            context.Out.WriteLine($"{{\n  \"file\": \"{Escape(Path.GetFileName(path))}\",\n{body}\n}}");
            return;
        }
        context.Out.WriteLine(summary.File);
        foreach (var line in summary.Lines)
        {
            context.Out.WriteLine($"  {line}");
        }
    }

    private static string Fmt(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
