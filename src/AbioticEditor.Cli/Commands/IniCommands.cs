using System.CommandLine;
using AbioticEditor.Core.Ini;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>ini list/get/set</c> - thin wrapper over <see cref="IniFile"/>, the order- and
/// comment-preserving UE ini engine in Core. Duplicate keys (UE's <c>+Key=</c> /
/// Admin.ini's repeated <c>Moderator=</c> lines) are first-class: <c>get</c> prints
/// every value, <c>set --add</c> appends another line instead of replacing.
/// </summary>
internal static class IniCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("ini", "Inspect or edit UE-style ini files (Admin.ini, SandboxSettings.ini, client config).");
        cmd.Subcommands.Add(BuildList());
        cmd.Subcommands.Add(BuildGet());
        cmd.Subcommands.Add(BuildSet(quiet));
        return cmd;
    }

    private static Command BuildList()
    {
        var fileArg = new Argument<string>("file") { Description = "Path to the .ini file." };
        var sectionArg = new Argument<string?>("section")
        {
            Description = "Section to list; omit to list all section names.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var cmd = new Command("list", "List an ini file's sections, or one section's key=value pairs.");
        cmd.Arguments.Add(fileArg);
        cmd.Arguments.Add(sectionArg);
        cmd.SetAction(parseResult => Cli.Run(() => List(
            parseResult.GetValue(fileArg),
            parseResult.GetValue(sectionArg))));
        return cmd;
    }

    private static Command BuildGet()
    {
        var fileArg = new Argument<string>("file") { Description = "Path to the .ini file." };
        var sectionArg = new Argument<string>("section") { Description = "Section name (case-insensitive)." };
        var keyArg = new Argument<string>("key") { Description = "Key name (case-insensitive)." };

        var cmd = new Command("get", "Print a key's value(s) - duplicate keys print one value per line.");
        cmd.Arguments.Add(fileArg);
        cmd.Arguments.Add(sectionArg);
        cmd.Arguments.Add(keyArg);
        cmd.SetAction(parseResult => Cli.Run(() => Get(
            parseResult.GetValue(fileArg),
            parseResult.GetValue(sectionArg),
            parseResult.GetValue(keyArg))));
        return cmd;
    }

    private static Command BuildSet(Option<bool> quiet)
    {
        var fileArg = new Argument<string>("file") { Description = "Path to the .ini file (a .bak of the previous content is kept)." };
        var sectionArg = new Argument<string>("section") { Description = "Section name (created when missing)." };
        var keyArg = new Argument<string>("key") { Description = "Key name." };
        var valueArg = new Argument<string>("value") { Description = "Value to write." };
        var addOpt = new Option<bool>("--add")
        {
            Description = "Append another key=value line (duplicate-key style) instead of replacing the first occurrence.",
        };

        var cmd = new Command("set", "Set a key's value (or --add a duplicate line), preserving comments, order and line endings.");
        cmd.Arguments.Add(fileArg);
        cmd.Arguments.Add(sectionArg);
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(valueArg);
        cmd.Options.Add(addOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Set(
            parseResult.GetValue(fileArg),
            parseResult.GetValue(sectionArg),
            parseResult.GetValue(keyArg),
            parseResult.GetValue(valueArg),
            parseResult.GetValue(addOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int List(string? file, string? section)
    {
        var ini = IniFile.Load(Cli.RequireFile(file, "ini file"));

        if (section is null)
        {
            foreach (var s in ini.Sections)
            {
                Console.WriteLine($"[{s.Name}] ({s.Keys.Count()} entr{(s.Keys.Count() == 1 ? "y" : "ies")})");
            }
            return Cli.Ok;
        }

        var found = ini.FindSection(section)
            ?? throw new CliUserErrorException($"section '[{section}]' not found.");
        foreach (var entry in found.Entries)
        {
            Console.WriteLine($"{entry.Key}={entry.Value}");
        }
        return Cli.Ok;
    }

    private static int Get(string? file, string? section, string? key)
    {
        var ini = IniFile.Load(Cli.RequireFile(file, "ini file"));
        var found = ini.FindSection(section ?? string.Empty)
            ?? throw new CliUserErrorException($"section '[{section}]' not found.");

        var values = found.GetValues(key ?? string.Empty);
        if (values.Count == 0)
        {
            throw new CliUserErrorException($"key '{key}' not found in '[{found.Name}]'.");
        }
        foreach (var value in values)
        {
            Console.WriteLine(value);
        }
        return Cli.Ok;
    }

    private static int Set(string? file, string? section, string? key, string? value, bool add, bool quiet)
    {
        var path = Cli.RequireFile(file, "ini file");
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
        {
            throw new CliUserErrorException("section and key are required.");
        }

        var ini = IniFile.Load(path);
        var target = ini.GetOrAddSection(section);
        if (add)
        {
            target.AddValue(key, value ?? string.Empty);
        }
        else
        {
            target.SetValue(key, value ?? string.Empty);
        }

        AbioticEditor.Core.Saves.SaveBackup.CreateFor(path);
        ini.Save(path);
        Cli.Info(quiet, $"{(add ? "Added" : "Set")} {key}={value} in [{section}] of {Path.GetFileName(path)} (backup kept).");
        return Cli.Ok;
    }
}
