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
    public void LiveConnect_wires_the_shared_tabs_and_the_dedicated_traders_tab()
    {
        var source = PagesSource("LiveConnect.razor");
        // Containment and world-teleporters reuse the exact same tab the file editor renders.
        Assert.Contains("<WorldContainmentTab Session=\"_containment\"", source, StringComparison.Ordinal);
        Assert.Contains("<WorldFeaturesTab Session=\"_portals\" FeatureId=\"@LivePortalsFeatureSession.PortalsFeatureId\"",
            source, StringComparison.Ordinal);
        // Traders got a dedicated tab instead (documented deviation - see LiveTradersTab.razor's
        // own header comment for why WorldTradersTab could not be reused safely here).
        Assert.Contains("<LiveTradersTab Session=\"_traders\"", source, StringComparison.Ordinal);
        // Entitlements and raw have no live equivalent at all.
        Assert.Contains("Live_EntitlementsOfflineOnly", source, StringComparison.Ordinal);
        Assert.Contains("Live_RawOfflineOnly", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_area_resource_keys_exist_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[]
        {
            "LiveTraders_Title", "LiveTraders_Intro", "LiveTraders_NotHostWarning", "LiveTraders_Unlock",
            "LiveContainment_OfflineWorldUnlocksNote", "Live_EntitlementsOfflineOnly", "Live_RawOfflineOnly",
            "Live_TabPortals", "Live_TabEntitlements",
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

    private static string WorldSource(string file) => UiSource.ReadAllText("Components", "World", file);
    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
