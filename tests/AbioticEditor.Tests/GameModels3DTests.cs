using System.Buffers.Binary;
using System.Numerics;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.GameModels3D;
using AbioticEditor.Plugins.Scene;
using AbioticEditor.Web.Models;
using CUE4Parse.UE4.Objects.Core.Math;

namespace AbioticEditor.Tests;

/// <summary>
/// The optional game-models plugin (<c>plugins/GameModels3D</c>) and the host side of the 3D model
/// capability: the plugin's matrices must agree with Core's <see cref="PlacedSceneSpace"/> (or parts
/// would drift off their objects), the mesh layout must match what the viewer parses, and the plugin
/// must load through the ordinary plugin manager. Tests that read the installed game skip without it.
/// </summary>
public sealed class GameModels3DTests
{
    private static readonly Random Rng = new(20261001);

    // ---------- space conversion ----------

    [Fact]
    public void Plugin_matrices_match_the_viewers_own_object_transform()
    {
        for (var i = 0; i < 200; i++)
        {
            var loc = new PlacedVector(Rng.NextDouble() * 1e5 - 5e4, Rng.NextDouble() * 1e5 - 5e4, Rng.NextDouble() * 1e4 - 5e3);
            var q = PlacedSceneSpace.Normalize(new PlacedQuaternion(Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5));
            var scale = new PlacedVector(0.5 + Rng.NextDouble() * 2, 0.5 + Rng.NextDouble() * 2, 0.5 + Rng.NextDouble() * 2);

            var plugin = SceneMath.ToViewer(SceneMath.Transform(
                new FVector((float)loc.X, (float)loc.Y, (float)loc.Z),
                new FQuat((float)q.X, (float)q.Y, (float)q.Z, (float)q.W),
                new FVector((float)scale.X, (float)scale.Y, (float)scale.Z)));

            // What base3d.js builds for a saved object: Matrix4.compose(position, quaternion, scale)
            // from the viewer-space values Core converts.
            var expected = Compose(PlacedSceneSpace.ToViewer(loc), PlacedSceneSpace.ToViewer(q), PlacedSceneSpace.ScaleToViewer(scale));
            for (var k = 0; k < 16; k++) Assert.True(Math.Abs(expected[k] - plugin[k]) <= 1e-3 + (1e-5 * Math.Abs(expected[k])), $"element {k}: {expected[k]} vs {plugin[k]}");
        }
    }

    [Fact]
    public void Composed_parts_follow_their_parent_like_the_game()
    {
        // A lid 88 cm up and 41 cm forward on a crate turned 90 degrees: in Unreal the lid ends
        // up 41 cm along +Y. The viewer maps Unreal +Y to its +Z and centimetres to metres.
        var lid = SceneMath.Transform(new FVector(41, 0, 88), new FRotator(0, 0, 0), FVector.OneVector);
        var crate = SceneMath.Transform(new FVector(1000, 2000, 300), new FRotator(0, 90, 0), FVector.OneVector);
        var world = SceneMath.ToViewer(lid * crate);
        Assert.Equal(10.0f, world[12], 3);
        Assert.Equal(3.0f + 0.88f, world[13], 3);
        Assert.Equal(20.0f + 0.41f, world[14], 3);
    }

    /// <summary>THREE.Matrix4.compose, column-major.</summary>
    private static float[] Compose(PlacedVector p, PlacedQuaternion q, PlacedVector s)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double x2 = x + x, y2 = y + y, z2 = z + z;
        double xx = x * x2, xy = x * y2, xz = x * z2, yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
        return
        [
            (float)((1 - (yy + zz)) * s.X), (float)((xy + wz) * s.X), (float)((xz - wy) * s.X), 0,
            (float)((xy - wz) * s.Y), (float)((1 - (xx + zz)) * s.Y), (float)((yz + wx) * s.Y), 0,
            (float)((xz + wy) * s.Z), (float)((yz - wx) * s.Z), (float)((1 - (xx + yy)) * s.Z), 0,
            (float)p.X, (float)p.Y, (float)p.Z, 1,
        ];
    }

    // ---------- mesh layout ----------

    [Fact]
    public void Mesh_layout_matches_the_documented_blocks()
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, -1];
        float[] uvs = [0, 0, 1, 0, 0, 1];
        uint[] indices = [0, 1, 2];
        var bytes = SceneMeshFormat.Write(positions, normals, uvs, indices, [new SceneMeshFormat.Section(2, 0, 3)]);

        Assert.Equal("ABM1", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16))); // 16-bit indices
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20))); // section material
        var at = 32;
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 12))); // second vertex x
        at += 36;
        Assert.Equal(32767, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 4))); // first normal z
        Assert.Equal(-32767, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at + 16)));
        at += 20; // 18 bytes of normals padded to 20
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8)));
        at += 24;
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 4)));
        Assert.Equal(at + 8, bytes.Length); // 6 bytes of indices padded to 8
        Assert.Equal(0, bytes.Length % 4);
    }

    [Fact]
    public void Large_meshes_switch_to_32_bit_indices()
    {
        const int count = 70_000;
        var bytes = SceneMeshFormat.Write(new float[count * 3], new float[count * 3], new float[count * 2], [0, 1, 69_999], [new SceneMeshFormat.Section(0, 0, 3)]);
        Assert.Equal(SceneMeshFormat.Flag32BitIndices, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(69_999u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4)));
    }

    // ---------- level index ----------

    [Fact]
    public void Level_index_survives_its_cache_file()
    {
        var world = SceneMath.Transform(new FVector(100, 200, 300), new FRotator(10, 20, 30), new FVector(1, 2, 3));
        var data = new LevelIndexData
        {
            Map = "AbioticFactor/Content/Maps/Test.umap",
            Meshes = ["/Game/A.A", "/Game/B.B"],
            OverrideSets = [[], [null, "/Game/M.M"]],
            Actors = ["StaticMeshActor_1"],
            Entries = [new LevelEntry(1, 1, 0, world, new Vector3(1, 2, 3), 450f)],
        };
        data.ComputeBounds();
        var path = Path.Combine(Path.GetTempPath(), $"abiotic-level-{Guid.NewGuid():N}.bin");
        try
        {
            LevelIndex.Save(path, data);
            var back = LevelIndex.Load(path)!;
            Assert.Equal(data.Map, back.Map);
            Assert.Equal(data.Meshes, back.Meshes);
            Assert.Null(back.OverrideSets[1][0]);
            Assert.Equal("/Game/M.M", back.OverrideSets[1][1]);
            var e = Assert.Single(back.Entries);
            Assert.Equal(world, e.World);
            Assert.Equal(450f, e.Radius);
            Assert.Equal(data.Min, back.Min);
            Assert.Equal(data.Max, back.Max);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- asset ids ----------

    [Theory]
    [InlineData("mesh/0/../../Windows/win.ini")]
    [InlineData("tex/512C:/Windows/win.ini")]
    [InlineData("mesh/0/Game/Models/SM_Thing")]
    [InlineData("mesh/0//Game/A.A")]
    [InlineData("other/0/Game/A.A")]
    [InlineData("")]
    public void Malformed_asset_ids_are_refused_without_touching_anything(string id)
    {
        var provider = new PakSceneModelProvider(new TestHost(Path.GetTempPath()));
        Assert.Null(provider.OpenAsset(id));
    }

    [Fact]
    public void Class_paths_that_are_not_game_paths_are_refused()
    {
        var provider = new PakSceneModelProvider(new TestHost(Path.GetTempPath()));
        Assert.Null(provider.DescribeClass(@"C:\Windows\notepad.exe"));
        Assert.Null(provider.DescribeLevel(new SceneLevelQuery("../../x", [0, 0, 0], [1, 1, 1], 10)));
    }

    // ---------- region naming (host side) ----------

    [Theory]
    [InlineData(@"C:\saves\WorldSave_Facility.sav", "Facility")]
    [InlineData("/home/p/WorldSave_Facility_Dam_Central.sav", "Facility_Dam_Central")]
    [InlineData(@"C:\saves\WorldSave_MetaData.sav", null)]
    [InlineData(@"C:\saves\Player_76561197993781479.sav", null)]
    [InlineData(@"C:\saves\WorldSave_Bad Name.sav", null)]
    [InlineData(null, null)]
    public void Region_names_come_from_the_world_save_file(string? path, string? region)
        => Assert.Equal(region, Base3DScene.RegionOf(path));

    [Fact]
    public void Level_actor_names_list_only_level_placed_objects_with_an_actor()
    {
        PlacedObjectSummary Row(string key, bool? built, string? actor) => new(key, null, "Thing_C", PlacedClassOrigin.GameBlueprint, actor, null,
            new PlacedObjectTransform(new PlacedVector(0, 0, 0), PlacedQuaternion.Identity, new PlacedVector(1, 1, 1)),
            null, null, built, null, null, null, null, 0, 0, [], []);
        var scene = Base3DScene.Build(
        [
            Row("a", false, "/Game/Maps/Facility.Facility:PersistentLevel.Door_C_1"),
            Row("b", true, "/Game/Maps/Facility.Facility:PersistentLevel.Deployed_X_C_2"),
            Row("c", null, null),
            Row("d", false, "/Game/Maps/Facility.Facility:PersistentLevel.Door_C_1"),
        ], _ => null);
        Assert.Equal(["/Game/Maps/Facility.Facility:PersistentLevel.Door_C_1"], scene.LevelActorNames());
    }

    [Fact]
    public void Scene_objects_carry_their_class_path_for_models()
    {
        var row = new PlacedObjectSummary("k", "/Game/B/Deployed_X.Deployed_X_C", "Deployed_X_C", PlacedClassOrigin.GameBlueprint, null, null,
            new PlacedObjectTransform(new PlacedVector(0, 0, 0), PlacedQuaternion.Identity, new PlacedVector(1, 1, 1)),
            null, null, true, null, null, null, null, 0, 0, [], []);
        var scene = Base3DScene.Build([row], _ => null);
        Assert.Equal("/Game/B/Deployed_X.Deployed_X_C", Assert.Single(scene.Objects).Cls);
    }

    [Fact]
    public void Painted_objects_ask_for_their_painted_model_and_unpainted_ones_do_not()
    {
        PlacedObjectSummary Row(string key, int? paint) => new(key, "/Game/B/Deployed_X.Deployed_X_C", "Deployed_X_C", PlacedClassOrigin.GameBlueprint, null, null,
            new PlacedObjectTransform(new PlacedVector(0, 0, 0), PlacedQuaternion.Identity, new PlacedVector(1, 1, 1)),
            null, null, true, null, null, null, paint, 0, 0, [], []);
        var scene = Base3DScene.Build([Row("red", 2), Row("plain", null), Row("none", DeployablePaintCatalog.NoneValue)], _ => null);
        Assert.Equal(2, scene.Objects.Single(o => o.Key == "red").Paint);
        Assert.Null(scene.Objects.Single(o => o.Key == "plain").Paint);
        Assert.Null(scene.Objects.Single(o => o.Key == "none").Paint);

        Assert.Equal("#paint=2", scene.Objects.Single(o => o.Key == "red").Variant);
        Assert.Null(scene.Objects.Single(o => o.Key == "plain").Variant);

        var (cls, state) = AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey("/Game/B/Deployed_X.Deployed_X_C#paint=2");
        Assert.Equal("/Game/B/Deployed_X.Deployed_X_C", cls);
        Assert.Equal(2, state!.PaintColor);
        Assert.Null(AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey("/Game/B/Deployed_X.Deployed_X_C").State);
        // Anything unexpected after the class path is not a state, and is passed on unchanged.
        Assert.Equal(("/Game/B/X.X_C#paint=-1", (SceneObjectState?)null), AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey("/Game/B/X.X_C#paint=-1"));
    }

    [Fact]
    public void Spline_meshes_bend_like_the_engine()
    {
        var straight = new MeshInfo([], new Vector3(0, -50, -50), new Vector3(300, 50, 50));
        // A straight curve the length of the mesh leaves it as it is.
        var line = new SplineBaker.Curve(Vector3.Zero, new Vector3(300, 0, 0), new Vector3(300, 0, 0), new Vector3(300, 0, 0),
            Vector2.One, Vector2.One, 0, 0, Vector2.Zero, Vector2.Zero, Vector3.UnitZ, 0, false, 0, 0);
        var (p, n) = SplineBaker.Bender(line, straight)(new Vector3(150, 10, 20), Vector3.UnitY);
        AssertNear(new Vector3(150, 10, 20), p);
        AssertNear(Vector3.UnitY, n);

        // A quarter turn: the mesh's far end follows the curve to its end point, facing along +Y.
        var turn = new SplineBaker.Curve(Vector3.Zero, new Vector3(400, 0, 0), new Vector3(200, 200, 0), new Vector3(0, 400, 0),
            Vector2.One, new Vector2(2, 2), 0, 0, Vector2.Zero, Vector2.Zero, Vector3.UnitZ, 0, false, 0, 0);
        var bend = SplineBaker.Bender(turn, straight);
        AssertNear(new Vector3(200, 200, 0), bend(new Vector3(300, 0, 0), Vector3.UnitZ).Position);
        // At the end the cross-section is doubled (EndScale 2) and turned: the mesh's +Y now points along -X.
        AssertNear(new Vector3(200 - 20, 200, 0), bend(new Vector3(300, 10, 0), Vector3.UnitZ).Position);
        AssertNear(new Vector3(200, 200, 20), bend(new Vector3(300, 0, 10), Vector3.UnitZ).Position);

        // Absolute components ignore their parent's transform (the tram rails' spline does).
        var parent = Matrix4x4.CreateTranslation(-12450, 32250, 500);
        var local = Matrix4x4.CreateTranslation(1, 2, 3);
        Assert.Equal(new Vector3(-12449, 32252, 503), SceneMath.Attach(local, parent, false, false, false).Translation);
        Assert.Equal(new Vector3(1, 2, 3), SceneMath.Attach(local, parent, true, true, false).Translation);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 0.01f, $"expected {expected}, got {actual}");

    [Fact]
    public void Garden_plots_carry_their_crops_into_the_model_key()
    {
        var row = new PlacedObjectSummary("plot", "/Game/B/GardenPlot_Medium.GardenPlot_Medium_C", "GardenPlot_Medium_C", PlacedClassOrigin.GameBlueprint, null, null,
            new PlacedObjectTransform(new PlacedVector(0, 0, 0), PlacedQuaternion.Identity, new PlacedVector(1, 1, 1)),
            null, null, true, null, null, null, null, 0, 0, [], [],
            [new PlacedCrop(1, "Plant_Tomato", 2), new PlacedCrop(0, "Plant_Corn", 4)]);
        var variant = Assert.Single(Base3DScene.Build([row], _ => null).Objects).Variant;
        Assert.Equal("#crops=0.Plant_Corn.4,1.Plant_Tomato.2", variant);

        var (cls, state) = AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey("/Game/B/GardenPlot_Medium.GardenPlot_Medium_C" + variant);
        Assert.Equal("/Game/B/GardenPlot_Medium.GardenPlot_Medium_C", cls);
        Assert.Null(state!.PaintColor);
        Assert.Equal([new SceneCrop(0, "Plant_Corn", 4), new SceneCrop(1, "Plant_Tomato", 2)], state.Crops!);
        // A crop row with characters a row name cannot have is refused.
        Assert.Null(AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey("/Game/B/X.X_C#crops=0.../etc.4").State);
    }

    [Fact]
    public void The_census_reads_what_grows_in_each_garden_plot_spot()
    {
        var facility = Path.Combine(Fixtures.ServerWorldsDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(facility)) return;
        var census = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(facility));
        var plots = census.Objects!.Where(o => o.ClassName?.StartsWith("GardenPlot_", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(plots);
        var crops = plots.Where(o => o.Crops is not null).SelectMany(o => o.Crops!).ToList();
        Assert.NotEmpty(crops);
        Assert.All(crops, c =>
        {
            Assert.StartsWith("Plant_", c.Row, StringComparison.Ordinal);
            Assert.InRange(c.Stage, 0, 7);
            Assert.True(c.Spot >= 0);
        });
    }

    [Fact]
    public void The_newer_mappings_file_wins_over_an_old_import()
    {
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-usmap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var user = Path.Combine(dir, "user.usmap");
            var bundled = Path.Combine(dir, "bundled.usmap");
            Assert.Null(GameAssetProvider.FindConventionalMappings(user, bundled));
            File.WriteAllBytes(bundled, [0xC4, 0x30]);
            Assert.Equal(bundled, GameAssetProvider.FindConventionalMappings(user, bundled));
            File.WriteAllBytes(user, [0xC4, 0x30]);
            File.SetLastWriteTimeUtc(user, new DateTime(2026, 5, 19, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(bundled, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(bundled, GameAssetProvider.FindConventionalMappings(user, bundled));
            // A dump imported after the editor was built still overrides the bundled file.
            File.SetLastWriteTimeUtc(user, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(user, GameAssetProvider.FindConventionalMappings(user, bundled));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Meshes_can_carry_vertex_colours_after_their_texture_coordinates()
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] normals = [0, 1, 0, 0, 1, 0, 0, 1, 0];
        float[] uvs = [0, 0, 1, 0, 0, 1];
        byte[] colors = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
        var plain = SceneMeshFormat.Write(positions, normals, uvs, [0u, 1u, 2u], [new SceneMeshFormat.Section(0, 0, 3)]);
        var coloured = SceneMeshFormat.Write(positions, normals, uvs, colors, [0u, 1u, 2u], [new SceneMeshFormat.Section(0, 0, 3)]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(16)) & SceneMeshFormat.FlagVertexColors);
        Assert.Equal(SceneMeshFormat.FlagVertexColors, BinaryPrimitives.ReadUInt32LittleEndian(coloured.AsSpan(16)) & SceneMeshFormat.FlagVertexColors);
        Assert.Equal(plain.Length + colors.Length, coloured.Length);
        // header 20 + one section 12 + positions 36 + normals 18 padded to 20 + uvs 24 = 112
        Assert.Equal(colors, coloured.AsSpan(112, colors.Length).ToArray());
        Assert.Throws<ArgumentException>(() => SceneMeshFormat.Write(positions, normals, uvs, [1, 2, 3], [0u, 1u, 2u], [new SceneMeshFormat.Section(0, 0, 3)]));
    }

    // ---------- plugin loading ----------

    [Fact]
    public void Plugin_loads_through_the_plugin_manager_and_offers_one_provider()
    {
        var root = Path.Combine(Path.GetTempPath(), "abiotic-models-plugin-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "GameModels3D");
        Directory.CreateDirectory(folder);
        var previous = Environment.GetEnvironmentVariable("ABIOTIC_PLUGINS_DIR");
        try
        {
            var built = Path.GetDirectoryName(typeof(GameModelsPlugin).Assembly.Location)!;
            File.Copy(Path.Combine(built, "GameModels3D.dll"), Path.Combine(folder, "GameModels3D.dll"));
            File.Copy(Path.Combine(RepoRoot(), "plugins", "GameModels3D", "plugin.json"), Path.Combine(folder, "plugin.json"));
            Environment.SetEnvironmentVariable("ABIOTIC_PLUGINS_DIR", root);

            var manager = new PluginManager();
            manager.EnsureLoaded("test");

            var descriptor = Assert.Single(manager.Descriptors);
            Assert.Equal(PluginLoadState.Loaded, descriptor.State);
            var provider = Assert.Single(manager.SceneModelProviders);
            Assert.Equal("pak-models", provider.Value.Id);
            Assert.Contains("3D model provider", descriptor.CapabilitySummary(), StringComparison.Ordinal);
            Assert.Contains(PluginCapabilities.SceneModels, descriptor.Manifest.Capabilities);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ABIOTIC_PLUGINS_DIR", previous);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AbioticEditor.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("repository root not found");
    }

    // ---------- against the installed game (skips without one) ----------

    [Fact]
    public void Classes_resolve_to_the_meshes_the_game_draws()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        var bench = assets.UseFileProvider(p => ClassModelResolver.Resolve(p,
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_CraftingBench_Default.Deployed_CraftingBench_Default_C"));
        Assert.Contains(bench, part => part.Mesh.EndsWith("SM_CraftingBench.SM_CraftingBench", StringComparison.Ordinal));
        // The placeholder desk the shared furniture parent declares must be replaced, not drawn too.
        Assert.DoesNotContain(bench, part => part.Mesh.Contains("SM_Office_Desk_NoDrawers", StringComparison.Ordinal));

        // A class whose override stores no mesh of its own: the mesh comes from the parent's override.
        var green = assets.UseFileProvider(p => ClassModelResolver.Resolve(p,
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_Antelight_Green.Deployed_Antelight_Green_C"));
        Assert.Contains(green, part => part.Mesh.Contains("SM_Antelight", StringComparison.Ordinal));

        // Child actors (power sockets on a plug strip) contribute their own parts.
        var strip = assets.UseFileProvider(p => ClassModelResolver.Resolve(p,
            "/Game/Blueprints/DeployedObjects/Misc/Deployed_PlugStrip.Deployed_PlugStrip_C"));
        Assert.Contains(strip, part => part.Name.StartsWith("PowerSocket", StringComparison.Ordinal));
    }

    [Fact]
    public void Materials_read_their_blend_mode_and_light_beams_count_as_effects()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        static ResolvedMaterial Of(GameAssetProvider a, string path) => a.UseFileProvider(p =>
            p.TryLoadPackageObject(path, out var o) ? MaterialResolver.Resolve(o as CUE4Parse.UE4.Assets.Exports.Material.UMaterialInterface) : ResolvedMaterial.Fallback);

        // The fake light-beam cones hung under ceiling lamps: translucent and unlit, so left out of the view.
        var beam = Of(assets, "/Game/Models/FX/M_EV_Lightbeam_Master_01.M_EV_Lightbeam_Master_01");
        Assert.True(beam.Effect);
        Assert.True(beam.Opacity < 1f);
        // Window glass: see-through (its blend mode comes from the master, stored as an enum name), but lit, so kept.
        var glass = Of(assets, "/Game/Textures/Glass/M_Glass_Unbreakable_01.M_Glass_Unbreakable_01");
        Assert.True(glass.Opacity < 1f);
        Assert.False(glass.Effect);
        // An ordinary opaque surface stays solid.
        var wall = Of(assets, "/Game/Models/Environment/Walls/M_SecurityKit.M_SecurityKit");
        Assert.Equal(1f, wall.Opacity);
        Assert.False(wall.Effect);
    }

    [Fact]
    public void Paint_swaps_the_slots_the_games_paint_table_names()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        const string Bench = "/Game/Blueprints/DeployedObjects/Furniture/Deployed_CraftingBench_Default.Deployed_CraftingBench_Default_C";

        var red = assets.UseFileProvider(p => PaintResolver.Materials(p, Bench, 2));
        Assert.NotNull(red);
        Assert.EndsWith("M_CraftingBench_Bench_Red.M_CraftingBench_Bench_Red", red![0], StringComparison.Ordinal);
        // "None" (12) is unpainted, and a class with no paint row has no paint materials.
        Assert.Null(assets.UseFileProvider(p => PaintResolver.Materials(p, Bench, DeployablePaintCatalog.NoneValue)));
        Assert.Null(assets.UseFileProvider(p => PaintResolver.Materials(p,
            "/Game/Blueprints/DeployedObjects/Misc/Deployed_PlugStrip.Deployed_PlugStrip_C", 2)));

        // Through the provider: the bench's own mesh wears the red material in slot 0.
        PluginHostEnvironment.GameAssets = () => assets;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "abiotic-paint-test-" + Guid.NewGuid().ToString("N"));
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var model = provider.DescribeClass(Bench, 2);
            var plain = provider.DescribeClass(Bench);
            Assert.NotNull(model);
            Assert.NotEqual(plain!.Parts[0].Materials[0].Texture, model!.Parts[0].Materials[0].Texture);
            Directory.Delete(dir, recursive: true);
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
        }
    }

    [Fact]
    public void Portal_world_levels_are_placed_where_the_facility_streams_them()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-portal-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // The Alps lodge's kitchen as saved in WorldSave_V_Alps.sav: Facility coordinates, because
            // the Facility map streams V_Alps in far from its own origin (turned about 42 degrees).
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var c = PlacedSceneSpace.ToViewer(new PlacedVector(-277000, 77000, -1500));
            var query = new SceneLevelQuery("V_Alps", [(float)c.X - 15, (float)c.Y - 5, (float)c.Z - 15], [(float)c.X + 15, (float)c.Y + 5, (float)c.Z + 15], 5000, []);
            provider.DescribeLevel(query);
            provider.WaitForLevels(TimeSpan.FromMinutes(5));
            var slice = provider.DescribeLevel(query);
            Assert.NotNull(slice);
            Assert.True(slice!.TotalInBox > 100, $"only {slice.TotalInBox} level pieces around the lodge");
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Characters_driven_by_an_animation_blueprint_rest_in_its_idle_animation()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var (posed, idle) = assets.UseFileProvider(p =>
        {
            if (!p.TryLoadPackage("AbioticFactor/Content/Maps/Facility_Office1.umap", out var pkg)) return (0, 0);
            int count = 0, idles = 0;
            foreach (var e in pkg.GetExports())
            {
                if (!PoseBaker.IsSkeletalComponent(e.ExportType) || Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(e, "AnimClass", null) is not { IsNull: false }) continue;
                if (PoseBaker.PoseOf(e) is not { } pose) continue;
                count++;
                if (pose.Anim.Name.Contains("Idle", StringComparison.OrdinalIgnoreCase)) idles++;
            }
            return (count, idles);
        });
        Assert.True(posed > 0, "no blueprint-driven character got a pose");
        Assert.Equal(posed, idle); // story characters stand in the blueprint's idle, not a sit or walk
    }

    [Fact]
    public void The_levels_lamps_and_door_leaves_are_indexed_and_open_doors_leave_their_doorway_clear()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Office1.umap"));
        Assert.True(index.Lights.Count > 20, $"only {index.Lights.Count} lights");
        Assert.All(index.Lights, l => Assert.True(l.Brightness > 0 && l.Radius > 0));
        Assert.Contains(index.DoorLeaves, e => index.Actors[index.Entries[e].Actor] == "BlastDoor_C_2");

        PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-door-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var c = PlacedSceneSpace.ToViewer(new PlacedVector(-13919.5, 10986.8, 11)); // Office1's blast door, in the world
            var box = new SceneLevelQuery("Facility_Office1", [(float)c.X - 6, (float)c.Y - 3, (float)c.Z - 6], [(float)c.X + 6, (float)c.Y + 4, (float)c.Z + 6], 20000, []);
            provider.DescribeLevel(box);
            provider.WaitForLevels(TimeSpan.FromMinutes(5));
            var closed = provider.DescribeLevel(box)!;
            var open = provider.DescribeLevel(box with { OpenDoors = ["Facility_Office1:BlastDoor_C_2"] })!;
            Assert.True(open.TotalInBox < closed.TotalInBox, $"open {open.TotalInBox}, closed {closed.TotalInBox}");
            Assert.NotEmpty(closed.Lights);
            Assert.All(closed.Lights, l => Assert.Equal(3, l.Position.Length));
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_swinging_door_open_inwards_or_outwards_draws_its_leaf_swung_that_way()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        const string Door = "SimpleDoor_ParentBP_C_0";
        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Office1.umap"));
        int Of(HashSet<int> set) => set.Single(e => index.Actors[index.Entries[e].Actor] == Door);
        var leaf = index.Entries[Of(index.DoorLeaves)];
        var inward = index.Entries[Of(index.DoorOpenInward)];
        var outward = index.Entries[Of(index.DoorOpenOutward)];
        Assert.Equal(leaf.Mesh, inward.Mesh); // the same leaf, placed swung
        Assert.NotEqual(leaf.World, inward.World);
        Assert.NotEqual(inward.World, outward.World);

        PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-swing-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = DoorLocationResolver.RootsForMap(assets, "Facility_Office1")[Door];
            var (x, y, z) = assets.PlaceInWorld("Facility_Office1", root.X, root.Y, root.Z);
            var c = PlacedSceneSpace.ToViewer(new PlacedVector(x, y, z));
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var box = new SceneLevelQuery("Facility_Office1", [(float)c.X - 4, (float)c.Y - 2, (float)c.Z - 4], [(float)c.X + 4, (float)c.Y + 3, (float)c.Z + 4], 20000, []);
            provider.DescribeLevel(box);
            provider.WaitForLevels(TimeSpan.FromMinutes(5));
            var closed = provider.DescribeLevel(box)!.TotalInBox;
            var swungIn = provider.DescribeLevel(box with { OpenDoors = ["Facility_Office1:" + Door + "|in"] })!.TotalInBox;
            var gone = provider.DescribeLevel(box with { OpenDoors = ["Facility_Office1:" + Door] })!.TotalInBox;
            Assert.Equal(closed, swungIn); // the closed leaf out, the swung one in
            Assert.Equal(closed - 1, gone);
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Landscape_terrain_is_indexed_and_bakes_to_a_height_grid()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/V_Alps.umap"));
        var terrain = index.Meshes.Where(LandscapeBaker.IsKey).ToList();
        Assert.True(terrain.Count > 100, $"only {terrain.Count} terrain pieces indexed");
        Assert.All(terrain, key => Assert.StartsWith("/Game/Maps/V_Alps.V_Alps#land=", key, StringComparison.Ordinal));

        var component = assets.UseFileProvider(p => LandscapeBaker.Load(p, terrain[0]));
        Assert.NotNull(component);
        var full = assets.UseFileProvider(_ => LandscapeBaker.Bake(component!, 0));
        var coarse = assets.UseFileProvider(_ => LandscapeBaker.Bake(component!, 1));
        var side = component!.ComponentSizeQuads + 1;
        Assert.Equal((uint)(side * side), BinaryPrimitives.ReadUInt32LittleEndian(full.AsSpan(4)));
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(coarse.AsSpan(4)) < (uint)(side * side));

        // Keys that are not landscape components are refused.
        Assert.Null(assets.UseFileProvider(p => LandscapeBaker.Load(p, "/Game/Maps/V_Alps.V_Alps#land=0")));
        Assert.Null(assets.UseFileProvider(p => LandscapeBaker.Load(p, "/Game/Maps/V_Alps.V_Alps#land=x")));
    }

    [Fact]
    public void The_fifth_terrain_slot_is_painted_by_the_misc2_layer()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        // The Japanese shrine paints its stone paths with "Misc2" (LayerInfo_Cobblestone); the material's
        // fifth slot (Quinary) holds the stone path texture.
        var layers = assets.UseFileProvider(p => TerrainMaterial.Read(p, "/Game/Textures/Landscape/M_ABF_LandscapeTorii.M_ABF_LandscapeTorii"));
        Assert.EndsWith("T_TORII_StonePath_01", layers[4].Texture!, StringComparison.Ordinal);

        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/H_Japan.umap"));
        var maxAlpha = assets.UseFileProvider(p =>
        {
            var best = 0;
            foreach (var key in index.Meshes.Where(LandscapeBaker.IsKey))
            {
                if (LandscapeBaker.Load(p, key) is not { } c || LandscapeBaker.Bake(c, 1) is not { } mesh) continue;
                var vertices = (int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(4));
                var sections = (int)BinaryPrimitives.ReadUInt32LittleEndian(mesh.AsSpan(12));
                var colours = 20 + (sections * 12) + (vertices * 12) + (((vertices * 6) + 3) & ~3) + (vertices * 8);
                for (var v = 0; v < vertices; v++) best = Math.Max(best, mesh[colours + (v * 4) + 3]);
                if (best > 128) break;
            }
            return best;
        });
        Assert.True(maxAlpha > 128, $"the fifth layer's weight never rises above {maxAlpha}");
    }

    [Fact]
    public void Crops_stand_on_their_spot_with_the_stage_mesh_and_fruit_when_grown()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        const string Plot = "/Game/Blueprints/DeployedObjects/Farming/GardenPlot_Medium.GardenPlot_Medium_C";

        var plants = assets.UseFileProvider(PlantTable.Read);
        Assert.Equal(32, plants.Count);
        Assert.Contains("Corn", plants.Keys);

        PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-crops-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var plain = provider.DescribeClass(Plot)!;
            var grown = provider.DescribeClass(Plot, new SceneObjectState(null, [new SceneCrop(1, "Plant_Corn", 4)]))!;
            var sprout = provider.DescribeClass(Plot, new SceneObjectState(null, [new SceneCrop(1, "Plant_Corn", 0)]))!;

            var cornGrown = grown.Parts.Single(p => p.Name == "Plot2/Plant_Corn");
            var cornSprout = sprout.Parts.Single(p => p.Name == "Plot2/Plant_Corn");
            Assert.NotEqual(cornGrown.Mesh, cornSprout.Mesh);
            Assert.Contains("Farmable", cornGrown.Mesh, StringComparison.Ordinal);
            // Corn's grown stage carries three ears (FruitMeshCount 3); a sprout has none.
            Assert.Equal(3, grown.Parts.Count(p => p.Name?.StartsWith("Plot2/Plant_Corn/Fruit", StringComparison.Ordinal) == true));
            Assert.Equal(plain.Parts.Count + 1, sprout.Parts.Count);
            // Spot 2 of a medium plot is its second planting square: +X and +Y of the centre (viewer X, Z).
            Assert.True(cornGrown.Matrix[12] > 0.3f && cornGrown.Matrix[14] > 0.3f, $"corn at {cornGrown.Matrix[12]}, {cornGrown.Matrix[14]}");
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Outdoor_ground_blends_the_terrain_layers_the_game_paints()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        // The Dam valley's material: base dirt plus a road texture, each with its own tiling.
        var layers = assets.UseFileProvider(p => TerrainMaterial.Read(p, "/Game/Textures/Landscape/M_ABF_LandscapeOutbackDirt.M_ABF_LandscapeOutbackDirt"));
        Assert.Equal(5, layers.Count);
        Assert.EndsWith("T_Ground_Dirt_Outback_05", layers[0].Texture!, StringComparison.Ordinal);
        Assert.EndsWith("T_Ground_Dirt_Outback", layers[1].Texture!, StringComparison.Ordinal);
        Assert.True(layers[1].RepeatMetres > layers[0].RepeatMetres);
        Assert.Null(layers[2].Texture);

        // A Dam terrain piece bakes with layer weights as vertex colours.
        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Dam.umap"));
        var terrain = index.Meshes.Where(LandscapeBaker.IsKey).ToList();
        Assert.NotEmpty(terrain);
        var baked = assets.UseFileProvider(p => LandscapeBaker.Load(p, terrain[0]) is { } c ? LandscapeBaker.Bake(c, 1) : null);
        Assert.NotNull(baked);
        Assert.Equal(SceneMeshFormat.FlagVertexColors, BinaryPrimitives.ReadUInt32LittleEndian(baked.AsSpan(16)) & SceneMeshFormat.FlagVertexColors);
    }

    [Fact]
    public void Water_tiles_by_its_own_scale_and_level_decals_are_indexed_as_quads()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        static ResolvedMaterial Of(GameAssetProvider a, string path) => a.UseFileProvider(p =>
            p.TryLoadPackageObject(path, out var o) ? MaterialResolver.Resolve(o as CUE4Parse.UE4.Assets.Exports.Material.UMaterialInterface) : ResolvedMaterial.Fallback);

        Assert.Equal(450f, Of(assets, "/Game/Textures/Liquid/M_WaterSurface_Dirty.M_WaterSurface_Dirty").TileCm);
        var frost = Of(assets, "/Game/Textures/Decals/M_Frost_02.M_Frost_02");
        Assert.True(frost.Decal);
        Assert.False(frost.Effect);
        Assert.Equal(1f, frost.Opacity);

        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Office1.umap"));
        var plane = index.Meshes.IndexOf(LevelIndex.DecalPlane);
        Assert.True(plane >= 0);
        var decals = index.Entries.Where(e => e.Mesh == plane && index.OverrideSets[e.Overrides].Any(m => m?.Contains("/Decals/", StringComparison.Ordinal) == true)).ToList();
        Assert.True(decals.Count > 50, $"only {decals.Count} decals");
        // Each decal quad is a rotation (no mirror), so lighting falls on the side facing the camera.
        Assert.All(decals, d => Assert.True(d.World.GetDeterminant() > 0));
    }

    [Fact]
    public void Liquid_containers_show_their_surface_at_the_saved_fill()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        const string Barrel = "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LiquidContainer_Barrel.Deployed_LiquidContainer_Barrel_C";
        PluginHostEnvironment.GameAssets = () => assets;
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-liquid-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new PakSceneModelProvider(new TestHost(dir));
            var empty = provider.DescribeClass(Barrel, new SceneObjectState(LiquidLevel: 0))!;
            var low = provider.DescribeClass(Barrel, new SceneObjectState(LiquidLevel: 1000))!;
            var full = provider.DescribeClass(Barrel, new SceneObjectState(LiquidLevel: 10000))!;
            Assert.DoesNotContain(empty.Parts, p => p.Name == LiquidFill.SurfaceComponent);
            var lowY = low.Parts.Single(p => p.Name == LiquidFill.SurfaceComponent).Matrix[13];
            var fullY = full.Parts.Single(p => p.Name == LiquidFill.SurfaceComponent).Matrix[13];
            // The barrel's surface runs from 1 cm to 97 cm (Liquid_FillLocationMin/Max), viewer Y is up in metres.
            Assert.InRange(fullY, 0.95f, 0.99f);
            Assert.InRange(lowY, 0.09f, 0.12f);
        }
        finally
        {
            PluginHostEnvironment.GameAssets = null!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        var key = AbioticEditor.Web.Services.SceneModelHostService.ParseModelKey(Barrel + "#liquid=2500#fluid=NewEnumerator16");
        Assert.Equal(2500, key.State!.LiquidLevel);
        Assert.Equal("NewEnumerator16", key.State.LiquidType);

        // The liquid picks its surface the way the game's RefreshLiquidTypeAppearance does: the enum
        // value (Ink is NewEnumerator16 = 13, Water NewEnumerator1 = 1) selects a switch case.
        Assert.EndsWith("M_Ink_Sink", assets.UseFileProvider(p => LiquidFill.SurfaceMaterial(p, Barrel, "E_LiquidType::NewEnumerator16"))!, StringComparison.Ordinal);
        Assert.EndsWith("M_Water_Sink", assets.UseFileProvider(p => LiquidFill.SurfaceMaterial(p, Barrel, "E_LiquidType::NewEnumerator1"))!, StringComparison.Ordinal);
        Assert.EndsWith("M_LiquidBlood_Red", assets.UseFileProvider(p => LiquidFill.SurfaceMaterial(p, Barrel, "E_LiquidType::NewEnumerator9"))!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_native_decoder_is_optional_and_never_throws()
    {
        if (NativeDecoder.Loaded) return; // another test loaded it in this process
        Assert.False(NativeDecoder.TryLoad(null));
        Assert.False(NativeDecoder.TryLoad(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"))));
    }

    /// <summary>
    /// With the native decoder loaded from a folder (as the plugin does from its own), animations
    /// compressed with ACL decode: the Dam's posed people (40 of 41 fail without it). Runs when
    /// CUE4PARSE_NATIVES_DIR names a folder holding a built CUE4Parse-Natives library.
    /// </summary>
    [Fact]
    public void Acl_compressed_poses_decode_once_the_native_decoder_is_loaded_from_a_folder()
    {
        if (Environment.GetEnvironmentVariable("CUE4PARSE_NATIVES_DIR") is not { Length: > 0 } dir) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        Assert.True(NativeDecoder.TryLoad(dir));

        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Dam.umap"));
        var keys = index.Meshes.Where(PoseBaker.IsKey).ToList();
        var (posed, failed) = assets.UseFileProvider(p =>
        {
            int ok = 0, bad = 0;
            foreach (var key in keys)
            {
                if (PoseBaker.Load(p, key) is not { } c || PoseBaker.PoseOf(c) is not { } pose) continue;
                var meshIndex = Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkeletalMesh", null)
                                ?? Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(c, "SkinnedAsset", null);
                if (meshIndex?.Load() is not CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh mesh) continue;
                try { if (PoseBaker.SkinMatrices(mesh, pose.Anim, pose.Time) is not null) ok++; }
                catch (DllNotFoundException) { bad++; }
            }
            return (ok, bad);
        });
        Assert.Equal(0, failed);
        Assert.True(posed > 30, $"only {posed} posed");
    }

    [Fact]
    public void Posed_corpses_in_the_level_are_skinned_in_their_pose()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        var index = assets.UseFileProvider(p => LevelIndex.Build(p, "AbioticFactor/Content/Maps/Facility_Office1.umap"));
        var posed = index.Meshes.Where(PoseBaker.IsKey).ToList();
        Assert.True(posed.Count > 5, $"only {posed.Count} posed meshes");

        // A posed component bakes, and its pose really moves the bones away from the rest pose.
        var (moved, baked) = assets.UseFileProvider(p =>
        {
            foreach (var key in posed)
            {
                if (PoseBaker.Load(p, key) is not { } component || PoseBaker.PoseOf(component) is not { } pose) continue;
                var meshIndex = Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(component, "SkeletalMesh", null)
                                ?? Props.Get<CUE4Parse.UE4.Objects.UObject.FPackageIndex?>(component, "SkinnedAsset", null);
                if (meshIndex?.Load() is not CUE4Parse.UE4.Assets.Exports.SkeletalMesh.USkeletalMesh mesh) continue;
                var skin = PoseBaker.SkinMatrices(mesh, pose.Anim, pose.Time)!;
                var maxShift = skin.Max(m => Math.Abs(m.M11 - 1) + Math.Abs(m.M22 - 1) + Math.Abs(m.M33 - 1) + m.Translation.Length());
                return (maxShift, PoseBaker.Bake(component, 1));
            }
            return (0f, (byte[]?)null);
        });
        Assert.NotNull(baked);
        Assert.True(moved > 0.1f, $"pose barely differs from the rest pose ({moved})");
    }

    private sealed class TestHost(string dir) : IPluginHost, IPluginLog
    {
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(2, 21);
        public string HostKind => "test";
        public IPluginLog Log => this;
        public IHostUi Ui => NullHostUi.Instance;
        public string DataDirectory => dir;
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
