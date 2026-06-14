using System.CommandLine;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>vehicle</c> - inspect and edit spawned vehicles in a region save's <c>VehicleMap</c>:
/// toggle drivable ("unlock") / wrecked state, move a vehicle, or reset it to its original
/// spawn position (resolved from the cooked level via the installed game). On-board storage is
/// edited through the normal container editor. Every write keeps a <c>.bak</c>.
/// </summary>
internal static class VehicleCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("vehicle",
            "Inspect and edit vehicles in a region save (unlock/lock, repair/wreck, move, reset to spawn).");
        cmd.Subcommands.Add(BuildList(quiet));
        cmd.Subcommands.Add(BuildInfo(quiet));
        cmd.Subcommands.Add(BuildFlag("unlock", "Make the vehicle drivable.", quiet));
        cmd.Subcommands.Add(BuildFlag("lock", "Make the vehicle not drivable.", quiet));
        cmd.Subcommands.Add(BuildFlag("repair", "Repair a wrecked vehicle.", quiet));
        cmd.Subcommands.Add(BuildFlag("wreck", "Mark the vehicle as destroyed.", quiet));
        cmd.Subcommands.Add(BuildMove(quiet));
        cmd.Subcommands.Add(BuildReset(quiet));
        return cmd;
    }

    // ---------- vehicle list <save> ----------

    private static Command BuildList(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };

        var cmd = new Command("list", "List the vehicles in a region save.");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parse => Cli.Run(() => List(parse.GetValue(saveArg), parse.GetValue(jsonOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int List(string? file, bool json, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var vehicles = WorldSaveReader.ReadFromFile(path).Vehicles;

        if (json)
        {
            Cli.WriteJson(vehicles.Select((v, i) => new
            {
                index = i,
                v.Id,
                v.DisplayName,
                v.Region,
                shortClass = v.ShortClass,
                v.Driveable,
                v.Destroyed,
                location = new { v.X, v.Y, v.Z },
                v.InventoryItemCount,
            }));
            return Cli.Ok;
        }

        if (vehicles.Count == 0)
        {
            Cli.Info(quiet, "No vehicles in this save (VehicleMap is region-save only).");
            return Cli.Ok;
        }

        Console.WriteLine($"Vehicles in {Path.GetFileName(path)} ({vehicles.Count}):");
        var idx = 0;
        foreach (var v in vehicles)
        {
            var state = v.Destroyed ? "WRECKED" : v.Driveable ? "DRIVABLE" : "LOCKED";
            Console.WriteLine($"  #{idx,-2} {v.DisplayName,-14} {v.Region,-20} {state,-8} "
                + $"items {v.InventoryItemCount,-3} @ ({v.X:F0},{v.Y:F0},{v.Z:F0})");
            idx++;
        }
        Console.WriteLine();
        Console.WriteLine("Edit with: vehicle unlock|lock|repair|wreck|move|reset <save> <#index|name|region> ...");
        return Cli.Ok;
    }

    // ---------- vehicle info <save> <vehicle> ----------

    private static Command BuildInfo(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var vehArg = new Argument<string>("vehicle") { Description = "Vehicle #index, name, region, or key substring." };

        var cmd = new Command("info", "Show one vehicle's details (and its resolved spawn position).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(vehArg);
        cmd.SetAction(parse => Cli.Run(() => Info(parse.GetValue(saveArg), parse.GetValue(vehArg), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Info(string? file, string? vehRef, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        var v = ResolveVehicle(data, vehRef);

        Console.WriteLine($"{v.DisplayName}  ({v.ShortClass})");
        Console.WriteLine($"  region:    {v.Region}");
        Console.WriteLine($"  state:     {(v.Destroyed ? "WRECKED" : v.Driveable ? "DRIVABLE" : "LOCKED")}");
        Console.WriteLine($"  location:  ({v.X:F1}, {v.Y:F1}, {v.Z:F1})");
        Console.WriteLine($"  inventory: {v.InventoryItemCount} item(s)");
        Console.WriteLine($"  key:       {v.Id}");

        var spawn = ResolveSpawn(v);
        if (spawn is { } s)
        {
            var atSpawn = AtSpawn(v, s);
            Console.WriteLine($"  spawn:     ({s.X:F1}, {s.Y:F1}, {s.Z:F1})  {(atSpawn ? "(at spawn)" : "(moved from spawn)")}");
        }
        else
        {
            Cli.Info(quiet, "  spawn:     (unresolved - game install / mappings unavailable)");
        }
        return Cli.Ok;
    }

    // ---------- vehicle unlock|lock|repair|wreck <save> <vehicle> ----------

    private static Command BuildFlag(string verb, string help, Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var vehArg = new Argument<string>("vehicle") { Description = "Vehicle #index, name, region, or key substring." };

        var cmd = new Command(verb, help);
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(vehArg);
        cmd.SetAction(parse => Cli.Run(() => Edit(
            parse.GetValue(saveArg), parse.GetValue(vehArg), parse.GetValue(quiet),
            v => verb switch
            {
                "unlock" => v with { Driveable = true },
                "lock" => v with { Driveable = false },
                "repair" => v with { Destroyed = false },
                "wreck" => v with { Destroyed = true },
                _ => v,
            },
            v => $"{verb}ed ({(v.Destroyed ? "wrecked" : v.Driveable ? "drivable" : "locked")})")));
        return cmd;
    }

    // ---------- vehicle move <save> <vehicle> <x> <y> <z> ----------

    private static Command BuildMove(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var vehArg = new Argument<string>("vehicle") { Description = "Vehicle #index, name, region, or key substring." };
        var xArg = new Argument<double>("x") { Description = "World X." };
        var yArg = new Argument<double>("y") { Description = "World Y." };
        var zArg = new Argument<double>("z") { Description = "World Z." };

        var cmd = new Command("move", "Move a vehicle to a world position.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(vehArg);
        cmd.Arguments.Add(xArg);
        cmd.Arguments.Add(yArg);
        cmd.Arguments.Add(zArg);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var x = parse.GetValue(xArg);
            var y = parse.GetValue(yArg);
            var z = parse.GetValue(zArg);
            return Edit(parse.GetValue(saveArg), parse.GetValue(vehArg), parse.GetValue(quiet),
                v => v with { X = x, Y = y, Z = z },
                v => $"moved to ({v.X:F0}, {v.Y:F0}, {v.Z:F0})");
        }));
        return cmd;
    }

    // ---------- vehicle reset <save> <vehicle> ----------

    private static Command BuildReset(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var vehArg = new Argument<string>("vehicle") { Description = "Vehicle #index, name, region, or key substring." };

        var cmd = new Command("reset", "Reset a vehicle to its original spawn position (needs the installed game).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(vehArg);
        cmd.SetAction(parse => Cli.Run(() => Reset(parse.GetValue(saveArg), parse.GetValue(vehArg), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Reset(string? file, string? vehRef, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        var v = ResolveVehicle(data, vehRef);

        var spawn = ResolveSpawn(v)
            ?? throw new CliUserErrorException(
                "could not resolve the spawn position (the game install / usmap mappings are required).");

        var updated = v with
        {
            X = spawn.X, Y = spawn.Y, Z = spawn.Z,
            QuatX = spawn.QuatX, QuatY = spawn.QuatY, QuatZ = spawn.QuatZ, QuatW = spawn.QuatW,
        };
        WorldSaveWriter.ApplyVehicles(data, new[] { updated });
        WorldSaveWriter.WriteToFile(data, path);
        Cli.Info(quiet, $"Reset '{v.DisplayName}' to spawn ({spawn.X:F0}, {spawn.Y:F0}, {spawn.Z:F0}). "
            + $"Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
        return Cli.Ok;
    }

    // ---------- shared ----------

    private static int Edit(
        string? file, string? vehRef, bool quiet,
        Func<WorldVehicle, WorldVehicle> mutate, Func<WorldVehicle, string> describe)
    {
        var path = Cli.RequireFile(file, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        var v = ResolveVehicle(data, vehRef);

        var updated = mutate(v);
        WorldSaveWriter.ApplyVehicles(data, new[] { updated });
        WorldSaveWriter.WriteToFile(data, path);
        Cli.Info(quiet, $"Vehicle '{updated.DisplayName}' ({updated.Region}) {describe(updated)}. "
            + $"Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
        return Cli.Ok;
    }

    private static WorldVehicle ResolveVehicle(WorldSaveData data, string? vehRef)
    {
        var vehicles = data.Vehicles;
        if (vehicles.Count == 0)
        {
            throw new CliUserErrorException("this save has no vehicles (VehicleMap is region-save only).");
        }
        if (string.IsNullOrWhiteSpace(vehRef))
        {
            throw new CliUserErrorException("missing vehicle reference (use a #index, name, region, or key from 'vehicle list').");
        }

        if (vehRef.StartsWith('#') && int.TryParse(vehRef[1..], out var index))
        {
            if (index < 0 || index >= vehicles.Count)
            {
                throw new CliUserErrorException($"vehicle index {index} out of range (0..{vehicles.Count - 1}).");
            }
            return vehicles[index];
        }

        var matches = vehicles.Where(v =>
            v.Id.Contains(vehRef, StringComparison.OrdinalIgnoreCase)
            || v.DisplayName.Contains(vehRef, StringComparison.OrdinalIgnoreCase)
            || v.Region.Contains(vehRef, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliUserErrorException($"no vehicle matches '{vehRef}'. Use 'vehicle list'."),
            _ => throw new CliUserErrorException($"'{vehRef}' matches {matches.Count} vehicles; be more specific or use #index."),
        };
    }

    private static ActorTransform? ResolveSpawn(WorldVehicle v)
    {
        try
        {
            using var provider = GameAssetProvider.CreateForLocalInstall();
            return provider?.TryGetActorTransform(v.VehicleId ?? v.Id);
        }
        catch
        {
            return null;
        }
    }

    private static bool AtSpawn(WorldVehicle v, ActorTransform spawn)
    {
        var dx = v.X - spawn.X;
        var dy = v.Y - spawn.Y;
        var dz = v.Z - spawn.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) < 100.0 * 100.0; // within 1 m
    }
}
