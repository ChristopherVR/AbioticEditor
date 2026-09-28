using System.CommandLine;
using System.Text.Json;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>world census</c> - read-only inventory of the objects placed in a region save (classes,
/// transforms, construction values, names, paint, inventory and power links). Phase 1 of the
/// base-building roadmap; nothing is written to the save.
/// </summary>
internal static class PlacedObjectCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Command BuildCensus(Option<bool> quiet)
    {
        var pathArg = new Argument<string>("path")
        {
            Description = "A WorldSave_*.sav file, or a world folder (every WorldSave_*.sav in it is censused).",
        };
        var outOpt = new Option<string?>("--out", "-o") { Description = "Write the JSON here instead of stdout." };
        var noObjects = new Option<bool>("--no-objects")
        {
            Description = "Omit the per-object list (class/field/map rollups only).",
        };
        var json = new Option<bool>("--json") { Description = "Emit JSON (default is a short text summary)." };

        var cmd = new Command("census",
            "Read-only census of placed objects (DeployedObjectMap and related maps): classes, transforms, "
            + "construction values, owners, paint, inventory and power links.");
        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(noObjects);
        cmd.Options.Add(json);
        cmd.SetAction(parse => Cli.Run(() => Run(
            parse.GetValue(pathArg), parse.GetValue(outOpt), parse.GetValue(noObjects),
            parse.GetValue(json), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Run(string? path, string? outPath, bool noObjects, bool json, bool quiet)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliUserErrorException("missing save path.");
        }
        var full = Path.GetFullPath(path);
        var files = Directory.Exists(full)
            ? Directory.GetFiles(full, "WorldSave_*.sav").Where(f => !f.EndsWith("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray()
            : [Cli.RequireFile(path, "save file")];
        if (files.Length == 0)
        {
            throw new CliUserErrorException($"no WorldSave_*.sav files under {full}.");
        }

        var reports = new List<PlacedObjectCensusReport>();
        foreach (var f in files)
        {
            var data = WorldSaveReader.ReadFromFile(f);
            reports.Add(PlacedObjectCensus.Build(data, Path.GetFileName(f), includeObjects: !noObjects));
        }

        if (json || outPath is not null)
        {
            object payload = reports.Count == 1 ? reports[0] : reports;
            var text = JsonSerializer.Serialize(payload, JsonOptions);
            if (outPath is not null)
            {
                File.WriteAllText(outPath, text);
                Cli.Info(quiet, $"Wrote census for {reports.Count} save(s) to {outPath}.");
            }
            else
            {
                Console.WriteLine(text);
            }
            return Cli.Ok;
        }

        foreach (var r in reports)
        {
            Console.WriteLine(
                $"{r.Source}: {r.ObjectCount} placed objects, {r.DistinctClasses} classes, "
                + $"{r.NonGameClassCount} non-game, {r.MissingClassCount} without class; "
                + $"keys: {r.KeysAreActorPaths} actor-path / {r.KeysAreGuids} guid / {r.KeysOther} other; "
                + $"{r.PowerLinks.Count} power sockets");
            foreach (var c in r.Classes.Take(8))
            {
                Console.WriteLine($"    {c.Count,5}  {c.ClassName}  [{c.Origin}]");
            }
        }
        return Cli.Ok;
    }
}
