using System.CommandLine;
using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>gamepass</c> - read and edit Game Pass / Microsoft Store saves, which are stored as Xbox
/// "wgs" containers (an <c>ABF_SAVE_VERSION</c> bundle of every world/player save) rather than
/// loose <c>.sav</c> files. <c>list</c> shows the saves packed in a wgs folder; <c>extract</c>
/// writes one out as a normal <c>.sav</c>; <c>import</c> packs an edited <c>.sav</c> back in.
/// </summary>
internal static class GamePassCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("gamepass", "Read and edit Game Pass / Microsoft Store (Xbox container) saves.");
        cmd.Subcommands.Add(BuildList(quiet));
        cmd.Subcommands.Add(BuildExtract(quiet));
        cmd.Subcommands.Add(BuildImport(quiet));
        cmd.Subcommands.Add(BuildDiscover(quiet));
        cmd.Subcommands.Add(BuildRepair(quiet));
        cmd.Subcommands.Add(BuildSnapshot(quiet));
        cmd.Subcommands.Add(BuildCompare(quiet));
        cmd.Subcommands.Add(BuildToGamePass(quiet));
        cmd.Subcommands.Add(BuildToSteam(quiet));
        return cmd;
    }

    private static readonly System.Text.Json.JsonSerializerOptions SnapshotJsonOptions = new() { WriteIndented = true };

    // The real, end-to-end Game Pass cloud-sync test: snapshot the wgs folder, let the actual game /
    // Xbox app run a sync cycle, then compare. The Connected Storage sync can't be invoked from
    // outside the title (it needs the title's SCID + a signed-in Xbox Live account), so observing the
    // before/after of a real sync is the only way to prove an edit survived rather than was reverted.
    private static Command BuildSnapshot(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var outArg = new Argument<string>("out") { Description = "Snapshot file to write (JSON)." };
        var cmd = new Command("snapshot",
            "Fingerprint a Game Pass save folder (per-container generation + blob hash + index timestamp) "
            + "so a later 'gamepass compare' can tell whether a real Xbox sync kept or reverted your edits.");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(outArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var folder = pr.GetValue(folderArg)!;
            if (!GamePassSaveSet.IsGamePassFolder(folder))
            {
                throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {folder}");
            }
            var snap = WgsSnapshot.Capture(folder);
            var json = System.Text.Json.JsonSerializer.Serialize(snap, SnapshotJsonOptions);
            File.WriteAllText(pr.GetValue(outArg)!, json);
            Console.WriteLine($"Snapshotted {snap.Containers.Count} container(s) -> {pr.GetValue(outArg)}");
            Console.WriteLine("Now run your sync cycle (edit, or let the game/Xbox sync), then 'gamepass compare'.");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildCompare(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var snapArg = new Argument<string>("snapshot") { Description = "A snapshot file from 'gamepass snapshot'." };
        var cmd = new Command("compare",
            "Compare a Game Pass save folder against an earlier snapshot and report which containers a "
            + "real Xbox sync dropped, rolled back, or changed - the actual proof of whether edits survive.");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(snapArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var folder = pr.GetValue(folderArg)!;
            if (!GamePassSaveSet.IsGamePassFolder(folder))
            {
                throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {folder}");
            }
            var before = System.Text.Json.JsonSerializer.Deserialize<WgsSnapshot>(File.ReadAllText(pr.GetValue(snapArg)!))
                ?? throw new CliUserErrorException("could not read the snapshot file.");
            var after = WgsSnapshot.Capture(folder);
            var diff = WgsSnapshot.Compare(before, after);
            if (diff.Count == 0)
            {
                Console.WriteLine("No changes - the folder is byte-for-byte the same as the snapshot.");
                return Cli.Ok;
            }
            foreach (var line in diff) Console.WriteLine(line);
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildRepair(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var cmd = new Command("repair",
            "Fix a save whose container points at a data blob missing from disk (an interrupted Xbox "
            + "sync leftover): re-point each manifest at the blob that is actually there. Close the "
            + "game and the Xbox app first.");
        cmd.Arguments.Add(folderArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var repaired = set.RepairMidSync();
            if (repaired.Count == 0)
            {
                Console.WriteLine("Nothing to repair - every container already points at a blob on disk.");
                return Cli.Ok;
            }
            Console.WriteLine($"Repaired {repaired.Count} container(s): {string.Join(", ", repaired)}");
            Console.WriteLine("A backup of the whole save folder was made first (.bak next to it).");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildDiscover(Option<bool> quiet)
    {
        var cmd = new Command("discover", "Find Game Pass save folders installed on this machine.");
        cmd.SetAction(_ => Cli.Run(() =>
        {
            var found = GamePassDiscovery.DiscoverAll();
            if (found.Count == 0)
            {
                Console.WriteLine("No Game Pass saves found.");
                return Cli.Ok;
            }
            foreach (var f in found)
            {
                Console.WriteLine($"{f.AccountId}  {f.FolderPath}  (last modified {f.LastModified:yyyy-MM-dd HH:mm})");
            }
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildList(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };
        var cmd = new Command("list", "List the player/world saves packed in a Game Pass save folder.");
        cmd.Arguments.Add(folderArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var entries = set.Entries();
            if (pr.GetValue(jsonOpt))
            {
                Cli.WriteJson(entries.Select(e => new
                {
                    e.WorldName, e.ContainerName, e.FileName, e.MemberPath,
                    kind = e.Kind.ToString(), e.IsEditable,
                }));
                return Cli.Ok;
            }
            foreach (var world in entries.GroupBy(e => e.WorldName))
            {
                Console.WriteLine($"World: {world.Key}");
                foreach (var e in world)
                {
                    Console.WriteLine($"  [{e.Kind,-13}] {e.FileName}");
                }
            }
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildExtract(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var memberArg = new Argument<string>("member") { Description = "Member file name (e.g. Player_2533... or WorldSave_Facility)." };
        var outArg = new Argument<string>("out") { Description = "Output .sav path." };
        var cmd = new Command("extract", "Write one packed save out as a normal .sav file.");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(memberArg);
        cmd.Arguments.Add(outArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var entry = ResolveEntry(set, pr.GetValue(memberArg));
            var bytes = set.ReadSave(entry);
            var outPath = pr.GetValue(outArg)!;
            File.WriteAllBytes(outPath, bytes);
            Cli.Info(pr.GetValue(quiet), $"Extracted {entry.FileName} -> {outPath} ({bytes.Length} bytes).");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildImport(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var memberArg = new Argument<string>("member") { Description = "Member file name to replace." };
        var inArg = new Argument<string>("in") { Description = "Edited .sav to pack back in." };
        var cmd = new Command("import", "Pack an edited .sav back into the Game Pass save (backs up the folder).");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(memberArg);
        cmd.Arguments.Add(inArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var entry = ResolveEntry(set, pr.GetValue(memberArg));
            var inPath = Cli.RequireFile(pr.GetValue(inArg), "edited save");
            var bytes = File.ReadAllBytes(inPath);
            set.WriteSave(entry, bytes);
            Cli.Info(pr.GetValue(quiet),
                $"Imported {Path.GetFileName(inPath)} -> {entry.FileName} in '{entry.ContainerName}' "
                + "(wgs folder backed up to <folder>.bak).");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildToGamePass(Option<bool> quiet)
    {
        var srcArg = new Argument<string>("steam-world") { Description = "A Steam world folder (WorldSave_*.sav + PlayerData/)." };
        var destArg = new Argument<string>("dest") { Description = "Output folder for the new Game Pass (wgs) container." };
        var worldOpt = new Option<string?>("--world") { Description = "World name to use inside the container (default: the folder name)." };
        var idOpt = new Option<string?>("--player-id") { Description = "Re-home the single player save to this account id (default: keep existing ids)." };
        var cmd = new Command("to-gamepass", "Convert a Steam world folder into a Game Pass / Xbox container save.");
        cmd.Arguments.Add(srcArg);
        cmd.Arguments.Add(destArg);
        cmd.Options.Add(worldOpt);
        cmd.Options.Add(idOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var src = pr.GetValue(srcArg) ?? throw new CliUserErrorException("a Steam world folder is required.");
            if (!Directory.Exists(src)) throw new CliUserErrorException($"folder not found: {src}");
            var dest = pr.GetValue(destArg) ?? throw new CliUserErrorException("a destination folder is required.");
            var outDir = GamePassConverter.SteamWorldToGamePass(src, dest, pr.GetValue(worldOpt), pr.GetValue(idOpt));
            Cli.Info(pr.GetValue(quiet),
                $"Converted Steam world -> Game Pass container at {outDir}. Copy this folder into the game's "
                + "wgs storage (Packages\\<PFN>\\SystemAppData\\wgs) and verify it loads in-game.");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildToSteam(Option<bool> quiet)
    {
        var srcArg = new Argument<string>("wgs-folder") { Description = "A Game Pass wgs container folder." };
        var destArg = new Argument<string>("dest") { Description = "Output Steam world folder (loose .sav files)." };
        var containerOpt = new Option<string?>("--container") { Description = "Which <World>-WC container to convert (default: the first)." };
        var idOpt = new Option<string?>("--player-id") { Description = "Re-home the single player save to this SteamID64 (default: keep existing ids)." };
        var cmd = new Command("to-steam", "Convert a Game Pass / Xbox container save into a Steam world folder.");
        cmd.Arguments.Add(srcArg);
        cmd.Arguments.Add(destArg);
        cmd.Options.Add(containerOpt);
        cmd.Options.Add(idOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var src = pr.GetValue(srcArg);
            if (src is null || !GamePassSaveSet.IsGamePassFolder(src))
            {
                throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {src}");
            }
            var dest = pr.GetValue(destArg) ?? throw new CliUserErrorException("a destination folder is required.");
            var outDir = GamePassConverter.GamePassToSteamWorld(src, pr.GetValue(containerOpt), dest, pr.GetValue(idOpt));
            Cli.Info(pr.GetValue(quiet),
                $"Converted Game Pass container -> Steam world folder at {outDir}. Place it under "
                + "%LOCALAPPDATA%\\AbioticFactor\\Saved\\SaveGames\\<steamid>\\Worlds\\.");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static GamePassSaveSet OpenSet(string? folder)
    {
        var dir = folder ?? throw new CliUserErrorException("a wgs folder path is required.");
        if (!Directory.Exists(dir))
        {
            throw new CliUserErrorException($"folder not found: {dir}");
        }
        if (!GamePassSaveSet.IsGamePassFolder(dir))
        {
            throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {dir}");
        }
        return GamePassSaveSet.Open(dir);
    }

    private static GamePassSaveEntry ResolveEntry(GamePassSaveSet set, string? member)
    {
        var needle = member ?? throw new CliUserErrorException("a member name is required.");
        var entries = set.Entries().Where(e => e.IsEditable).ToList();
        var match = entries.FirstOrDefault(e =>
                        string.Equals(e.FileName, needle, StringComparison.OrdinalIgnoreCase))
                    ?? entries.FirstOrDefault(e =>
                        e.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new CliUserErrorException(
            $"no editable member matching '{needle}'. Use 'gamepass list' to see members.");
    }
}
