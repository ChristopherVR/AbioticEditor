using System.CommandLine;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>world power plug|unplug|repair</c> - power rerouting and repair on a region save, staged in the
/// same <see cref="StagedBaseEdits"/> the app uses (so the file written is byte-identical to the app's),
/// previewed with <c>--dry-run</c>, applied all-or-nothing and written with a <c>.bak</c>.
/// </summary>
internal static class PowerEditCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("power", "Plug devices into sockets, unplug them, or repair broken power links (staged; --dry-run previews).");
        cmd.Subcommands.Add(BuildPlug(quiet));
        cmd.Subcommands.Add(BuildUnplug(quiet));
        cmd.Subcommands.Add(BuildRepair(quiet));
        return cmd;
    }

    private sealed record Common(Argument<string> Save, Option<bool> DryRun, Option<bool> Json);

    private static Common AddCommon(Command cmd)
    {
        var save = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var dry = new Option<bool>("--dry-run") { Description = "Print the preview and write nothing." };
        var json = new Option<bool>("--json") { Description = "Print the preview as JSON." };
        cmd.Arguments.Add(save);
        cmd.Options.Add(dry);
        cmd.Options.Add(json);
        return new Common(save, dry, json);
    }

    private static Command BuildPlug(Option<bool> quiet)
    {
        var cmd = new Command("plug",
            "Plug a device into a socket. The device is unplugged from its current socket first (a device takes power from one place).");
        var c = AddCommon(cmd);
        var socket = new Option<string>("--socket", "-s")
        {
            Description = "The socket: an outlet id (device key plus outlet number, e.g. <32 hex>2) or a wall socket's key.",
            Required = true,
        };
        var device = new Option<string>("--device", "-d") { Description = "The device's key (32 hex).", Required = true };
        cmd.Options.Add(socket);
        cmd.Options.Add(device);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = PlacedObjectEditCommands.Open(parse.GetValue(c.Save));
            edits.StagePlug(parse.GetValue(socket)!.Trim(), parse.GetValue(device)!.Trim());
            return PlacedObjectEditCommands.Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "plugged");
        }));
        return cmd;
    }

    private static Command BuildUnplug(Option<bool> quiet)
    {
        var cmd = new Command("unplug", "Unplug whatever a socket powers.");
        var c = AddCommon(cmd);
        var socket = new Option<string[]>("--socket", "-s")
        {
            Description = "Socket id(s). Repeat, or comma-separate, for several.",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        cmd.Options.Add(socket);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = PlacedObjectEditCommands.Open(parse.GetValue(c.Save));
            foreach (var s in (parse.GetValue(socket) ?? []).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                edits.StageUnplug(s);
            }
            return PlacedObjectEditCommands.Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "unplugged");
        }));
        return cmd;
    }

    private static Command BuildRepair(Option<bool> quiet)
    {
        var cmd = new Command("repair",
            "Find broken power links and leftover outlet records (the other WorldSave_*.sav files in the folder are read to tell "
            + "'in another region' from 'gone'). Applies the recommended fixes; --all includes the rest (extra feeds).");
        var c = AddCommon(cmd);
        var all = new Option<bool>("--all") { Description = "Also apply fixes that are not recommended (a device fed by several sockets keeps only the nearest)." };
        var list = new Option<bool>("--list") { Description = "Only list what was found; change nothing." };
        cmd.Options.Add(all);
        cmd.Options.Add(list);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = PlacedObjectEditCommands.Open(parse.GetValue(c.Save));
            var others = PlacedObjectEditCommands.ReadSiblings(path);
            edits.OtherSaves = others;
            var positions = PlacedObjectCensus.Build(data).Objects!
                .Where(o => o.Transform?.Translation is not null)
                .GroupBy(o => o.Key).ToDictionary(g => g.Key, g => g.First().Transform!.Translation!.Value, StringComparer.Ordinal);
            var fixes = PowerRepair.Find(data, others, key => positions.TryGetValue(key, out var p) ? p : null);
            if (others.Count == 0) Cli.Warn("no other world saves were found next to this one; only fixes that need none are listed.");
            foreach (var group in fixes.GroupBy(f => f.Kind))
            {
                Console.WriteLine($"{group.Key}: {group.Count()}{(group.First().Recommended ? " (recommended)" : string.Empty)}");
                foreach (var f in group.Take(parse.GetValue(list) ? int.MaxValue : 10)) Console.WriteLine($"  {f.SocketId}: {f.Description}");
            }
            if (fixes.Count == 0) Console.WriteLine("Nothing to repair.");
            if (parse.GetValue(list) || fixes.Count == 0) return Cli.Ok;
            var chosen = fixes.Where(f => f.Recommended || parse.GetValue(all)).ToList();
            if (chosen.Count == 0)
            {
                Console.WriteLine("No recommended fixes; use --all to apply the others.");
                return Cli.Ok;
            }
            edits.StageRepairs(chosen);
            return PlacedObjectEditCommands.Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "repaired");
        }));
        return cmd;
    }
}
