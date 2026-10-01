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
        foreach (var t in new[] { "inspect", "objects", "filters", "display" })
            Assert.Contains($"_sideTab == \"{t}\"", tab, StringComparison.Ordinal);
        Assert.Contains("[\"inspect\", \"objects\", \"filters\", \"display\", \"edit\"]",
            UiSource.ReadAllText("Components", "World", "WorldBases3DTab.Layout.razor.cs"), StringComparison.Ordinal);
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
}
