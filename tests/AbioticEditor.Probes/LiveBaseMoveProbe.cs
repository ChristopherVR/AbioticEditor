using System.IO;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

/// <summary>
/// In-game check of live base moving (round for GitHub issue 69): with Abiotic Factor running a
/// throwaway world and the live helper started, this connects the way the editor does and runs the
/// live moves one after another, logging what the running game reports back. Runs only when
/// LIVE_MOVE_PROBE names a log file; LIVE_MOVE_TARGET names the other world's area save to move
/// out to and bring back from (a throwaway copy, never a real world).
/// </summary>
public class LiveBaseMoveProbe
{
    [Fact]
    public async Task Move_a_base_with_the_game_running()
    {
        var logPath = Environment.GetEnvironmentVariable("LIVE_MOVE_PROBE");
        var target = Environment.GetEnvironmentVariable("LIVE_MOVE_TARGET");
        if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(target)) return;
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        void Log(string line) => log.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditorLiveAgent");
        var port = int.Parse(File.ReadAllText(Path.Combine(dir, "port.txt")).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var token = File.ReadAllText(Path.Combine(dir, "token.txt")).Trim();
        await using var channel = new TcpLiveGameChannel();
        await channel.ConnectAsync(new LiveConnectionInfo("127.0.0.1", port, token));
        var bases = new LiveBasesChannel(channel);
        var mover = new LiveBaseMover(channel);

        // 1. Save now, and find the running world on disk.
        var running = await mover.SaveAndReadAsync();
        Log($"saved: world {running.WorldName}, file {running.File}, written {File.GetLastWriteTime(running.File):HH:mm:ss}");

        // 2. The base to move: the "Hot Stuff" group's main floor, as the Bases tab offers it.
        var worldBase = BaseDetector.Detect(running.Data.Deployables).First(b => b.Deployables.Any(d => d.CustomName == "Hot Stuff"));
        var keys = BaseFloors.MainFloorIds(worldBase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var live = (await bases.GetAsync()).Deployables;
        var pieces = live.Where(d => d.AssetId is { } k && keys.Contains(k)).ToList();
        Log($"base: {keys.Count} saved pieces on its main floor, {pieces.Count} live pieces matched by save id");
        Assert.NotEmpty(pieces);

        // 3. Move within the running world to the Office Sector.
        var lift = await mover.LiftAsync(pieces);
        var office = BaseDestinations.For(lift.Package, running.Data).First(d => d.Name == "The Office Sector");
        await mover.MoveWithinAsync(lift, office.OffsetFor(lift.Package), 0);
        live = (await bases.GetAsync()).Deployables;
        var moved = live.Where(d => lift.LiveIdByKey.Values.Contains(d.Id)).ToList();
        Log($"moved within: {moved.Count} live pieces now near ({office.Point.X:0}, {office.Point.Y:0}); first at ({moved.FirstOrDefault()?.X:0}, {moved.FirstOrDefault()?.Y:0}, {moved.FirstOrDefault()?.Z:0}); stored items live {moved.Sum(d => d.StoredItemCount)}");
        var afterSave = mover.FindRunningWorld(running.WorldName);
        Log($"  in the game-saved file: {keys.Count(k => WorldMapAccessor.FindEntry(afterSave.Data.Raw, "DeployedObjectMap", k) is { } p && PlacedObjectCensus.ReadTransform(p)?.Translation is { } t && Math.Abs(t.X - office.Point.X) < 6000 && Math.Abs(t.Y - office.Point.Y) < 6000)} of {keys.Count} pieces are at the new place");

        // 4. Move out to the other world: write it there first, then remove here.
        var outLift = await mover.LiftAsync(moved);
        var targetData = WorldSaveReader.ReadFromFile(target);
        var edits = new StagedBaseEdits();
        var copy = edits.StageTransplant(outLift.Package, new PlacedVector(0, 0, 0));
        var applied = edits.ApplyTo(targetData);
        Log($"move out: transplant into {Path.GetFileName(Path.GetDirectoryName(target))} applied={applied.Applied} created={applied.Created.Count} blocking=[{string.Join(" | ", applied.Issues.Where(i => i.IsBlocking).Select(i => i.Message).Distinct())}]");
        Assert.True(applied.Applied);
        WorldSaveWriter.WriteToFile(targetData, target);
        var removed = await mover.RemoveAsync(outLift.LiveIdByKey.Values.ToList());
        live = (await bases.GetAsync()).Deployables;
        Log($"  removed live: {removed}; still live: {live.Count(d => outLift.LiveIdByKey.Values.Contains(d.Id))}");
        var afterOut = mover.FindRunningWorld(running.WorldName);
        Log($"  running world's saved file still holds {keys.Count(k => WorldMapAccessor.FindEntry(afterOut.Data.Raw, "DeployedObjectMap", k) is not null)} of the moved pieces; target holds {copy.NewKeys.Values.Count(k => WorldMapAccessor.FindEntry(WorldSaveReader.ReadFromFile(target).Raw, "DeployedObjectMap", k) is not null)} new pieces");

        // 5. Bring it back in from the other world, with what the crates held.
        var back = BasePackage.Create(WorldSaveReader.ReadFromFile(target), copy.NewKeys.Values, "Target", "WorldSave_Facility.sav", out _);
        var donors = live.Where(d => d.DeployedByPlayer && d.ClassPath is { Length: > 0 }).GroupBy(d => d.ClassPath!).ToDictionary(g => g.Key, g => g.First().Id);
        var result = await mover.BringAsync(back, new PlacedVector(0, 0, 0), 0, null, donors);
        live = (await bases.GetAsync()).Deployables;
        var brought = live.Where(d => result.Placed.Values.Contains(d.Id)).ToList();
        Log($"bring in: placed {result.Placed.Count} of {back.Pieces.Count}; live now {brought.Count}; stored items live {brought.Sum(d => d.StoredItemCount)} (package {back.Pieces.Count(p => p.StoredItems > 0)} pieces held items, {back.StoredItems} items)");
        foreach (var note in result.Notes) Log($"  note: {note}");
        foreach (var d in brought.OrderBy(d => d.Z)) Log($"  {d.ClassName} ({d.X:0}, {d.Y:0}, {d.Z:0}) items {d.StoredItemCount} name {d.CustomName}");
        var afterIn = mover.FindRunningWorld(running.WorldName);
        Log($"  running world's saved file holds {brought.Count(d => d.AssetId is { } k && WorldMapAccessor.FindEntry(afterIn.Data.Raw, "DeployedObjectMap", k) is not null)} of {brought.Count} brought pieces");
    }
}
