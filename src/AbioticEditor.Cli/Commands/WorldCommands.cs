using System.CommandLine;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>world</c> - view and edit the per-actor world-state maps that were previously only
/// preserved verbatim (elevators, buttons, resource nodes, NPC spawners, power sockets,
/// vehicles, portals, triggers, trams, server entitlements). Every one of these is a
/// registered <see cref="IWorldMapFeature"/>, so this command is fully generic: it lists the
/// features present in a save, shows their entries/fields, and patches one field. Edits keep a
/// <c>.bak</c> like every other writer.
/// </summary>
internal static class WorldCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("world",
            "Inspect and edit world-state maps (elevators, buttons, resource nodes, vehicles, portals, …).");
        cmd.Subcommands.Add(BuildList(quiet));
        cmd.Subcommands.Add(BuildShow(quiet));
        cmd.Subcommands.Add(BuildSet(quiet));
        return cmd;
    }

    // ---------- world list <save> ----------

    private static Command BuildList(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };

        var cmd = new Command("list", "List the editable world-map features present in a save.");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parse => Cli.Run(() => List(parse.GetValue(saveArg), parse.GetValue(jsonOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int List(string? file, bool json, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var applicable = WorldMapFeatures.ApplicableTo(save)
            .Select(f => new { f.Id, f.DisplayName, f.MapName, count = f.Read(save).Count, f.Description })
            .ToList();

        if (json)
        {
            Cli.WriteJson(applicable);
            return Cli.Ok;
        }

        if (applicable.Count == 0)
        {
            Cli.Info(quiet, "No editable world-map features are present in this save.");
            return Cli.Ok;
        }

        Console.WriteLine($"Editable world-map features in {Path.GetFileName(path)}:");
        foreach (var f in applicable)
        {
            Console.WriteLine($"  {f.Id,-20} {f.count,5} entr{(f.count == 1 ? "y " : "ies")}  - {f.DisplayName}");
        }
        Console.WriteLine();
        Console.WriteLine("Use 'world show <save> <feature>' to see entries and fields.");
        return Cli.Ok;
    }

    // ---------- world show <save> <feature> ----------

    private static Command BuildShow(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var featureArg = new Argument<string>("feature") { Description = "Feature id (see 'world list')." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };
        var limitOpt = new Option<int>("--limit") { Description = "Max entries to print (default 50).", DefaultValueFactory = _ => 50 };

        var cmd = new Command("show", "Show entries and fields for one feature.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(featureArg);
        cmd.Options.Add(jsonOpt);
        cmd.Options.Add(limitOpt);
        cmd.SetAction(parse => Cli.Run(() => Show(
            parse.GetValue(saveArg), parse.GetValue(featureArg),
            parse.GetValue(jsonOpt), parse.GetValue(limitOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Show(string? file, string? featureId, bool json, int limit, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var feature = ResolveFeature(featureId);
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        if (!feature.AppliesTo(save))
        {
            throw new CliUserErrorException($"'{feature.Id}' ({feature.MapName}) is not present in this save.");
        }

        var entries = feature.Read(save);
        if (json)
        {
            Cli.WriteJson(entries.Take(limit).Select(e => new
            {
                e.Key,
                e.Label,
                fields = e.Fields.Select(f => new { f.Id, f.Label, f.Value, kind = f.Kind.ToString(), f.Editable, f.Options }),
            }));
            return Cli.Ok;
        }

        Console.WriteLine($"{feature.DisplayName} - {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} ({feature.Description})");
        var i = 0;
        foreach (var e in entries.Take(limit))
        {
            Console.WriteLine($"  #{i}  {e.Label}");
            Console.WriteLine($"      key: {e.Key}");
            foreach (var f in e.Fields)
            {
                var editable = f.Editable ? "" : " (read-only)";
                // Show small choice lists inline; summarise large ones (e.g. the 134 teleporter tags).
                var options = f.Options switch
                {
                    { Count: > 0 and <= 12 } o => $"  [{string.Join("|", o)}]",
                    { Count: > 12 } o => $"  ({o.Count} choices, see --json)",
                    _ => "",
                };
                Console.WriteLine($"      {f.Id,-22}= {f.Value}{editable}{options}");
            }
            i++;
        }
        if (entries.Count > limit)
        {
            Console.WriteLine($"  ... {entries.Count - limit} more (raise --limit to see them).");
        }
        Console.WriteLine();
        Console.WriteLine("Edit with: world set <save> <feature> <#index|key> <field> <value>");
        return Cli.Ok;
    }

    // ---------- world set <save> <feature> <entry> <field> <value> ----------

    private static Command BuildSet(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var featureArg = new Argument<string>("feature") { Description = "Feature id (see 'world list')." };
        var entryArg = new Argument<string>("entry") { Description = "Entry key, a #index (from 'world show'), or a unique substring of the key." };
        var fieldArg = new Argument<string>("field") { Description = "Field id to set." };
        var valueArg = new Argument<string>("value") { Description = "New value." };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "Validate and report the change without writing." };

        var cmd = new Command("set", "Set one field of one entry (keeps a .bak on write).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(featureArg);
        cmd.Arguments.Add(entryArg);
        cmd.Arguments.Add(fieldArg);
        cmd.Arguments.Add(valueArg);
        cmd.Options.Add(dryRunOpt);
        cmd.SetAction(parse => Cli.Run(() => Set(
            parse.GetValue(saveArg), parse.GetValue(featureArg), parse.GetValue(entryArg),
            parse.GetValue(fieldArg), parse.GetValue(valueArg), parse.GetValue(dryRunOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Set(string? file, string? featureId, string? entryRef, string? field, string? value, bool dryRun, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var feature = ResolveFeature(featureId);
        var data = WorldSaveReader.ReadFromFile(path);
        var save = data.Raw;
        if (!feature.AppliesTo(save))
        {
            throw new CliUserErrorException($"'{feature.Id}' ({feature.MapName}) is not present in this save.");
        }

        var entryKey = ResolveEntryKey(feature, save, entryRef);
        var result = feature.SetField(save, entryKey, field ?? string.Empty, value);
        if (result.IsError)
        {
            throw new CliUserErrorException(result.Error!);
        }
        if (!result.Changed)
        {
            Cli.Info(quiet, $"No change - '{field}' is already '{value}'.");
            return Cli.Ok;
        }

        if (dryRun)
        {
            Cli.Info(quiet, $"[dry-run] would set {feature.Id} {field}='{value}' on {entryKey} and write {Path.GetFileName(path)}.");
            return Cli.Ok;
        }

        WorldSaveWriter.WriteToFile(data, path);
        Cli.Info(quiet, $"Set {feature.Id} {field}='{value}' on {entryKey}. Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
        return Cli.Ok;
    }

    // ---------- helpers ----------

    private static IWorldMapFeature ResolveFeature(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new CliUserErrorException("missing feature id.");
        }
        return WorldMapFeatures.Find(id)
            ?? throw new CliUserErrorException(
                $"unknown feature '{id}'. Known: {string.Join(", ", WorldMapFeatures.All.Select(f => f.Id))}.");
    }

    /// <summary>Resolves an entry reference: <c>#index</c>, an exact key, or a unique key substring.</summary>
    private static string ResolveEntryKey(IWorldMapFeature feature, UeSaveGame.SaveGame save, string? entryRef)
    {
        if (string.IsNullOrWhiteSpace(entryRef))
        {
            throw new CliUserErrorException("missing entry reference.");
        }
        var entries = feature.Read(save);

        if (entryRef.StartsWith('#') && int.TryParse(entryRef[1..], out var index))
        {
            if (index < 0 || index >= entries.Count)
            {
                throw new CliUserErrorException($"entry index {index} out of range (0..{entries.Count - 1}).");
            }
            return entries[index].Key;
        }

        if (entries.Any(e => string.Equals(e.Key, entryRef, StringComparison.Ordinal)))
        {
            return entryRef;
        }

        var matches = entries.Where(e => e.Key.Contains(entryRef, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0].Key,
            0 => throw new CliUserErrorException($"no entry matches '{entryRef}'. Use 'world show' to list keys."),
            _ => throw new CliUserErrorException($"'{entryRef}' matches {matches.Count} entries; be more specific or use #index."),
        };
    }
}
