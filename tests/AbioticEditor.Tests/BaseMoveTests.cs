using UeSaveGame.PropertyTypes;
using UeSaveGame;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Moving a base between worlds (GitHub issue 69): a group of player-built pieces is lifted into a
/// <see cref="BasePackage"/> (in memory, or as a file), put down in another world with everything
/// stored inside, and removed from the world it left. Proved against the dedicated-server Facility
/// save; a second fresh load of the same save stands in for "another world of the same area".
/// </summary>
public sealed class BaseMoveTests
{
    /// <summary>The largest detected base: the pieces a player would move.</summary>
    private static List<string> BiggestBase(WorldSaveData data)
    {
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var bases = BaseDetector.Detect(data.Deployables);
        return bases.OrderByDescending(b => b.Deployables.Count).First()
            .Deployables.Select(d => d.Id).Where(built.Contains).ToList();
    }

    [Fact]
    public void A_package_carries_only_player_built_pieces_and_round_trips_through_a_file()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var level = PlacedObjectCensus.Build(source).Objects!.First(o => o.DeployedByPlayer != true).Key;

        var package = BasePackage.Create(source, keys.Append(level).Append("not-a-key"), "Cascade", "WorldSave_Facility.sav", out var skipped);

        Assert.Equal(keys.Count, package.Pieces.Count);
        Assert.Contains(skipped, s => s.Key == level && s.Reason == "level-placed");
        Assert.Contains(skipped, s => s.Key == "not-a-key" && s.Reason == "not-in-save");
        Assert.NotEmpty(package.LevelPackages);
        Assert.EndsWith(BasePackage.FileExtension, package.SuggestedFileName(), StringComparison.Ordinal);

        var json = package.ToJson();
        var back = BasePackage.Parse(json);
        Assert.Equal(package.Pieces.Select(p => p.Key).Order(), back.Pieces.Select(p => p.Key).Order());
        Assert.Equal(package.StoredItems, back.StoredItems);
        Assert.Equal("Cascade", back.SourceWorld);
        Assert.Equal("WorldSave_Facility.sav", back.AreaFile);
        // The file is the carried part of the save, compressed (about 550 KB for this 505-piece base), not the whole ~16 MB area.
        Assert.True(json.Length < OriginalBytes.Length / 10, $"package is {json.Length} bytes, save {OriginalBytes.Length}, pieces {package.Pieces.Count} of {PlayerBuilt(source).Count()}");
    }

    [Fact]
    public void A_file_that_is_not_a_base_is_refused_with_a_plain_reason()
    {
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse("{\"hello\":1}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse("not json"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => BasePackage.Parse(
            "{\"format\":\"abiotic-editor-base\",\"version\":1,\"save\":\"!!!\"}"u8.ToArray()));
    }

    [Fact]
    public void Putting_a_package_down_adds_every_piece_with_its_contents_and_leaves_the_rest_alone()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var package = BasePackage.Parse(BasePackage.Create(source, keys, "Cascade", "WorldSave_Facility.sav", out _).ToJson());

        var target = Load();
        Assert.False(package.IsOtherMap(target));
        var before = Fingerprint(target.Raw);
        var edits = new StagedBaseEdits(Counter('A'));
        var staged = edits.StageTransplant(package, new PlacedVector(5000, -3000, 0), 90);
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Equal(keys.Count, preview.Duplications.Count);
        Assert.Equal(package.StoredItems, preview.ItemsCopied);
        Assert.DoesNotContain(preview.Issues, i => i.Code == "contents-copied");

        var result = edits.ApplyTo(target);
        Assert.True(result.Applied);
        Assert.Equal(keys.Count, result.Created.Count);

        // Exactly the new pieces (and their outlets) were added; nothing that was there changed.
        var reloaded = WorldSaveReader.ReadFromStream(new MemoryStream(Serialize(target)));
        var after = Fingerprint(reloaded.Raw);
        foreach (var (map, entries) in before)
        {
            foreach (var (key, value) in entries)
            {
                Assert.True(after[map].TryGetValue(key, out var now), $"{map}[{key}] went missing");
                if (key != "(value)") Assert.Equal(value, now);
            }
        }
        var newKeys = staged.NewKeys.Values.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(newKeys, after["DeployedObjectMap"].Keys.Except(before["DeployedObjectMap"].Keys).ToHashSet(StringComparer.Ordinal));
        var stored = newKeys.Sum(k => PlacedObjectContents.ReadItems(WorldMapAccessor.FindEntry(reloaded.Raw, "DeployedObjectMap", k)!).Sum(i => Math.Max(i.Count, 1)));
        Assert.Equal(package.StoredItems, stored);
    }

    [Fact]
    public void Restaging_moves_the_landing_spot_and_keeps_the_new_ids()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source).Take(4).ToList();
        var package = BasePackage.Create(source, keys, "Cascade", "WorldSave_Facility.sav", out _);
        var edits = new StagedBaseEdits(Counter('B'));
        var staged = edits.StageTransplant(package, new PlacedVector(0, 0, 0));
        var first = edits.Preview(Load()).Duplications.ToDictionary(r => r.NewKey, r => r.After!.Translation!.Value);

        var moved = edits.RestageDuplication(staged.Id, new PlacedVector(100, 200, 30), 0)!;

        Assert.Equal(staged.NewKeys, moved.NewKeys);
        foreach (var row in edits.Preview(Load()).Duplications)
        {
            var was = first[row.NewKey];
            var now = row.After!.Translation!.Value;
            Assert.Equal(was.X + 100, now.X, 3);
            Assert.Equal(was.Y + 200, now.Y, 3);
            Assert.Equal(was.Z + 30, now.Z, 3);
        }
        Assert.Null(edits.RestageDuplication(9999, new PlacedVector(0, 0, 0), 0));
    }

    [Fact]
    public void The_moved_pieces_can_be_removed_from_the_world_they_left()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source);
        var edits = new StagedBaseEdits();
        edits.StageDelete(keys, DeletePolicy.MovedAway);
        var preview = edits.Preview(source);
        // The fixture's biggest base (claimed beds, power, teleporters) moves without anything blocking.
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        var result = edits.ApplyTo(source);
        Assert.True(result.Applied);
        Assert.Equal(keys.Count, result.Deleted.Count);
        Assert.All(keys, k => Assert.Null(WorldMapAccessor.FindEntry(source.Raw, "DeployedObjectMap", k)));
    }

    /// <summary>Pretends <paramref name="save"/> is another map: every actor path moves to a made-up level.</summary>
    private static void MoveToOtherMap(WorldSaveData save, string package)
    {
        foreach (var e in WorldMapAccessor.Entries(save.Raw, "DeployedObjectMap"))
        {
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is { SubPath: { } sub })
            {
                var asset = package[(package.LastIndexOf('/') + 1)..];
                WorldMapAccessor.SetSoftObjectPath(e.Props, "ActorPath_", $"{package}.{asset}:{sub}");
            }
        }
    }

    [Fact]
    public void A_base_from_another_map_goes_into_the_level_the_target_keeps_its_own_pieces_in()
    {
        if (!HasServerFacility) return;
        var keys = BiggestBase(Load()).Take(12).ToList();
        var package = BasePackage.Create(Load(), keys, "Cascade", "WorldSave_Facility.sav", out _);
        var target = Load();
        MoveToOtherMap(target, "/Game/Maps/Elsewhere");

        Assert.True(package.IsOtherMap(target));
        Assert.DoesNotContain(BaseDestinations.For(package, target), d => d.Kind == BaseDestinationKind.SameSpot);

        var edits = new StagedBaseEdits(Counter('C'));
        var staged = edits.StageTransplant(package, new PlacedVector(1000, 0, 0));
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Contains(preview.Issues, i => i.Code == "other-map" && !i.IsBlocking);
        Assert.True(edits.ApplyTo(target).Applied);

        var reloaded = WorldSaveReader.ReadFromStream(new MemoryStream(Serialize(target)));
        var paths = staged.NewKeys.Values
            .Select(k => WorldMapAccessor.GetSoftObjectPath(WorldMapAccessor.FindEntry(reloaded.Raw, "DeployedObjectMap", k)!, "ActorPath_")!.Value)
            .ToList();
        Assert.All(paths, p => Assert.Equal("/Game/Maps/Elsewhere", p.Package));
        Assert.All(paths, p => Assert.Equal("Elsewhere", p.Asset));
        Assert.Equal(paths.Count, paths.Select(p => p.SubPath).Distinct().Count());
    }

    [Fact]
    public void A_portal_world_with_nothing_built_yet_takes_the_base_into_its_own_level()
    {
        if (!HasServerFacility) return;
        var portal = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_V_Alps.sav");
        if (!File.Exists(portal)) return;
        var target = WorldSaveReader.ReadFromFile(portal);
        var level = WorldMapAccessor.Entries(target.Raw, "DeployedObjectMap")
            .Select(e => WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_")?.Package).First(p => p is not null);
        var package = BasePackage.Create(Load(), BiggestBase(Load()).Take(3), "Cascade", "WorldSave_Facility.sav", out _);
        Assert.True(package.IsOtherMap(target));
        Assert.True(BasePackage.HasNoPlayerPieces(target));
        var places = BaseDestinations.For(package, target);
        Assert.DoesNotContain(places, d => d.Kind == BaseDestinationKind.Sector);
        Assert.DoesNotContain(places, d => d.Kind == BaseDestinationKind.SameSpot);
        var middle = Assert.Single(places, d => d.Kind == BaseDestinationKind.AreaMiddle);

        var edits = new StagedBaseEdits(Counter('D'));
        var staged = edits.StageTransplant(package, middle.OffsetFor(package));
        var preview = edits.Preview(target);
        Assert.True(preview.CanApply, string.Join(" | ", preview.Issues.Where(i => i.IsBlocking).Select(i => i.Message)));
        Assert.Contains(preview.Issues, i => i.Code == "other-map" && i.Severity == BaseEditSeverity.Warning);
        // Landed in the portal's own space, not at the Facility coordinates the pieces came from.
        var centre = preview.Duplications.Select(r => r.After!.Translation!.Value).ToList();
        Assert.InRange(centre.Average(p => p.X), middle.Point.X - 1, middle.Point.X + 1);
        Assert.InRange(centre.Average(p => p.Y), middle.Point.Y - 1, middle.Point.Y + 1);
        Assert.True(edits.ApplyTo(target).Applied);
        Assert.All(staged.NewKeys.Values, k => Assert.Equal(level,
            WorldMapAccessor.GetSoftObjectPath(WorldMapAccessor.FindEntry(target.Raw, "DeployedObjectMap", k)!, "ActorPath_")?.Package));
    }

    [Fact]
    public void Destinations_offer_the_same_spot_the_named_sectors_and_the_bases_already_there()
    {
        if (!HasServerFacility) return;
        var package = BasePackage.Create(Load(), BiggestBase(Load()), "Cascade", "WorldSave_Facility.sav", out _);
        var target = Load();

        var places = BaseDestinations.For(package, target);

        var same = Assert.Single(places, d => d.Kind == BaseDestinationKind.SameSpot);
        Assert.Equal(new PlacedVector(0, 0, 0), same.OffsetFor(package));
        Assert.Equal(10, places.Count(d => d.Kind == BaseDestinationKind.Sector));
        Assert.Contains(places, d => d.Kind == BaseDestinationKind.Sector && d.Name == "Manufacturing West");
        Assert.Contains(places, d => d.Kind == BaseDestinationKind.BesideBase && d.NearSector is not null);
        Assert.DoesNotContain(places, d => d.Name == "Outside any base");

        // The middle of the pieces lands on the place, and the lowest piece on its floor.
        var mines = places.First(d => d.Name == "The Mines");
        var shift = mines.OffsetFor(package);
        Assert.Equal(mines.Point.X, package.Centre!.Value.X + shift.X, 0);
        Assert.Equal(mines.Point.Y, package.Centre!.Value.Y + shift.Y, 0);
        Assert.Equal(mines.Point.Z, package.FloorZ!.Value + shift.Z, 0);
    }

    [Fact]
    public void Outlet_ids_are_found_on_saves_from_game_update_1_5()
    {
        // Native (1.5) saves name the outlet id SocketID instead of PowerSocket_<hash>. Copying a base
        // rewrites that id for each copied outlet; when it could not be found, the copy kept the
        // original's id and the safety check refused it (found moving a real base between two 1.5 worlds).
        var props = NativeSaveSchema.CreateStruct(new List<FPropertyTag>(), "Socket", "AbioticPowerSocketSave");
        props.Add(new FPropertyTag(new FString("SocketID"), new(new FString("StrProperty"), []), EPropertyTagFlags.None)
        {
            Property = new StrProperty(new FString("SocketID")) { Value = new FString("ABC1") },
        });

        Assert.Equal("ABC1", props.GetString("PowerSocket_"));
        Assert.Same(props[0], props.FindByPrefix("PowerSocket_"));
    }

    [Fact]
    public void A_base_split_over_two_far_apart_floors_moves_its_main_floor_and_lands_on_the_floor()
    {
        // Found moving a real base: the map groups pieces by distance on the map only, so a bench up top
        // and crates 175 m below were one base, and moving it whole left most pieces in the air.
        WorldDeployable Piece(string id, double z, bool bench = false)
            => new(id, bench ? "Deployed_CraftingBench_Default_C" : "Deployed_StorageCrate_C", 0, 0, z, false, 0);
        var pieces = new[]
        {
            Piece("a", -15800, bench: true), Piece("b", -15790), Piece("c", -15400), Piece("d", -15200),
            Piece("x", -33300), Piece("y", -33290),
        };
        var main = BaseFloors.MainFloorIds(new WorldBase("Base 1", 0, 0, pieces));
        Assert.Equal(["a", "b", "c", "d"], main.Order());
        Assert.Equal(2, BaseFloors.Split(pieces, p => p.Z).Count);
        // A tall building of the player's own (storeys a few metres apart) stays one floor.
        Assert.Single(BaseFloors.Split(new[] { 0.0, 400, 800, 1200, 1600 }, z => z));
    }

    [Fact]
    public void The_landing_floor_is_the_main_floor_not_a_stray_piece_far_below()
    {
        if (!HasServerFacility) return;
        var source = Load();
        var keys = BiggestBase(source).Take(10).ToList();
        var package = BasePackage.Create(source, keys, "Cascade", "WorldSave_Facility.sav", out _);
        var mainFloor = package.FloorZ!.Value;
        // The floor is the lowest piece of the floor most pieces stand on.
        var zs = package.Pieces.Select(p => p.Position!.Value.Z).ToList();
        Assert.Contains(mainFloor, zs);
        Assert.True(zs.Count(z => z >= mainFloor && z - mainFloor <= BaseFloors.FloorGap * zs.Count) >= zs.Count / 2);
    }

    private static string? NativeFacility()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "fixtures", "Update1_5", "WorldSave_Facility.sav");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    [Fact]
    public void A_base_cannot_go_between_saves_of_the_old_and_the_new_game_layout()
    {
        // Found in the game: pieces from a 1.5 world put into an older world vanished when the game
        // upgraded that world (it could not read them: no position, no level). Both directions refuse.
        if (!HasServerFacility || NativeFacility() is not { } nativePath) return;
        var native = WorldSaveReader.ReadFromFile(nativePath);
        var old = Load();
        Assert.True(PlacedObjectDuplication.IsNativeSave(native));
        Assert.False(PlacedObjectDuplication.IsNativeSave(old));

        var fromOld = BasePackage.Create(old, BiggestBase(old).Take(3), "Old", "WorldSave_Facility.sav", out _);
        var intoNative = new StagedBaseEdits(Counter('G'));
        intoNative.StageTransplant(fromOld, new PlacedVector(0, 0, 0));
        Assert.Contains(intoNative.Preview(native).Issues, i => i.IsBlocking && i.Code == "save-format");

        // The other 1.5 fixture has pieces a player built.
        var pets = WorldSaveReader.ReadFromFile(Path.Combine(Path.GetDirectoryName(nativePath)!, "PlacedPets", "WorldSave_Facility.sav"));
        var nativeKeys = PlacedObjectCensus.Build(pets).Objects!.Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.Transform?.Translation is not null).Select(o => o.Key).Take(3).ToList();
        Assert.NotEmpty(nativeKeys);
        var fromNative = BasePackage.Parse(BasePackage.Create(pets, nativeKeys, "New", "WorldSave_Facility.sav", out _).ToJson());
        var intoOld = new StagedBaseEdits(Counter('H'));
        intoOld.StageTransplant(fromNative, new PlacedVector(0, 0, 0));
        Assert.Contains(intoOld.Preview(old).Issues, i => i.IsBlocking && i.Code == "save-format");

        // The same layout on both sides is fine.
        var intoNativeToo = new StagedBaseEdits(Counter('J'));
        intoNativeToo.StageTransplant(fromNative, new PlacedVector(500, 0, 0));
        Assert.DoesNotContain(intoNativeToo.Preview(WorldSaveReader.ReadFromFile(nativePath)).Issues, i => i.Code == "save-format");
    }

    [Fact]
    public void Single_pieces_of_a_staged_base_can_be_stood_at_their_own_heights()
    {
        // Found in the game: a base put down on a flat floor kept the heights of its old place, so pieces
        // floated, the game dropped them and a bench was lost. The 3D view stands each piece on the floor.
        if (!HasServerFacility) return;
        var keys = BiggestBase(Load()).Take(5).ToList();
        var package = BasePackage.Create(Load(), keys, "Cascade", "WorldSave_Facility.sav", out _);
        var edits = new StagedBaseEdits(Counter('C'));
        var staged = edits.StageTransplant(package, new PlacedVector(1000, 0, 0));
        var target = Load();
        var first = keys[0];

        Assert.NotNull(edits.SetPieceHeights(staged.Id, new Dictionary<string, double> { [first] = 1234 }));
        var rows = edits.Preview(target).Duplications;
        Assert.Equal(1234, rows.Single(r => r.SourceKey == first).After!.Translation!.Value.Z);
        var other = rows.First(r => r.SourceKey != first);
        Assert.NotEqual(1234, other.After!.Translation!.Value.Z);

        // Moving the base again starts from the group's own heights; the floor is found again there.
        edits.RestageDuplication(staged.Id, new PlacedVector(2000, 0, 0), 0);
        Assert.NotEqual(1234, edits.Preview(target).Duplications.Single(r => r.SourceKey == first).After!.Translation!.Value.Z);
        Assert.Null(edits.SetPieceHeights(999, new Dictionary<string, double>()));
        var result = edits.ApplyTo(target);
        Assert.True(result.Applied, string.Join(" | ", result.Issues.Where(i => i.IsBlocking).Select(i => i.Code + ": " + i.Message)));
    }
}
