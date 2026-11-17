using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Moving bases with a running game (live editing): asking the game to save and waiting for it,
/// finding the running world's save, lifting pieces by their save keys, moving them within the
/// world, and bringing a base in (placed piece by piece, refilled, all or nothing).
/// </summary>
public sealed class LiveBaseMoverTests
{
    private sealed class Game : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public List<(string Command, JsonElement Payload)> Calls { get; } = [];
        public int SavingPolls { get; set; } = 2;
        public int FailSpawnAt { get; set; } = -1;
        private int _spawns;

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var element = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            Calls.Add((command, element));
            object? result = command switch
            {
                "world.save" => new { started = true, saving = true, worldName = "Cascade" },
                "world.saving" => new { saving = SavingPolls-- > 0, worldName = "Cascade" },
                "bases.spawn" => Spawn(),
                "bases.remove" => new { removed = element.GetProperty("ids").GetArrayLength() },
                "bases.move" or "bases.set" or "containers.set" or "containers.setfull" or "containers.setcomplete" => null,
                _ => throw new LiveAgentException($"unknown command '{command}'"),
            };
            var json = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(json.ValueKind == JsonValueKind.Null ? default! : json.Deserialize<TResponse>(JsonOptions)!);
        }

        private object Spawn()
        {
            if (_spawns++ == FailSpawnAt) throw new LiveAgentException("this kind of object could not be loaded in the running game");
            return new { id = "Spawned_" + _spawns };
        }

        public int Count(string command) => Calls.Count(c => c.Command == command);
    }

    private sealed class TempWorld : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "abf-live-move-" + Guid.NewGuid().ToString("N"));
        public string Folder => Path.Combine(Root, "Cascade");

        public TempWorld()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, "WorldSave_Facility.sav"), OriginalBytes);
        }

        public IReadOnlyList<DiscoveredWorld> Discover() => [new DiscoveredWorld(Folder, "Cascade", DiscoveredWorldSource.Client, null)];

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }

    private static LiveBaseMover Mover(Game game, TempWorld world)
        => new(game, world.Discover) { PollInterval = TimeSpan.FromMilliseconds(1) };

    private static List<LiveDeployable> LivePieces(int count)
    {
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var keys = BaseDetector.Detect(data.Deployables).OrderByDescending(b => b.Deployables.Count).First()
            .Deployables.Select(d => d.Id).Where(built.Contains).Take(count).ToList();
        return keys.Select((k, i) => new LiveDeployable("Live_" + i, "Deployed_X_C", 0, 0, 0, null, false, 0, false, [],
            DeployedByPlayer: true, AssetId: k, Region: "Facility")).ToList();
    }

    [Fact]
    public async Task Saving_waits_until_the_game_has_finished()
    {
        if (!HasServerFacility) return;
        using var world = new TempWorld();
        var game = new Game { SavingPolls = 3 };

        var name = await Mover(game, world).SaveWorldAsync();

        Assert.Equal("Cascade", name);
        Assert.Equal(1, game.Count("world.save"));
        Assert.Equal(4, game.Count("world.saving"));
    }

    [Fact]
    public async Task Lifting_reads_the_pieces_from_the_fresh_save_by_their_save_keys()
    {
        if (!HasServerFacility) return;
        using var world = new TempWorld();
        var game = new Game();
        var pieces = LivePieces(5);
        pieces.Add(new LiveDeployable("Live_unsaved", "Deployed_X_C", 0, 0, 0, null, false, 0, false, [], DeployedByPlayer: true, AssetId: "NOT-A-KEY"));

        var lift = await Mover(game, world).LiftAsync(pieces);

        Assert.Equal(5, lift.Package.Pieces.Count);
        Assert.Equal(["Live_unsaved"], lift.NotSaved);
        Assert.Equal("Live_0", lift.LiveIdByKey[pieces[0].AssetId!]);
        Assert.Equal(Path.GetFullPath(world.Folder), Path.GetFullPath(lift.World.Folder));
        Assert.Equal(1, game.Count("world.save"));
    }

    [Fact]
    public async Task Moving_within_the_running_world_moves_each_live_piece_and_saves()
    {
        if (!HasServerFacility) return;
        using var world = new TempWorld();
        var game = new Game();
        var mover = Mover(game, world);
        var lift = await mover.LiftAsync(LivePieces(4));
        var before = LiveBaseMover.Placements(lift.Package, new PlacedVector(0, 0, 0), 0);

        await mover.MoveWithinAsync(lift, new PlacedVector(1000, -500, 0), 0);

        var moves = game.Calls.Where(c => c.Command == "bases.move").ToList();
        Assert.Equal(4, moves.Count);
        foreach (var move in moves)
        {
            var key = lift.LiveIdByKey.Single(kv => kv.Value == move.Payload.GetProperty("id").GetString()).Key;
            Assert.Equal(before[key].Translation!.Value.X + 1000, move.Payload.GetProperty("x").GetDouble(), 3);
            Assert.Equal(before[key].Translation!.Value.Y - 500, move.Payload.GetProperty("y").GetDouble(), 3);
        }
        Assert.Equal(2, game.Count("world.save"));
    }

    [Fact]
    public async Task Bringing_a_base_in_places_every_piece_refills_it_and_saves()
    {
        if (!HasServerFacility) return;
        using var world = new TempWorld();
        var data = Load();
        var keys = PlayerBuilt(data).Where(o => o.StoredItemCount > 0).Take(3).Select(o => o.Key).ToList();
        var package = BasePackage.Create(data, keys, "Other", "WorldSave_Facility.sav", out _);
        var game = new Game();

        var result = await Mover(game, world).BringAsync(package, new PlacedVector(0, 0, 0), 0, null, null);

        Assert.Equal(3, result.Placed.Count);
        Assert.Equal(3, game.Count("bases.spawn"));
        Assert.Contains(game.Calls, c => c.Command.StartsWith("containers.set", StringComparison.Ordinal));
        Assert.All(game.Calls.Where(c => c.Command == "bases.spawn"), c => Assert.StartsWith("/Game/", c.Payload.GetProperty("classPath").GetString()));
        Assert.Equal(1, game.Count("world.save"));
    }

    [Fact]
    public async Task A_piece_that_cannot_be_placed_takes_the_whole_bring_back()
    {
        if (!HasServerFacility) return;
        using var world = new TempWorld();
        var data = Load();
        var keys = PlayerBuilt(data).Take(4).Select(o => o.Key).ToList();
        var package = BasePackage.Create(data, keys, "Other", "WorldSave_Facility.sav", out _);
        var game = new Game { FailSpawnAt = 2 };

        await Assert.ThrowsAsync<LiveAgentException>(() => Mover(game, world).BringAsync(package, new PlacedVector(0, 0, 0), 0, null, null));

        var removal = Assert.Single(game.Calls, c => c.Command == "bases.remove");
        Assert.Equal(2, removal.Payload.GetProperty("ids").GetArrayLength());
        Assert.Equal(0, game.Count("world.save"));
    }

    [Fact]
    public void Placements_turn_the_base_about_its_middle_and_take_piece_heights()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var keys = BaseDetector.Detect(data.Deployables).OrderByDescending(b => b.Deployables.Count).First()
            .Deployables.Select(d => d.Id).Where(PlayerBuilt(data).Select(o => o.Key).ToHashSet().Contains).Take(6).ToList();
        var package = BasePackage.Create(data, keys, "Cascade", "WorldSave_Facility.sav", out _);
        var centre = package.Centre!.Value;

        var turned = LiveBaseMover.Placements(package, new PlacedVector(0, 0, 0), 180, new Dictionary<string, double> { [keys[0]] = 4321 });
        var flat = LiveBaseMover.Placements(package, new PlacedVector(0, 0, 0), 0);

        foreach (var key in keys.Skip(1))
        {
            var a = flat[key].Translation!.Value;
            var b = turned[key].Translation!.Value;
            Assert.Equal(2 * centre.X - a.X, b.X, 3);
            Assert.Equal(2 * centre.Y - a.Y, b.Y, 3);
        }
        Assert.Equal(4321, turned[keys[0]].Translation!.Value.Z);
    }
}
