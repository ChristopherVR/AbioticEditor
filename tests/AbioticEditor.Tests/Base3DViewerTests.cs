using System.Xml.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Base-building phase 3 (the 3D viewer) and the UI half of phase 4 (staged move/rotate): the
/// coordinate conversion the viewer relies on, the filters, the session's staging and revert, the
/// save path, and the source-level wiring (vendored renderer, localization, hosts). Fixture-backed
/// tests skip when the fixture is absent, like the rest of the suite.
/// </summary>
public sealed class Base3DViewerTests
{
    // ---------- coordinate conversion ----------

    private static readonly Random Rng = new(20260928);

    private static PlacedQuaternion RandomQuaternion()
        => PlacedSceneSpace.Normalize(new PlacedQuaternion(Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5, Rng.NextDouble() - 0.5));

    private static void AssertVectorClose(PlacedVector expected, PlacedVector actual, double tolerance = 1e-9)
    {
        Assert.InRange(actual.X - expected.X, -tolerance, tolerance);
        Assert.InRange(actual.Y - expected.Y, -tolerance, tolerance);
        Assert.InRange(actual.Z - expected.Z, -tolerance, tolerance);
    }

    [Fact]
    public void Position_maps_to_viewer_metres_with_z_up_and_round_trips()
    {
        // Save (X forward, Y right, Z up, cm) -> viewer (x, y up, z, m).
        var viewer = PlacedSceneSpace.ToViewer(new PlacedVector(1000, 2000, 300));
        Assert.Equal(new PlacedVector(10, 3, 20), viewer);

        for (var i = 0; i < 200; i++)
        {
            var p = new PlacedVector((Rng.NextDouble() - 0.5) * 680_000, (Rng.NextDouble() - 0.5) * 680_000, (Rng.NextDouble() - 0.5) * 70_000);
            AssertVectorClose(p, PlacedSceneSpace.FromViewer(PlacedSceneSpace.ToViewer(p)), 1e-7);
        }
    }

    [Fact]
    public void Rotation_round_trips_exactly()
    {
        for (var i = 0; i < 200; i++)
        {
            var q = RandomQuaternion();
            var back = PlacedSceneSpace.FromViewer(PlacedSceneSpace.ToViewer(q));
            Assert.Equal(q, back); // sign flips and swaps only, so no rounding at all
        }
    }

    [Fact]
    public void Rotation_conversion_is_consistent_with_the_position_conversion()
    {
        // The property that actually proves the mapping: rotating a point in save space and then
        // converting equals converting first and rotating with the converted quaternion. It holds for
        // any rotation only if the quaternion map is the correct conjugation by the axis swap.
        for (var i = 0; i < 300; i++)
        {
            var q = RandomQuaternion();
            var v = new PlacedVector((Rng.NextDouble() - 0.5) * 10, (Rng.NextDouble() - 0.5) * 10, (Rng.NextDouble() - 0.5) * 10);
            var rotatedThenConverted = PlacedSceneSpace.ToViewer(PlacedSceneSpace.Rotate(q, v));
            var convertedThenRotated = PlacedSceneSpace.Rotate(PlacedSceneSpace.ToViewer(q), PlacedSceneSpace.ToViewer(v));
            AssertVectorClose(rotatedThenConverted, convertedThenRotated, 1e-9);
        }
    }

    [Fact]
    public void Save_yaw_turns_x_toward_y_which_is_a_negative_turn_about_viewer_up()
    {
        // In the save a positive yaw turns +X toward +Y. In the viewer +X stays +x and +Y becomes +z,
        // and turning +x toward +z is a NEGATIVE rotation about +Y in a right-handed frame.
        var yaw90 = PlacedSceneSpace.YawQuaternion(90);
        AssertVectorClose(new PlacedVector(0, 1, 0), PlacedSceneSpace.Rotate(yaw90, new PlacedVector(1, 0, 0)));
        Assert.Equal(90, yaw90.YawDegrees, 6);

        var viewerQ = PlacedSceneSpace.ToViewer(yaw90);
        Assert.Equal(0, viewerQ.X);
        Assert.Equal(0, viewerQ.Z);
        Assert.True(viewerQ.Y < 0, "a positive save yaw must be a negative rotation about viewer +Y");
        AssertVectorClose(new PlacedVector(0, 0, 1), PlacedSceneSpace.Rotate(viewerQ, new PlacedVector(1, 0, 0)));
    }

    [Fact]
    public void Axis_swap_is_a_reflection_so_handedness_changes()
    {
        // The scalar triple product of the axis images is the sign of the mapping's determinant. The
        // raw components of the save axes give +1; a proper rotation would keep it, a reflection flips
        // it. Left-handed to right-handed must be a reflection.
        static PlacedVector Cross(PlacedVector a, PlacedVector b)
            => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
        static double Dot(PlacedVector a, PlacedVector b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        var ex = new PlacedVector(1, 0, 0);
        var ey = new PlacedVector(0, 1, 0);
        var ez = new PlacedVector(0, 0, 1);
        Assert.True(Dot(Cross(ex, ey), ez) > 0);
        Assert.True(Dot(
            Cross(PlacedSceneSpace.ToViewer(ex), PlacedSceneSpace.ToViewer(ey)),
            PlacedSceneSpace.ToViewer(ez)) < 0);
    }

    [Fact]
    public void Scale_swaps_axes_without_changing_magnitude()
    {
        Assert.Equal(new PlacedVector(2, 4, 3), PlacedSceneSpace.ScaleToViewer(new PlacedVector(2, 3, 4)));
    }

    [Fact]
    public void ComposeYaw_matches_the_staged_model_rotation()
    {
        var q = PlacedSceneSpace.Normalize(new PlacedQuaternion(0.1, -0.2, 0.5, 0.8));
        var composed = PlacedSceneSpace.ComposeYaw(q, 30);

        // Same result as the Core staging model's own RotateYawBy (compared through the rotation of a vector).
        var probe = new PlacedVector(0.3, 0.7, -0.2);
        var viaStage = PlacedSceneSpace.Rotate(PlacedSceneSpace.YawQuaternion(30), PlacedSceneSpace.Rotate(q, probe));
        AssertVectorClose(viaStage, PlacedSceneSpace.Rotate(composed, probe), 1e-9);
    }

    // ---------- categories and filters ----------

    [Theory]
    [InlineData("Deployed_CraftingBench_Default_C", false, PlacedObjectCategory.Bench)]
    [InlineData("Deployed_Lamp_Wall_Crafted_C", false, PlacedObjectCategory.Light)]
    [InlineData("Deployed_Battery_T2_C", false, PlacedObjectCategory.Power)]
    [InlineData("Deployed_PlugStrip_C", false, PlacedObjectCategory.Power)]
    [InlineData("Deployed_StorageCrate_Makeshift_T3_C", true, PlacedObjectCategory.Container)]
    [InlineData("Deployed_Barricade_Plank_Full_C", false, PlacedObjectCategory.Structure)]
    [InlineData("Deployed_Furniture_Chair_Office_01_C", false, PlacedObjectCategory.Other)]
    [InlineData("Deployed_Furniture_Chair_Office_01_C", true, PlacedObjectCategory.Container)]
    [InlineData(null, false, PlacedObjectCategory.Unknown)]
    public void Categories_come_from_the_class_name(string? className, bool hasInventory, PlacedObjectCategory expected)
        => Assert.Equal(expected, PlacedObjectCategoryCatalog.Classify(className, hasInventory));

    private static PlacedObjectSummary Row(string key, string? cls, bool? built, double? z, string? name = null, int inventory = 0)
        => new(key, null, cls, PlacedClassOrigin.GameBlueprint, null, null,
            z is null ? null : new PlacedObjectTransform(new PlacedVector(0, 0, z.Value), PlacedQuaternion.Identity, new PlacedVector(1, 1, 1)),
            null, null, built, name, null, null, null, inventory, 0, [], []);

    [Fact]
    public void Scene_lists_objects_without_a_location_apart_and_never_at_the_origin()
    {
        var rows = new[]
        {
            Row("a", "Deployed_CraftingBench_Default_C", true, 100),
            Row("b", "Deployed_Lamp_Wall_Crafted_C", false, null),
            Row("c", "Deployed_PlugStrip_C", true, 5000),
        };
        var scene = Base3DScene.Build(rows, _ => null);

        Assert.Equal(["a", "c"], scene.Objects.Select(o => o.Key));
        var unresolved = Assert.Single(scene.Unresolved);
        Assert.Equal("b", unresolved.Key);
        Assert.DoesNotContain(scene.Objects, o => o.Key == "b");
        Assert.Equal(100, scene.MinZCm);
        Assert.Equal(5000, scene.MaxZCm);
    }

    [Fact]
    public void Filters_combine_origin_category_height_and_search()
    {
        var rows = new[]
        {
            Row("bench", "Deployed_CraftingBench_Default_C", true, 100),
            Row("lamp", "Deployed_Lamp_Wall_Crafted_C", false, 300),
            Row("plug", "Deployed_PlugStrip_C", true, 5000, "Kitchen strip"),
            Row("crate", "Deployed_StorageCrate_Makeshift_T3_C", true, 400, inventory: 2),
        };
        var scene = Base3DScene.Build(rows, _ => null);
        string[] Keys(Base3DFilter f) => scene.Apply(f).Select(i => scene.Objects[i].Key).ToArray();

        Assert.Equal(["bench", "lamp", "plug", "crate"], Keys(new Base3DFilter()));
        Assert.Equal(["bench", "plug", "crate"], Keys(new Base3DFilter { ShowLevelPlaced = false }));
        Assert.Equal(["lamp"], Keys(new Base3DFilter { ShowPlayerBuilt = false }));
        Assert.Equal(["bench", "lamp", "crate"], Keys(new Base3DFilter { HiddenCategories = new HashSet<PlacedObjectCategory> { PlacedObjectCategory.Power } }));
        Assert.Equal(["lamp", "crate"], Keys(new Base3DFilter { MinZCm = 200, MaxZCm = 1000 }));
        Assert.Equal(["plug"], Keys(new Base3DFilter { Search = "kitchen" }));
        Assert.Equal(["bench"], Keys(new Base3DFilter { Search = "craftingbench" }));
        Assert.Empty(Keys(new Base3DFilter { Search = "nothing matches this" }));
    }

    [Fact]
    public void Scene_uses_the_staged_transform_and_converts_it_to_viewer_space()
    {
        var rows = new[] { Row("a", "Deployed_CraftingBench_Default_C", true, 100) };
        var staged = new PlacedObjectTransform(new PlacedVector(500, 200, 150), PlacedSceneSpace.YawQuaternion(90), new PlacedVector(1, 1, 1));
        var scene = Base3DScene.Build(rows, key => key == "a" ? staged : null);

        var o = Assert.Single(scene.Objects);
        Assert.Equal([5.0, 1.5, 2.0], o.P);
        Assert.Equal(100, scene.SavedZCm[0]); // the height band still uses the saved height
    }

    // ---------- session staging and revert ----------

    private static string? SmallFacility()
        => Fixtures.ClientWorldSaves("WorldSave_Facility.sav")
            .OrderBy(p => new FileInfo(p).Length).FirstOrDefault();

    private sealed class TempCopy : IDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(Path.GetTempPath(), "abf-b3d-" + Guid.NewGuid().ToString("N"));
        public string SavePath { get; }

        public TempCopy(string source)
        {
            Directory.CreateDirectory(Folder);
            SavePath = System.IO.Path.Combine(Folder, "WorldSave_Facility.sav");
            File.Copy(source, SavePath);
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { }
        }
    }

    private static WorldSaveSession Open(string path) => new(WorldSaveReader.ReadFromFile(path), path);

    private static (PlacedObjectSummary Built, PlacedObjectSummary Other, PlacedObjectSummary Level) Pick(WorldSaveSession session)
    {
        bool Full(PlacedObjectSummary o) => o.Transform is { Translation: not null, Rotation: not null, Scale3D: not null };
        var built = session.PlacedObjects.Where(o => o.DeployedByPlayer == true && Full(o)).Take(2).ToArray();
        var level = session.PlacedObjects.First(o => o.DeployedByPlayer != true && Full(o));
        return (built[0], built[1], level);
    }

    [Fact]
    public void Staging_a_move_marks_the_session_dirty_and_revert_clears_it()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var session = Open(temp.SavePath);
        var (built, _, _) = Pick(session);
        Assert.False(session.IsDirty);
        var revision = session.PlacedTransformsRevision;

        var target = built.Transform!.Translation!.Value with { X = built.Transform.Translation.Value.X + 250 };
        var result = session.StagePlacedTransform(built.Key, target, null);

        Assert.True(result.Staged);
        Assert.True(session.IsDirty);
        Assert.True(session.HasStagedPlacedTransforms);
        Assert.True(session.PlacedTransformsRevision > revision);
        Assert.Equal(target, session.CurrentPlacedTransform(built.Key)!.Translation);
        Assert.Equal(built.Transform.Translation, session.FindPlacedObject(built.Key)!.Transform!.Translation); // saved value untouched

        var row = Assert.Single(session.PlacedTransformPreview());
        Assert.Equal(250, row.DistanceCm!.Value, 6);
        Assert.False(row.Blocked);

        Assert.True(session.RevertPlacedTransform(built.Key));
        Assert.False(session.IsDirty);
        Assert.Empty(session.PlacedTransformPreview());
        Assert.False(session.RevertPlacedTransform(built.Key));
    }

    [Fact]
    public void Revert_all_and_the_workspace_revert_both_clear_every_staged_move()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var session = Open(temp.SavePath);
        var (a, b, _) = Pick(session);
        foreach (var o in new[] { a, b })
            Assert.True(session.StagePlacedTransform(o.Key, o.Transform!.Translation!.Value with { Z = o.Transform.Translation.Value.Z + 10 }, null).Staged);
        Assert.Equal(2, session.PlacedTransformPreview().Count);

        session.RevertAllPlacedTransforms();
        Assert.False(session.IsDirty);

        session.StagePlacedTransform(a.Key, a.Transform!.Translation!.Value with { Y = 1 }, null);
        Assert.True(session.IsDirty);
        session.Revert(); // what the shell's REVERT button calls
        Assert.False(session.IsDirty);
        Assert.False(session.HasStagedPlacedTransforms);
    }

    [Fact]
    public void Level_placed_objects_and_unknown_keys_are_refused_and_nothing_is_staged()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var session = Open(temp.SavePath);
        var (_, _, level) = Pick(session);

        var refused = session.StagePlacedTransform(level.Key, level.Transform!.Translation!.Value with { X = 1 }, null);
        Assert.False(refused.Staged);
        Assert.Equal(PlacedTransformRefusal.LevelPlaced, refused.Refusal);
        Assert.Equal(PlacedTransformRefusal.NotFound, session.StagePlacedTransform("no-such-key", new PlacedVector(1, 2, 3), null).Refusal);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Staging_the_saved_value_again_is_a_no_op_and_does_not_light_the_unsaved_indicator()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var session = Open(temp.SavePath);
        var (built, _, _) = Pick(session);

        Assert.True(session.StagePlacedTransform(built.Key, built.Transform!.Translation, built.Transform.Rotation).Staged);
        Assert.False(session.IsDirty);
    }

    // ---------- the save path ----------

    [Fact]
    public async Task Save_writes_exactly_the_staged_transforms_and_keeps_a_backup()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var original = File.ReadAllBytes(temp.SavePath);
        var session = Open(temp.SavePath);
        var (a, b, level) = Pick(session);
        var before = session.PlacedObjects.ToDictionary(o => o.Key, o => o.Transform, StringComparer.Ordinal);

        var movedA = a.Transform!.Translation!.Value with { X = a.Transform.Translation.Value.X + 120.5, Y = a.Transform.Translation.Value.Y - 33.25 };
        var turnedB = PlacedSceneSpace.ComposeYaw(b.Transform!.Rotation!.Value, 45);
        Assert.True(session.StagePlacedTransform(a.Key, movedA, null).Staged);
        Assert.True(session.StagePlacedTransform(b.Key, null, turnedB).Staged);

        await session.SaveAsync();

        Assert.False(session.IsDirty);
        Assert.False(session.HasStagedPlacedTransforms);
        Assert.True(File.Exists(temp.SavePath + ".bak"), "the .bak backup must be kept");
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp.SavePath + ".bak")), "the backup must be the pre-save file");

        var written = File.ReadAllBytes(temp.SavePath);
        Assert.Equal(original.Length, written.Length);
        var changed = Enumerable.Range(0, original.Length).Count(i => original[i] != written[i]);
        Assert.InRange(changed, 1, 56 + 56); // at most one translation and one rotation worth of doubles

        var after = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(temp.SavePath)).Objects!
            .ToDictionary(o => o.Key, o => o.Transform, StringComparer.Ordinal);
        Assert.Equal(before.Count, after.Count);
        foreach (var (key, transform) in before)
        {
            if (key == a.Key)
            {
                Assert.Equal(movedA, after[key]!.Translation);
                Assert.Equal(transform!.Rotation, after[key]!.Rotation);
                Assert.Equal(transform.Scale3D, after[key]!.Scale3D);
            }
            else if (key == b.Key)
            {
                Assert.Equal(transform!.Translation, after[key]!.Translation);
                Assert.Equal(turnedB, after[key]!.Rotation);
                Assert.Equal(transform.Scale3D, after[key]!.Scale3D);
            }
            else
            {
                Assert.Equal(transform, after[key]); // every other object, level-placed included, is unchanged
            }
        }
        Assert.Equal(before[level.Key], after[level.Key]);

        // The session now shows the new saved location without a reload.
        Assert.Equal(movedA, session.FindPlacedObject(a.Key)!.Transform!.Translation);
        Assert.Equal(movedA.X, session.Deployables.First(d => d.Id == a.Key).X);
    }

    [Fact]
    public async Task A_save_with_no_staged_moves_leaves_the_file_byte_identical()
    {
        if (SmallFacility() is not { } source) return;
        using var temp = new TempCopy(source);
        var original = File.ReadAllBytes(temp.SavePath);
        var session = Open(temp.SavePath);
        var (a, _, _) = Pick(session);
        session.StagePlacedTransform(a.Key, a.Transform!.Translation!.Value with { X = 1 }, null);
        session.RevertAllPlacedTransforms();

        await session.SaveAsync(); // nothing is dirty, so nothing is written

        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp.SavePath)));
        Assert.False(File.Exists(temp.SavePath + ".bak"));
    }

    // ---------- resources, wiring and vendored files ----------

    private static HashSet<string> ResourceNames()
        => XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_3d_view_resource_used_by_the_component_exists_in_english()
    {
        var names = ResourceNames();
        var source = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor")
            + UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor");
        var used = System.Text.RegularExpressions.Regex.Matches(source, "\"(World3D_[A-Za-z_]+)\"").Select(m => m.Groups[1].Value)
            .Where(k => !k.EndsWith('_') && k != "World3D_Gizmo") // prefixes; the full keys are checked below
            .ToHashSet();
        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, names));

        // Keys built from a prefix in the component (category names, gizmo modes) must exist too.
        foreach (var category in PlacedObjectCategoryCatalog.All) Assert.Contains("World3D_Category_" + category, names);
        foreach (var mode in new[] { "Off", "Translate", "Rotate" }) Assert.Contains("World3D_Gizmo" + mode, names);
        foreach (var key in new[] { "WorldBases_ViewSwitch", "WorldBases_ViewMap", "WorldBases_View3D" }) Assert.Contains(key, names);
    }

    [Fact]
    public void English_3d_resources_have_no_em_dashes_and_other_languages_fall_back_to_english()
    {
        var doc = XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"));
        var mine = doc.Root!.Elements("data").Where(e => ((string)e.Attribute("name")!).StartsWith("World3D_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(mine);
        Assert.All(mine, e => Assert.DoesNotContain('\u2014',(string)e.Element("value")!));

        var english = HostLanguageService.ResourceFor("en", "World3D_MoveOptIn");
        Assert.Equal("Experimental: move objects", english);
        foreach (var language in new[] { "de", "es", "fr", "ru" })
            Assert.Equal(english, HostLanguageService.ResourceFor(language, "World3D_MoveOptIn"));
    }

    [Fact]
    public void The_3d_view_is_a_switch_inside_the_bases_tab_offered_only_with_the_models_plugin()
    {
        var surface = UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor");
        Assert.Contains("<WorldBases3DTab", surface, StringComparison.Ordinal);
        Assert.Contains("<WorldBasesTab", surface, StringComparison.Ordinal);
        // No separate world tab and no settings opt-in: the view comes with the optional plugin.
        Assert.DoesNotContain("\"bases3d\"", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable3DBaseView", surface, StringComparison.Ordinal);
        Assert.Contains("ThreeDView=\"@(GameModelsAvailable ?", surface, StringComparison.Ordinal);
        Assert.Contains("SceneModelHostService", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable3DBaseView", UiSource.ReadAllText("Components", "Pages", "Settings.razor"), StringComparison.Ordinal);

        // The Bases tab shows the Map / 3D switch only when handed a 3D view.
        var bases = UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor");
        Assert.Contains("@if (ThreeDView is not null)", bases, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"view-3d\"", bases, StringComparison.Ordinal);

        // The live host renders the Bases tab without a 3D view (no staged file session there).
        var live = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.DoesNotContain("ThreeDView", live, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_another_regions_save_frames_its_objects_and_reloads_its_level()
    {
        // Blazor reuses the 3D tab when another region's save is opened in the same place; without
        // a reset the camera stayed on the previous region and the new objects were off screen.
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("if (!ReferenceEquals(Session, _sceneSession))", tab, StringComparison.Ordinal);
        Assert.Contains("_framedOnce = false;", tab, StringComparison.Ordinal);
        Assert.Contains("_regionChanged = true;", tab, StringComparison.Ordinal);
        Assert.Contains("if (_view is not null && _regionChanged)", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_controls_are_opt_in_warned_and_limited_to_player_built_objects()
    {
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("World3D_MoveOptIn", tab, StringComparison.Ordinal);
        Assert.Contains("World3D_MoveWarning", tab, StringComparison.Ordinal);
        Assert.Contains("private bool _moveOptIn;", tab, StringComparison.Ordinal); // off by default
        Assert.Contains("DeployedByPlayer: true", tab, StringComparison.Ordinal);  // gizmo only for player-built
        Assert.Contains("Workspace.NotifyEdited()", tab, StringComparison.Ordinal); // unsaved-edit indicator like other tabs
        Assert.Contains("Session.StagePlacedTransform", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldSaveWriter", tab, StringComparison.Ordinal); // never writes directly

        var warning = XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx")).Root!.Elements("data")
            .First(e => (string)e.Attribute("name")! == "World3D_MoveWarning").Element("value")!.Value;
        Assert.Contains("NOT been verified in-game", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_and_duplicate_are_left_as_a_marked_extension_point()
    {
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("EXTENSION POINT (delete and duplicate)", tab, StringComparison.Ordinal);
        Assert.Contains("data-extension-point=\"placed-object-delete-duplicate\"", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void Three_js_is_vendored_locally_with_its_license_and_never_loaded_from_a_cdn()
    {
        var lib = UiSource.Resolve("wwwroot", "lib", "three");
        foreach (var file in new[] { "three.module.min.js", "three.core.min.js", "OrbitControls.min.js", "TransformControls.min.js", "LICENSE" })
            Assert.True(File.Exists(Path.Combine(lib, file)), file + " must be vendored");
        Assert.Contains("MIT License", File.ReadAllText(Path.Combine(lib, "LICENSE")), StringComparison.Ordinal);

        // Import specifiers between the vendored files are relative, so nothing needs an import map or the network.
        foreach (var file in Directory.GetFiles(lib, "*.js"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("from\"three\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("from 'three'", text, StringComparison.Ordinal);
        }

        var view = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.DoesNotContain("http://", view, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", view, StringComparison.Ordinal);
        Assert.Contains("./lib/three/three.module.min.js", view, StringComparison.Ordinal);

        var notices = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "THIRD-PARTY-NOTICES.txt"));
        Assert.Contains("three.js", notices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIT", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_stays_independent_of_the_renderer()
    {
        var core = Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Core");
        foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                                 && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("THREE.", File.ReadAllText(file), StringComparison.Ordinal);
        }
        Assert.False(Directory.EnumerateFiles(core, "three*", SearchOption.AllDirectories).Any());
    }
}
