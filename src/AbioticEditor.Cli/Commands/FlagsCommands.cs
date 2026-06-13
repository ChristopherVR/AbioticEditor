using System.CommandLine;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>flags list/set</c> - quest-progression flags on a world save. Setting a flag
/// respects the <see cref="FlagGate"/> prerequisite model: missing prerequisites are
/// reported on stderr and the write is refused unless <c>--force</c> is given.
/// </summary>
internal static class FlagsCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("flags", "List or edit a world save's quest-progression flags.");
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildSet(quiet));
        return cmd;
    }

    private static Command BuildList()
    {
        var saveArg = new Argument<string>("world")
        {
            Description = "Path to a WorldSave_*.sav file.",
        };
        var filterOpt = new Option<string?>("--filter")
        {
            Description = "Only show flags containing this text (case-insensitive; also matches the area).",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit a JSON array instead of the human-readable list.",
        };

        var cmd = new Command("list", "List the flags currently set on a world save.");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(filterOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parseResult => Cli.Run(() => List(
            parseResult.GetValue(saveArg),
            parseResult.GetValue(filterOpt),
            parseResult.GetValue(jsonOpt))));
        return cmd;
    }

    private static Command BuildSet(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("world")
        {
            Description = "Path to a WorldSave_*.sav file (a .bak of the previous content is kept on write).",
        };
        var flagArg = new Argument<string>("flag")
        {
            Description = "Flag name, e.g. Office_NewGameStarted.",
        };
        var clearOpt = new Option<bool>("--clear")
        {
            Description = "Remove the flag instead of setting it.",
        };
        var forceOpt = new Option<bool>("--force")
        {
            Description = "Set the flag even when its story prerequisites are missing.",
        };

        var cmd = new Command("set", "Set (or with --clear remove) one flag on a world save.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(flagArg);
        cmd.Options.Add(clearOpt);
        cmd.Options.Add(forceOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Set(
            parseResult.GetValue(saveArg),
            parseResult.GetValue(flagArg),
            parseResult.GetValue(clearOpt),
            parseResult.GetValue(forceOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int List(string? save, string? filter, bool json)
    {
        var path = Cli.RequireFile(save, "world save");
        var data = WorldSaveReader.ReadFromFile(path);

        var flags = data.Flags
            .Select(QuestFlagCatalog.Lookup)
            .Where(f => filter is null
                || f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || f.Area.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (json)
        {
            Cli.WriteJson(flags.Select(f => new
            {
                name = f.Name,
                area = f.Area,
                category = f.Category.ToString(),
                friendlyName = f.FriendlyName,
            }));
            return Cli.Ok;
        }

        foreach (var f in flags)
        {
            Console.WriteLine($"{f.Name,-42} {f.Area,-13} {f.Category}");
        }
        Console.WriteLine($"{flags.Count} flag(s){(filter is null ? string.Empty : $" matching '{filter}'")} of {data.Flags.Count} set.");
        return Cli.Ok;
    }

    private static int Set(string? save, string? flag, bool clear, bool force, bool quiet)
    {
        var path = Cli.RequireFile(save, "world save");
        if (string.IsNullOrWhiteSpace(flag))
        {
            throw new CliUserErrorException("missing flag name.");
        }

        var data = WorldSaveReader.ReadFromFile(path);
        var flags = data.Flags.ToList();
        var existing = flags.FindAll(f => string.Equals(f, flag, StringComparison.OrdinalIgnoreCase));

        if (clear)
        {
            if (existing.Count == 0)
            {
                Cli.Info(quiet, $"'{flag}' is not set on {Path.GetFileName(path)} - nothing to do.");
                return Cli.Ok;
            }
            flags.RemoveAll(f => string.Equals(f, flag, StringComparison.OrdinalIgnoreCase));
            Write(data, flags, path);
            Cli.Info(quiet, $"Cleared '{existing[0]}' from {Path.GetFileName(path)} (backup kept).");
            return Cli.Ok;
        }

        if (existing.Count > 0)
        {
            Cli.Info(quiet, $"'{existing[0]}' is already set on {Path.GetFileName(path)} - nothing to do.");
            return Cli.Ok;
        }

        if (!QuestFlagCatalog.KnownFlags.Contains(flag, StringComparer.OrdinalIgnoreCase))
        {
            Cli.Warn($"'{flag}' is not a flag observed in any known save - setting it anyway.");
        }

        // FlagGate prerequisite model: story triggers are linear and region flags need
        // the chapter where their region opens.
        var have = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
        var missing = FlagGate.PrerequisitesFor(flag).Where(p => !have.Contains(p)).ToList();
        if (missing.Count > 0)
        {
            Cli.Warn($"'{flag}' has {missing.Count} missing prerequisite flag(s): {string.Join(", ", missing)}");
            if (!force)
            {
                throw new CliUserErrorException(
                    "prerequisites missing - set them first or pass --force to write anyway.");
            }
        }

        flags.Add(flag);
        Write(data, flags, path);
        Cli.Info(quiet, $"Set '{flag}' on {Path.GetFileName(path)} (backup kept).");
        return Cli.Ok;
    }

    private static void Write(WorldSaveData data, IReadOnlyList<string> flags, string path)
    {
        if (!WorldSaveWriter.ApplyFlags(data, flags))
        {
            // Delta-serialization: a world that never had a flag carries no WorldFlags
            // array, and the writer only patches existing structure.
            throw new CliUserErrorException(
                $"{Path.GetFileName(path)} has no WorldFlags array to edit - flags for this region "
                + "likely live in WorldSave_Facility.sav (or the world hasn't been visited yet).");
        }
        WorldSaveWriter.WriteToFile(data, path);
    }
}
