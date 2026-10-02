using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// "Show in 3D" from the other world tabs, the story characters the game places more than once (two
/// Dr. Cahn in the Facility level), and the 3D view's loading readout and layout.
/// </summary>
public sealed class ShowIn3DTests
{
    [Fact]
    public void Map_keys_resolve_to_a_placed_object_a_level_actor_or_nothing()
    {
        var actor = WorldLocateTarget.ForMapKey("/Game/Maps/Facility.Facility:PersistentLevel.Button_Generic_C_5", "Button");
        Assert.Equal(WorldLocateKind.LevelActor, actor!.Kind);

        var placed = WorldLocateTarget.ForMapKey("8A3D3D6B4C65077B66342DB0E6860E86", "Bench");
        Assert.Equal(WorldLocateKind.PlacedObject, placed!.Kind);
        Assert.Equal("8A3D3D6B4C65077B66342DB0E6860E86", placed.Id);

        // An outlet record is keyed by its owner's key plus the outlet digit: it shows the owner.
        var outlet = WorldLocateTarget.ForMapKey("B1C5A4CF4B99D1C6297CB8B1E79ACBB92", "Outlet");
        Assert.Equal(WorldLocateKind.PlacedObject, outlet!.Kind);
        Assert.Equal("B1C5A4CF4B99D1C6297CB8B1E79ACBB9", outlet.Id);

        // Story triggers and named inventories have no place in the world.
        Assert.Null(WorldLocateTarget.ForMapKey("WF_NewGameStarted", "Trigger"));
        Assert.Null(WorldLocateTarget.ForMapKey("Boxy", "Inventory"));
    }

    [Fact]
    public void Every_world_tab_with_things_in_the_world_offers_show_in_3d()
    {
        foreach (var tab in new[]
                 {
                     "WorldDoorsTab.razor", "WorldContainersTab.razor", "WorldDroppedItemsTab.razor", "WorldNpcsTab.razor",
                     "WorldPetsTab.razor", "WorldVehiclesTab.razor", "WorldFeaturesTab.razor", "LiveChemistryBenchTab.razor",
                     "WorldBasesTab.razor",
                 })
        {
            Assert.Contains("<ShowIn3DButton", UiSource.ReadAllText("Components", "World", tab), StringComparison.Ordinal);
        }

        // The editor hands every tab the locator only when the 3D view exists, and a request
        // switches to the Bases tab, which opens the 3D view of the whole region.
        var surface = UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor");
        Assert.Contains("<CascadingValue Value=\"@(ThreeDViewAvailable", surface, StringComparison.Ordinal);
        Assert.Contains("Locate=\"@_locate\"", surface, StringComparison.Ordinal);
        var bases = UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor");
        Assert.Contains("_show3D = true;", bases, StringComparison.Ordinal);

        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("focusPoint(point, distance)", js, StringComparison.Ordinal);
        Assert.Contains("focusMarker(kind, id, distance)", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Models_wait_for_their_textures_and_the_view_reports_what_is_loading()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("await texturesReady(ready.flatMap(p => p.materials));", js, StringComparison.Ordinal);
        Assert.Contains("report(\"textures\"", js, StringComparison.Ordinal);
        Assert.Contains("TEXTURE_WAIT_MS", js, StringComparison.Ordinal); // a missing texture never holds a model back for good
        // A click that hits no piece takes the nearest one on screen (whole-region pieces are tiny).
        Assert.Contains("return best ?? nearestOnScreen(clientX, clientY);", js, StringComparison.Ordinal);

        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"loading\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"hud\"", tab, StringComparison.Ordinal);
        // The inspector is always shown above the tabs (picking never switches tabs); the lists are tabs.
        Assert.Contains("data-b3d=\"inspect-pane\"", tab, StringComparison.Ordinal);
        foreach (var t in new[] { "objects", "filters", "display" })
            Assert.Contains($"_sideTab == \"{t}\"", tab, StringComparison.Ordinal);
        Assert.Contains("[\"objects\", \"filters\", \"display\"]",
            UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Layout.razor.cs"), StringComparison.Ordinal);
        // A crate's contents are edited in the inspector, not by jumping to the Containers tab.
        Assert.Contains("<ContainerSlotsPanel Session=\"@Session\" ContainerId=\"@obj.Key\" />", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaving_the_3d_view_keeps_it_and_coming_back_loads_nothing_again()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("export function createView(host, dotnet, parkKey)", js, StringComparison.Ordinal);
        Assert.Contains("park(key) {", js, StringComparison.Ordinal);
        Assert.Contains("reattach(newHost, newDotnet) {", js, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("\"createView\", _host, _self, ParkKey", tab, StringComparison.Ordinal);
        Assert.Contains("await ParkViewAsync();", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokeVoidAsync(\"dispose\")", tab, StringComparison.Ordinal);
        // The Bases tab remembers Map or 3D per open save.
        Assert.Contains("Shown3D.TryGetValue(Session, out _)", UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Models_arrive_incrementally_and_the_first_read_starts_early_without_starving_the_window()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("if (!disposed) addArrivedModels();", js, StringComparison.Ordinal); // no full rebuild per batch
        Assert.Contains("const CLASS_BATCH = 24;", js, StringComparison.Ordinal);
        var service = UiSource.ReadAllText("Services", "SceneModelHostService.cs");
        Assert.Contains("Math.Max(2, Environment.ProcessorCount / 2)", service, StringComparison.Ordinal);
        Assert.Contains("public void Prewarm(", service, StringComparison.Ordinal);
        Assert.Contains("scene.Prewarm(", UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ground_items_and_level_things_are_clickable_and_zoom_reaches_details()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("const markerLayers = { door: doorLayer, npc: npcLayer, item: itemLayer, thing: thingLayer };", js, StringComparison.Ordinal);
        Assert.Contains("dotnet.invokeMethodAsync(\"OnMarkerPicked\"", js, StringComparison.Ordinal);
        Assert.Contains("controls.zoomToCursor = true;", js, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"dblclick\"", js, StringComparison.Ordinal);
        var markers = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Markers.razor.cs");
        Assert.Contains("[JSInvokable]", markers, StringComparison.Ordinal);
        Assert.Contains("\"buttons\", \"destructibles\", \"resource-nodes\"", markers, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"item-card\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"thing-card\"", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void Pieces_can_be_dragged_in_edit_mode_and_the_view_goes_full_screen()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("setDraggable(on) { draggable = !!on; },", js, StringComparison.Ordinal);
        Assert.Contains("function dragTo(e)", js, StringComparison.Ordinal);
        Assert.Contains("hoverLine", js, StringComparison.Ordinal); // what a click will pick is outlined
        Assert.Contains("list.length === 1 ? 0.6 : 2.5", js, StringComparison.Ordinal); // one piece fills the view
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("SelectedIsMovable", tab, StringComparison.Ordinal);
        Assert.Contains("_moveOptIn && _selected.Count <= 1 && SelectedObject is { DeployedByPlayer: true", tab, StringComparison.Ordinal); // only player-built, only in Edit mode
        Assert.Contains("data-b3d=\"fullscreen\"", tab, StringComparison.Ordinal);
        Assert.Contains("public Task OnEscapePressed()", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"level-card\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"edit-quick\"", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void World_lists_show_the_pictures_shipped_with_the_editor()
    {
        // One picture per kind: every blast door shows the BlastDoor_C picture.
        Assert.Equal("BlastDoor_C", WorldThumbnails.ClassOf("/Game/Maps/Facility.Facility:PersistentLevel.BlastDoor_C_11"));
        Assert.Equal("Resource_Micronode_LeyakEssence_TWO_C", WorldThumbnails.ClassOf("/Game/Maps/Facility.Facility:PersistentLevel.Resource_Micronode_LeyakEssence_TWO_C_2147459596"));
        Assert.Null(WorldThumbnails.For("doors", "/Game/Maps/Facility.Facility:PersistentLevel.NoSuchDoor_C_1"));
        Assert.Null(WorldThumbnails.KindOfFeature("triggers"));

        // Every picture the generated index lists is really shipped, so a list never shows a broken image.
        var root = UiSource.Resolve("wwwroot", "thumbs");
        Assert.True(Directory.Exists(root), "the rendered pictures are checked in under wwwroot/thumbs");
        foreach (var kind in new[] { "doors", "buttons", "destructibles", "elevators", "trams", "portals", "resource-nodes" })
        {
            var dir = Path.Combine(root, kind);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.webp"))
            {
                var cls = Path.GetFileNameWithoutExtension(file);
                Assert.NotNull(WorldThumbnails.For(kind, $"/Game/Maps/X.X:PersistentLevel.{cls}_1"));
            }
        }
        Assert.NotNull(WorldThumbnails.For("doors", "/Game/Maps/Facility.Facility:PersistentLevel.BlastDoor_C_11"));
        Assert.NotNull(WorldThumbnails.For("trams", "/Game/Maps/Facility.Facility:PersistentLevel.Tram_Default_C_1"));

        // Where each particular door, button or tram is: one picture per level actor, keyed by map and actor.
        Assert.Null(WorldThumbnails.PlaceOf("/Game/Maps/Nowhere.Nowhere:PersistentLevel.NoSuchDoor_C_1"));
        var places = Path.Combine(root, "places");
        if (Directory.Exists(places))
        {
            foreach (var mapDir in Directory.GetDirectories(places))
            {
                var map = Path.GetFileName(mapDir);
                foreach (var file in Directory.GetFiles(mapDir, "*.webp").Take(20))
                {
                    var actor = Path.GetFileNameWithoutExtension(file);
                    Assert.Equal($"_content/AbioticEditor.Web.Shared/thumbs/places/{map}/{actor}.webp",
                        WorldThumbnails.PlaceOf($"/Game/Maps/{map}.{map}:PersistentLevel.{actor}"));
                }
            }
        }

        var doors = UiSource.ReadAllText("Components", "World", "WorldDoorsTab.razor");
        Assert.Contains("WorldThumbnails.For(\"doors\", door.Id)", doors, StringComparison.Ordinal);
        Assert.Contains("WorldThumbnails.PlaceOf(door.Id)", doors, StringComparison.Ordinal);
        Assert.Contains("WorldThumbnails.KindOfFeature(FeatureId)", UiSource.ReadAllText("Components", "World", "WorldFeaturesTab.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_opening_prepares_the_whole_region_level_in_the_background()
    {
        var service = UiSource.ReadAllText("Services", "SceneModelHostService.cs");
        Assert.Contains("PrewarmLevel(region, levelCentres())", service, StringComparison.Ordinal);
        Assert.Contains("is { PendingMaps: > 0 }", service, StringComparison.Ordinal); // every sub-level indexed first
        var surface = UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor");
        Assert.Contains("BaseDetector.Detect(world.Deployables)", surface, StringComparison.Ordinal);
        Assert.Contains("GetWorldDoorPositionsForMapAsync(map)", surface, StringComparison.Ordinal);
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("if (pending.length >= 24) flushing = flushing.then(flush);", js, StringComparison.Ordinal); // level shown in batches
    }

    [Fact]
    public void Moving_around_is_smooth_and_the_view_controls_are_compact()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("controls.enableDamping = true;", js, StringComparison.Ordinal);
        Assert.Contains("function markMoving()", js, StringComparison.Ordinal); // lower resolution only while moving
        Assert.Contains("function flyStep(now)", js, StringComparison.Ordinal); // W A S D / Q E without walking
        Assert.Contains("Math.min(60, Math.max(2, distance * 0.5))", js, StringComparison.Ordinal);
        Assert.Contains("requestPointerLock", js, StringComparison.Ordinal); // walk looks with the mouse
        Assert.Contains("walkRamp", js, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"show-popover\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"help-card\"", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void Traders_containment_cells_and_side_panel_cards_reach_the_3d_view_in_their_own_save()
    {
        // A target can name the save it stands in; "Show in 3D" opens that save first.
        var surface = UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor");
        Assert.Contains("await Workspace.SelectAsync(save.Path);", surface, StringComparison.Ordinal);
        Assert.Contains("$\"WorldSave_{name}.sav\"", surface, StringComparison.Ordinal); // a level's region save, or a broader one
        Assert.Contains("(!world.IsMetadataSave && ThreeDViewAvailable)", surface, StringComparison.Ordinal); // every region has the 3D view
        Assert.Contains("Locators.Current = ThreeDViewAvailable ? Locator3D : null;", surface, StringComparison.Ordinal);
        Assert.Contains("Locators.Current", UiSource.ReadAllText("Components", "World", "ShowIn3DButton.razor"), StringComparison.Ordinal);

        Assert.Contains("Items.FindNarrativeNpcs(TraderWords(trader, detailLore))", UiSource.ReadAllText("Components", "World", "WorldTradersTab.razor"), StringComparison.Ordinal);
        Assert.Contains("SaveFileName = unit.RegionSaveFileName", UiSource.ReadAllText("Components", "World", "WorldContainmentTab.razor"), StringComparison.Ordinal);
        Assert.Contains("WorldLocateKind.LevelActor, selected.Id", UiSource.ReadAllText("Components", "World", "WorldNpcsTab.razor"), StringComparison.Ordinal); // holograms

        using var catalog = new ItemCatalogService();
        var blacksmith = catalog.FindNarrativeNpcs(["Blacksmith"]);
        if (blacksmith.Count > 0) Assert.All(blacksmith, b => Assert.Equal("Facility_MFWest", b.Level));
        Assert.DoesNotContain(catalog.FindNarrativeNpcs(["Warren"]), w => w.Level.StartsWith("MainMenu", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_in_3d_waits_for_the_view_and_wall_sockets_can_be_wired_in_it()
    {
        var locate = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Locate.razor.cs");
        Assert.Contains("private async Task LiftCurtainWhenLoadedAsync()", locate, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"curtain\"", tab, StringComparison.Ordinal);
        Assert.Contains("<PowerRewirePanel Session=\"Session\" SocketKey=\"@thing.Key\"", tab, StringComparison.Ordinal);
        Assert.Contains("\"power-sockets\"]", UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Markers.razor.cs"), StringComparison.Ordinal);
        // The power check runs by itself: no button.
        var repair = UiSource.ReadAllText("Components", "World", "PowerRepairPanel.razor");
        Assert.DoesNotContain("data-power=\"check\"", repair, StringComparison.Ordinal);
        Assert.Contains("protected override async Task OnParametersSetAsync()", repair, StringComparison.Ordinal);
    }

    [Fact]
    public void The_distillery_history_offers_only_what_a_distillery_accepts()
    {
        var registry = GameDataRegistry.LoadBundled();
        Assert.NotNull(registry?.Distillations);
        Assert.InRange(registry!.Distillations!.Count, 100, 400);
        Assert.Contains(registry.Distillations, r => r.Input == "sugarcrystal" && r.Output == "distillation_fizzy" && r.Count == 8);
        var section = UiSource.ReadAllText("Components", "Player", "PlayerDistilledSection.razor");
        Assert.Contains("Catalog.Distillations", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_Dr_Cahn_placements_are_told_apart_by_area()
    {
        var registry = GameDataRegistry.LoadBundled();
        if (registry?.NarrativeNpcPlacements is not { Count: > 0 } placements) return;

        var residence = NarrativeNpcNameCatalog.ResolvePlacement(placements, "/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_ParentBP_C_0");
        var security = NarrativeNpcNameCatalog.ResolvePlacement(placements, "/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_ParentBP_C_2");
        Assert.Equal("Res_Cahn_Res", residence!.Row);
        Assert.Equal("Residence_IceWallRemoved", residence.DisappearFlag);
        Assert.Equal("SECURITY_Cahn_2", security!.Row);
        Assert.Equal("Security_ExitOpened", security.AppearFlag);

        using var catalog = new ItemCatalogService();
        var npcs = new[]
        {
            new WorldNpc("/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_ParentBP_C_0", false, null),
            new WorldNpc("/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_ParentBP_C_1", false, null),
            new WorldNpc("/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_ParentBP_C_2", false, null),
        };
        var names = npcs.Select(n => catalog.GetNarrativeNpcDisplayName(n, npcs, (name, area) => $"{name} ({area})")).ToList();
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(names, n => n!.EndsWith("(Residence)", StringComparison.Ordinal));
        Assert.Contains(names, n => n!.EndsWith("(Security)", StringComparison.Ordinal));
        // A name nobody else shares is left as it is.
        Assert.DoesNotContain("(", names[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_3d_view_fits_the_window_and_draws_the_level_by_default()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("export function fitToWindow(root)", js, StringComparison.Ordinal);
        Assert.Contains("followLevel() { return followLevelNow(); }", js, StringComparison.Ordinal); // the level follows the view
        Assert.Contains("function levelFloorUnder(at)", js, StringComparison.Ordinal); // the cut sits above the real floor
        Assert.Contains("async classThumbnail(options)", js, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("private bool _levelOn = true;", tab, StringComparison.Ordinal);
        Assert.Contains("\"fitToWindow\", _root", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"bar\"", UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor"), StringComparison.Ordinal);
        Assert.Contains("await _view.InvokeAsync<bool>(\"followLevel\")", UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Locate.razor.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_picked_piece_shows_its_contents_first_and_level_pieces_copy_as_your_own()
    {
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"level-copy\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"details\"", tab, StringComparison.Ordinal); // file paths and numbers folded away
        Assert.True(tab.IndexOf("<ContainerSlotsPanel Session=\"@Session\" ContainerId=\"@obj.Key\" />", StringComparison.Ordinal)
                    < tab.IndexOf("data-b3d=\"details\"", StringComparison.Ordinal));
        Assert.DoesNotContain("World3D_EditNeedsOptIn", tab, StringComparison.Ordinal); // remove and copy turn Edit mode on themselves
        var place = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Place.razor.cs");
        Assert.Contains("await Session.FindDonorAsync(obj.ClassPath)", place, StringComparison.Ordinal);
        Assert.Contains("if (!_moveOptIn) await SetMoveOptInAsync(true);", place, StringComparison.Ordinal);
    }

    [Fact]
    public void Containers_built_into_the_level_have_pictures_and_money_is_green()
    {
        Assert.NotNull(WorldThumbnails.For("containers", "Container_Locker_C"));
        Assert.NotNull(WorldThumbnails.For("containers", "Deployed_Refrigerator_C"));
        Assert.Null(WorldThumbnails.For("containers", "Not_A_Real_Container_C"));
        Assert.Contains("WorldThumbnails.For(\"containers\", container.ClassName)", UiSource.ReadAllText("Components", "World", "WorldContainersTab.razor"), StringComparison.Ordinal);
        Assert.Contains("(\"money\",         MoneyGreen)", File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Core", "Infrastructure", "GameAssets", "IconColorizer.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Game_file_reads_for_the_3d_view_do_not_queue_behind_the_background_work()
    {
        var root = UiSource.RepositoryRoot;
        var host = File.ReadAllText(Path.Combine(root, "src", "AbioticEditor.Web.Shared", "Services", "SceneModelHostService.cs"));
        Assert.Contains("private void YieldToView()", host, StringComparison.Ordinal);
        Assert.Contains("private readonly SemaphoreSlim _actorReads", File.ReadAllText(Path.Combine(root, "src", "AbioticEditor.Web.Shared", "Services", "GameArtService.cs")), StringComparison.Ordinal);
        Assert.Contains("TryGetKnownActorPosition(actorObjectPath, out var known)", File.ReadAllText(Path.Combine(root, "src", "AbioticEditor.Core", "Infrastructure", "GameAssets", "GameAssetProvider.cs")), StringComparison.Ordinal);
        Assert.Contains("GameMaps.WorldOf(package)", File.ReadAllText(Path.Combine(root, "plugins", "GameModels3D", "LevelIndex.cs")), StringComparison.Ordinal);
        Assert.Contains("LoadWorld(r) ?? SaveWorld(r, BuildWorld(r))", File.ReadAllText(Path.Combine(root, "plugins", "GameModels3D", "PakSceneModelProvider.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Spawn_points_show_the_creature_the_game_says_they_spawn()
    {
        var registry = GameDataRegistry.LoadBundled();
        Assert.NotNull(registry?.NpcSpawnCreatures);
        Assert.Equal("NPC_Gatekeeper_Chieftain", NpcSpawnCatalog.CreatureOf(registry!.NpcSpawnCreatures, "/Game/Maps/Facility.Facility:PersistentLevel.NPCSpawn_Gatekeeper_Chieftain_C_3"));
        using var catalog = new ItemCatalogService();
        Assert.Equal("NPC_Gatekeeper_Grunt", catalog.SpawnedCreature("/Game/Maps/Facility.Facility:PersistentLevel.NPCSpawn_Gatekeeper_Phyter_C_1")?.Class);
    }

    [Fact]
    public void The_3d_card_edits_level_things_and_shows_characters_like_the_traders_tab()
    {
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("<FeatureEntryEditor Session=\"Session\" FeatureId=\"@thing.FeatureId\"", tab, StringComparison.Ordinal);
        Assert.Contains("<CharacterCard3D Npc=\"@npc\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"other-area\"", tab, StringComparison.Ordinal); // a socket kept in another area's save
        var markers = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Markers.razor.cs");
        Assert.Contains("private async Task<bool> PickLevelActorAsync(string mapActor)", markers, StringComparison.Ordinal); // a wall plug opens its socket
        Assert.Contains("Art.TryGetActorWorldTransformsAsync(", markers, StringComparison.Ordinal); // grouped by level file
        Assert.Contains("FeatureFieldText.Label(L, FeatureId, entry, field)", UiSource.ReadAllText("Components", "World", "WorldFeaturesTab.razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_draws_fast_and_editing_feels_like_a_base_builder()
    {
        var js = UiSource.ReadAllText("wwwroot", "base3d.js");
        Assert.Contains("async function mergeLevel(levelLoad)", js, StringComparison.Ordinal); // BatchedMesh per material
        Assert.Contains("const POINT_POOL = 4, SPOT_POOL = 2;", js, StringComparison.Ordinal); // a fixed light count: no recompiles
        Assert.Contains("function tuneMovingRatio(now)", js, StringComparison.Ordinal);
        Assert.Contains("function unblockView()", js, StringComparison.Ordinal); // a clear line of sight after a jump
        Assert.Contains("function levelCeilingAbove(at, floorY)", js, StringComparison.Ordinal);
        Assert.Contains("const stairClip", js, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"pointerlockchange\"", js, StringComparison.Ordinal); // Escape ends walking
        Assert.Contains("const SNAP_M = 0.1;", js, StringComparison.Ordinal);
        Assert.Contains("\"OnGroupDragged\"", js, StringComparison.Ordinal);
        Assert.Contains("export async function fillMapBackdrop(element, key, options)", js, StringComparison.Ordinal); // the 2D map's level picture
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("public async Task OnDuplicateKey()", tab, StringComparison.Ordinal);
        Assert.Contains("public async Task OnRotateKey(double degrees)", tab, StringComparison.Ordinal);
        Assert.Contains("data-map=\"backdrop\"", UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor"), StringComparison.Ordinal);
        Assert.NotNull(WorldThumbnails.For("power-sockets", "/Game/Maps/Facility.Facility:PersistentLevel.PowerSocket_ParentBP_C_4"));
    }
}
