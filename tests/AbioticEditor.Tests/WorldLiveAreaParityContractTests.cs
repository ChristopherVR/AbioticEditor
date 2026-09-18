namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for round-76's live-editing slice (containment, traders, world
/// teleporters/portals, entitlements, raw): asserts the "offline tab is THE component used
/// live, bound to a narrow interface" pattern actually holds in source, the way
/// <c>PlayerUiParityContractTests</c> pins the native-to-Razor migration contract.
/// </summary>
public sealed class WorldLiveAreaParityContractTests
{
    [Fact]
    public void WorldContainmentTab_binds_to_the_narrow_containment_interface_not_the_concrete_session()
    {
        var source = WorldSource("WorldContainmentTab.razor");
        Assert.Contains("public IWorldContainmentSession Session", source, StringComparison.Ordinal);
        // The world-wide GlobalUnlocks sweep has no live UObject equivalent, so it takes a
        // second, optional, concrete-typed parameter instead of living on the shared interface.
        Assert.Contains("public WorldSaveSession? FileSession", source, StringComparison.Ordinal);
        Assert.Contains("FileSession is { HasWorldUnlocks: true }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_implements_both_live_editing_boundaries()
    {
        var source = ModelSource("WorldSaveSession.cs");
        Assert.Contains("IWorldContainmentSession", source, StringComparison.Ordinal);
        Assert.Contains("IWorldFeaturesSession", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveContainmentSession_implements_the_same_interface_as_the_file_session()
    {
        var source = ModelSource("LiveContainmentSession.cs");
        Assert.Contains(": IWorldContainmentSession", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePortalsFeatureSession_implements_the_same_interface_and_is_scoped_to_portals_only()
    {
        var source = ModelSource("LivePortalsFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("PortalsFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldFeaturesTab_binds_to_the_narrow_features_interface()
    {
        var source = WorldSource("WorldFeaturesTab.razor");
        Assert.Contains("public IWorldFeaturesSession Session", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_tabs_and_the_dedicated_chemistry_tab()
    {
        var source = PagesSource("LiveConnect.razor");
        // Containment, world-teleporters and traders reuse the exact same tab the file editor renders.
        Assert.Contains("<WorldContainmentTab Session=\"_containment\"", source, StringComparison.Ordinal);
        Assert.Contains("<WorldFeaturesTab Session=\"_portals\" FeatureId=\"@LivePortalsFeatureSession.PortalsFeatureId\"",
            source, StringComparison.Ordinal);
        Assert.Contains("<WorldTradersTab Session=\"_traders\"", source, StringComparison.Ordinal);
        // Chemistry benches still get a dedicated tab (a documented deviation - see that
        // component's own header comment for why the generic tab could not be reused safely
        // there).
        Assert.Contains("<LiveChemistryBenchTab Session=\"chemistrySession\"", source, StringComparison.Ordinal);
        // Neither Entitlements nor Raw JSON has a live equivalent, and neither is offered as a
        // dead placeholder tab live either - see LiveConnect.razor's own comment on why both are
        // omitted entirely there instead.
    }

    [Fact]
    public void WorldTradersTab_binds_to_the_narrow_traders_interface_not_the_concrete_session()
    {
        var source = WorldSource("WorldTradersTab.razor");
        Assert.Contains("public IWorldTradersSession Session", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_and_LiveTradersSession_implement_the_same_traders_boundary()
    {
        Assert.Contains("IWorldTradersSession", ModelSource("WorldSaveSession.cs"), StringComparison.Ordinal);
        Assert.Contains(": IWorldTradersSession", ModelSource("LiveTradersSession.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Live_area_resource_keys_exist_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[]
        {
            "LiveTraders_NotHostWarning",
            "LiveContainment_OfflineWorldUnlocksNote",
            "Live_TabPortals",
            "LiveChemistryBenches_Title", "LiveChemistryBenches_Intro", "LiveChemistryBenches_NotHostWarning",
            "LiveChemistryBenches_NoneFound", "LiveChemistryBenches_WouldProduce", "LiveChemistryBenches_NoMatchingRecipe",
        })
        {
            Assert.Contains(key, resources);
        }
    }

    [Fact]
    public void Live_area_lua_modules_are_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        foreach (var module in new[] { "areas.containment", "areas.traders", "areas.portals" })
        {
            Assert.Contains(module, manifest, StringComparison.Ordinal);
        }
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "containment.lua")));
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "traders.lua")));
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "portals.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_new_wire_shapes()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        foreach (var heading in new[] { "containment.list", "containment.set", "traders.list", "traders.unlock", "portals.list", "portals.set" })
        {
            Assert.Contains(heading, doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LiveElevatorsFeatureSession_implements_the_same_interface_and_is_scoped_to_elevators_only()
    {
        var source = ModelSource("LiveElevatorsFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("ElevatorsFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_elevators_feature_tab()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_elevators\" FeatureId=\"@LiveElevatorsFeatureSession.ElevatorsFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_elevators_resource_key_exists_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Live_TabElevators", resources);
    }

    [Fact]
    public void Live_elevators_lua_module_is_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        Assert.Contains("areas.elevators", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "elevators.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_elevators_wire_shapes()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        foreach (var heading in new[] { "elevators.list", "elevators.set" })
        {
            Assert.Contains(heading, doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WorldNpcsTab_binds_to_the_narrow_interface_plus_an_optional_live_creatures_session()
    {
        // Round 99: the old dedicated "Creatures" tab (LiveNpcsTab.razor) was folded into the
        // same shared story-character tab as a second chip - Creatures is optional (null offline,
        // and null live until connected) rather than a second required parameter.
        var source = WorldSource("WorldNpcsTab.razor");
        Assert.Contains("public IWorldNpcsSession Session", source, StringComparison.Ordinal);
        Assert.Contains("public LiveNpcSession? Creatures", source, StringComparison.Ordinal);
        Assert.False(File.Exists(UiSource.Resolve("Components", "World", "LiveNpcsTab.razor")),
            "LiveNpcsTab.razor should be deleted - its roster is now the Creatures chip inside WorldNpcsTab.");
    }

    [Fact]
    public void LiveConnect_wires_both_npc_sessions_into_the_merged_tab()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldNpcsTab Session=\"_narrativeNpcs\" Creatures=\"_npcs\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<LiveNpcsTab", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Merged_npcs_tab_resource_keys_exist_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[] { "WorldNpcs_SectionStory", "WorldNpcs_SectionCreatures", "WorldNpcs_CreaturesNeedLiveConnection" })
        {
            Assert.Contains(key, resources);
        }
        // The dedicated live-only "Creatures" tab button is gone - merged into the chip above.
        Assert.DoesNotContain("Live_TabWildlife", resources);
    }

    private static string WorldSource(string file) => UiSource.ReadAllText("Components", "World", file);
    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
