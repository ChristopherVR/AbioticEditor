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
        cmd.Subcommands.Add(BuildRenamePlayer(quiet));
        cmd.Subcommands.Add(BuildStatus(quiet));
        cmd.Subcommands.Add(BuildBackups(quiet));
        cmd.Subcommands.Add(BuildRestoreBackup(quiet));
        cmd.Subcommands.Add(BuildOrphans(quiet));
        cmd.Subcommands.Add(BuildRecoverOrphan(quiet));
        return cmd;
    }

    /// <summary>
    /// The option every command that writes into a real Xbox save carries. Without it a save that
    /// Xbox is arguing with itself about, or one the running game still owns, is refused rather
    /// than written into.
    /// </summary>
    private static Option<bool> ForceOption() => new("--force")
    {
        Description = "Save anyway when the checks say Xbox may throw the change away "
            + "(the game running, an unsettled cloud conflict). Last resort.",
    };

    /// <summary>
    /// Prints what the safety check found and refuses the write unless <c>--force</c> was passed.
    /// Runs before anything is backed up or written, so a refusal leaves the save exactly as it was.
    /// </summary>
    private static void RequireWritable(GamePassSaveSet set, bool force)
    {
        var check = set.CheckWritable();
        foreach (var warning in check.Warnings)
        {
            Cli.Warn(warning.Message);
        }
        if (check.CanWrite) return;
        if (force)
        {
            set.AllowUnsafeWrites(GamePassWriteOverride.AcceptRiskOfLosingThisSave(
                "--force was passed on the command line"));
            Cli.Warn($"{check.BlockingMessage()} Saving anyway because --force was passed.");
            return;
        }
        throw new CliUserErrorException(
            $"{check.BlockingMessage()} Pass --force to save anyway.");
    }

    // Read-only. The whole point is to be able to look at a save that is misbehaving without
    // touching it, since touching it is what tends to make these problems worse.
    private static Command BuildStatus(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var cmd = new Command("status",
            "Show how Xbox currently sees this save: whether it has an unresolved cloud conflict, what "
            + "state each part of it is in, what is running, and what spare copies exist. Changes nothing.");
        cmd.Arguments.Add(folderArg);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var dir = pr.GetValue(folderArg) ?? throw new CliUserErrorException("a wgs folder path is required.");
            if (!GamePassSaveSet.IsGamePassFolder(dir))
            {
                throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {dir}");
            }
            var store = WgsContainerStore.Open(dir);

            Console.WriteLine($"Sync state: {store.SyncState}");
            if (store.HasUnresolvedConflicts)
            {
                Console.WriteLine(
                    "  WARNING: Xbox has a conflict for this save that it has not settled. Launch the game "
                    + "and let it load and save this world before editing, or the next sync may discard "
                    + "whatever you change.");
            }

            var invalid = store.InvalidStateContainers;
            if (invalid.Count > 0)
            {
                Console.WriteLine(
                    $"  WARNING: {invalid.Count} part(s) of this save carry a state Xbox does not define "
                    + $"({string.Join(", ", invalid)}). An older version of this editor wrote those. "
                    + "Run 'gamepass repair' to put them back.");
            }

            var scan = GamePassEnvironment.Scan();
            Console.WriteLine();
            Console.WriteLine(scan.Found.Count == 0
                ? scan.Unknown
                    ? "Running now: could not read the process list, so this is unknown."
                    : "Running now: nothing that touches Xbox saves."
                : $"Running now: {string.Join(", ", scan.Found.Select(p => $"{p.Name} ({p.Role})"))}");

            // The same verdict every write path uses, so 'status' answers "can I edit this right
            // now" rather than leaving the player to work it out from the fields above.
            var check = store.CheckWritable();
            Console.WriteLine();
            Console.WriteLine(check.CanWrite
                ? "Editing: nothing is blocking a change right now."
                : "Editing: BLOCKED. Pass --force on a write command to go ahead anyway.");
            foreach (var line in check.Lines())
            {
                Console.WriteLine($"  - {line}");
            }

            Console.WriteLine();
            Console.WriteLine($"{"Container",-34}{"Number",-8}{"State",-10}Size");
            foreach (var c in store.Containers)
            {
                var state = c.HasInvalidState ? $"?{c.RawState}" : c.State.ToString();
                Console.WriteLine($"{c.Name,-34}{c.ContainerNumber,-8}{state,-10}{c.BlobSize}");
            }

            var set = GamePassSaveSet.Open(dir);
            var backups = set.WorldBackups();
            Console.WriteLine();
            if (backups.Count == 0)
            {
                Console.WriteLine("Spare copies the game keeps: none in this save.");
            }
            else
            {
                Console.WriteLine("Spare copies the game keeps (use 'gamepass restore-backup' to put one back):");
                foreach (var b in backups)
                {
                    Console.WriteLine(
                        $"  {b.WorldName,-24}{b.BlobSize,12} bytes  last saved {b.LastSavedUtc:yyyy-MM-dd HH:mm} UTC"
                        + (b.LiveWorldExists ? string.Empty : "  (the live world is GONE - this copy is all that is left)"));
                }
            }

            var orphans = set.OrphanedContainers();
            Console.WriteLine();
            if (orphans.Count == 0)
            {
                Console.WriteLine("Leftover save data not in the container list: none.");
            }
            else
            {
                Console.WriteLine("Leftover save data not in the container list "
                    + "(use 'gamepass recover-orphan' to put one back):");
                foreach (var o in orphans)
                {
                    Console.WriteLine($"  {o.FolderName}  {o.WorldName ?? "unknown world",-24}{o.BlobSize,12} bytes");
                }
            }
            return Cli.Ok;
        }));
        return cmd;
    }

    // Read-only, like 'status': a player looking for an intact copy of a broken world should be
    // able to see what is there without anything being written on their behalf.
    private static Command BuildBackups(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };
        var cmd = new Command("backups",
            "List the spare copies of each world that the game itself keeps (the '-WC-B' containers). "
            + "Each one is a complete world, one save behind the live copy. Changes nothing.");
        cmd.Arguments.Add(folderArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var backups = set.WorldBackups();
            if (pr.GetValue(jsonOpt))
            {
                Cli.WriteJson(backups);
                return Cli.Ok;
            }
            if (backups.Count == 0)
            {
                Console.WriteLine("This save has no spare copies (the game writes one per world once it has saved twice).");
                return Cli.Ok;
            }
            foreach (var b in backups)
            {
                Console.WriteLine($"{b.WorldName,-24}{b.ContainerName,-28}{b.BlobSize,12} bytes  "
                    + $"last saved {b.LastSavedUtc:yyyy-MM-dd HH:mm} UTC"
                    + (b.LiveWorldExists ? string.Empty : "  (the live world is GONE)"));
            }
            return Cli.Ok;
        }));
        return cmd;
    }

    // Recovery, not editing: this throws away the live world and puts an older one in its place.
    private static Command BuildRestoreBackup(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var worldArg = new Argument<string>("world") { Description = "The world to restore (e.g. ForScience)." };
        var forceOpt = ForceOption();
        var cmd = new Command("restore-backup",
            "Put the game's own spare copy of a world back over the live one, for a world that has "
            + "broken. Everything since that copy was made is lost, so the whole save folder is backed "
            + "up first. Close the game and the Xbox app.");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(worldArg);
        cmd.Options.Add(forceOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var needle = pr.GetValue(worldArg) ?? throw new CliUserErrorException("a world name is required.");
            var backups = set.WorldBackups();
            var match = backups.FirstOrDefault(b =>
                            b.WorldName.Equals(needle, StringComparison.OrdinalIgnoreCase)
                            || b.ContainerName.Equals(needle, StringComparison.OrdinalIgnoreCase))
                        ?? throw new CliUserErrorException(
                            $"no spare copy for '{needle}'. Use 'gamepass backups' to see what is there.");

            RequireWritable(set, pr.GetValue(forceOpt));
            var world = set.RestoreWorldFromBackup(match.ContainerName);
            Cli.Info(pr.GetValue(quiet),
                $"Restored '{world}' from the copy the game saved on {match.LastSavedUtc:yyyy-MM-dd HH:mm} UTC. "
                + "The whole save folder was backed up first (.bak next to it). Launch the game to check it loads.");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildOrphans(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };
        var cmd = new Command("orphans",
            "List save data still on disk that the container list no longer points at - what Xbox "
            + "leaves behind when it drops a world from a save. Changes nothing.");
        cmd.Arguments.Add(folderArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var folder = pr.GetValue(folderArg) ?? throw new CliUserErrorException("a wgs folder path is required.");
            if (!GamePassSaveSet.IsGamePassFolder(folder))
            {
                throw new CliUserErrorException($"not a Game Pass save folder (no containers.index): {folder}");
            }
            var orphans = WgsContainerStore.FindOrphanedContainers(folder);
            if (pr.GetValue(jsonOpt))
            {
                Cli.WriteJson(orphans);
                return Cli.Ok;
            }
            if (orphans.Count == 0)
            {
                Console.WriteLine("Nothing left over - every folder of save data is in the container list.");
                return Cli.Ok;
            }
            Console.WriteLine($"{"Folder",-36}{"Gen",-6}{"World",-24}{"Size",12}  Last written");
            foreach (var o in orphans)
            {
                Console.WriteLine($"{o.FolderName,-36}{o.ContainerNumber,-6}{o.WorldName ?? "unknown",-24}{o.BlobSize,12}"
                    + $"  {o.LastWrittenUtc:yyyy-MM-dd HH:mm} UTC");
            }
            Console.WriteLine();
            Console.WriteLine("Put one back with 'gamepass recover-orphan <wgs-folder> <folder>'.");
            return Cli.Ok;
        }));
        return cmd;
    }

    private static Command BuildRecoverOrphan(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var orphanArg = new Argument<string>("folder") { Description = "The leftover folder from 'gamepass orphans' (the start of it is enough)." };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "The world container name to put it back as (default: the world name in the data, plus '-WC').",
        };
        var forceOpt = ForceOption();
        var cmd = new Command("recover-orphan",
            "Put leftover save data back into the container list so the game can see that world again. "
            + "Only the list changes; the save data stays where it is. Backs up the save folder first.");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(orphanArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(forceOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var needle = pr.GetValue(orphanArg) ?? throw new CliUserErrorException("a leftover folder name is required.");
            var orphans = set.OrphanedContainers();
            var match = orphans.FirstOrDefault(o =>
                            o.FolderName.Equals(needle, StringComparison.OrdinalIgnoreCase))
                        ?? orphans.FirstOrDefault(o =>
                            o.FolderName.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                        ?? throw new CliUserErrorException(
                            $"no leftover save data matching '{needle}'. Use 'gamepass orphans' to see what is there.");

            RequireWritable(set, pr.GetValue(forceOpt));
            var name = set.RecoverOrphanedWorld(match, pr.GetValue(nameOpt));
            Cli.Info(pr.GetValue(quiet),
                $"Put '{match.FolderName}' back as '{name}'. The save folder was backed up first (.bak next to "
                + "it). Launch the game to check the world is there.");
            return Cli.Ok;
        }));
        return cmd;
    }

    // Renaming a packed player has to go through the container: the bundle carries its own list of
    // member names, so extracting, running 'steamid', and importing again puts the edited save back
    // under the OLD name and the old id quietly returns.
    private static Command BuildRenamePlayer(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("wgs-folder") { Description = "Path to the wgs container folder." };
        var memberArg = new Argument<string>("member") { Description = "The player save to re-home (e.g. Player_2533...)." };
        var idArg = new Argument<string>("new-id") { Description = "The account id to give it." };
        var forceOpt = ForceOption();
        var cmd = new Command("rename-player",
            "Re-home a packed player save to another account id, rewriting both its name and the owner "
            + "id stored inside it (backs up the folder).");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(memberArg);
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(forceOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var entry = ResolveEntry(set, pr.GetValue(memberArg));
            if (entry.Kind != GamePassSaveKind.Player)
            {
                throw new CliUserErrorException($"'{entry.FileName}' is not a player save.");
            }
            var newId = pr.GetValue(idArg) ?? throw new CliUserErrorException("a new account id is required.");
            RequireWritable(set, pr.GetValue(forceOpt));
            var renamed = set.RenamePlayerToAccount(entry, newId);
            Cli.Info(pr.GetValue(quiet),
                $"Re-homed {entry.FileName} -> {renamed} in '{entry.ContainerName}' "
                + "(wgs folder backed up to <folder>.bak).");
            return Cli.Ok;
        }));
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
        var forceOpt = ForceOption();
        var cmd = new Command("import", "Pack an edited .sav back into the Game Pass save (backs up the folder).");
        cmd.Arguments.Add(folderArg);
        cmd.Arguments.Add(memberArg);
        cmd.Arguments.Add(inArg);
        cmd.Options.Add(forceOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var set = OpenSet(pr.GetValue(folderArg));
            var entry = ResolveEntry(set, pr.GetValue(memberArg));
            var inPath = Cli.RequireFile(pr.GetValue(inArg), "edited save");
            var bytes = File.ReadAllBytes(inPath);
            RequireWritable(set, pr.GetValue(forceOpt));
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
        var intoOpt = new Option<bool>("--into")
        {
            Description = "Add the world to the Xbox save folder already at <dest>, keeping the saves in it "
                + "(default: refuse, because a fresh container list would orphan them).",
        };
        var cmd = new Command("to-gamepass", "Convert a Steam world folder into a Game Pass / Xbox container save.");
        cmd.Arguments.Add(srcArg);
        cmd.Arguments.Add(destArg);
        cmd.Options.Add(worldOpt);
        cmd.Options.Add(idOpt);
        cmd.Options.Add(intoOpt);
        cmd.SetAction(pr => Cli.Run(() =>
        {
            var src = pr.GetValue(srcArg) ?? throw new CliUserErrorException("a Steam world folder is required.");
            if (!Directory.Exists(src)) throw new CliUserErrorException($"folder not found: {src}");
            var dest = pr.GetValue(destArg) ?? throw new CliUserErrorException("a destination folder is required.");
            var into = pr.GetValue(intoOpt);
            if (into && !GamePassSaveSet.IsGamePassFolder(dest))
            {
                throw new CliUserErrorException($"--into needs an existing Xbox save folder, and {dest} is not one.");
            }
            var outDir = GamePassConverter.SteamWorldToGamePass(
                src, dest, pr.GetValue(worldOpt), pr.GetValue(idOpt), mergeIntoExisting: into);
            Cli.Info(pr.GetValue(quiet), into
                ? $"Added the world to the Xbox save folder at {outDir}. Launch the game offline to check it loads."
                : $"Converted Steam world -> Game Pass container at {outDir}. This is a save folder of its own: "
                    + "to put it in the game, run this again with --into pointing at your real Xbox save folder "
                    + "(find it with 'gamepass discover'), with the game and the Xbox app closed.");
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
