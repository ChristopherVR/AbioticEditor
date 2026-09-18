namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the live "Resource Nodes" area (round 101), the same style
/// <c>WorldLiveButtonsAreaTests</c>/<c>WorldLiveAreaParityContractTests</c> pin for the other live
/// world-map features - kept in its own file rather than added to the shared one so a change here
/// never collides with concurrent work on other live areas' own entries there.
/// </summary>
public sealed class WorldLiveResourceNodesAreaTests
{
    [Fact]
    public void LiveResourceNodesFeatureSession_implements_the_same_interface_and_is_scoped_to_resource_nodes_only()
    {
        var source = ModelSource("LiveResourceNodesFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("ResourceNodesFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_features_tab_for_resource_nodes()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_resourceNodes\" FeatureId=\"@LiveResourceNodesFeatureSession.ResourceNodesFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_does_not_auto_refresh_resource_nodes_on_the_periodic_loop()
    {
        // The area is deliberately absent from ActiveLiveSessions' switch - a region can carry
        // far more resource nodes than any other live world area, so only an explicit tab visit
        // or a region change re-fetches it. Assert there is no `case "resourcenodes":` entry
        // inside that specific switch, not merely that the string is absent from the whole file
        // (EnsureAreaConnectedAsync legitimately has its own `case "resourcenodes":`).
        var source = PagesSource("LiveConnect.razor").Replace("\r\n", "\n", StringComparison.Ordinal);
        var activeSessionsStart = source.IndexOf("private IEnumerable<object?> ActiveLiveSessions()", StringComparison.Ordinal);
        Assert.True(activeSessionsStart >= 0, "ActiveLiveSessions method not found");
        var methodEnd = source.IndexOf("\n    }\n", activeSessionsStart, StringComparison.Ordinal);
        Assert.True(methodEnd > activeSessionsStart, "could not find the end of ActiveLiveSessions");
        var body = source[activeSessionsStart..methodEnd];
        Assert.DoesNotContain("case \"resourcenodes\":", body, StringComparison.Ordinal);
        Assert.Contains("resourcenodes deliberately has NO case here", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TabResourceNodes_resource_key_exists_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Live_TabResourceNodes", resources);
    }

    [Fact]
    public void Live_resourcenodes_lua_module_is_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        Assert.Contains("areas.resourcenodes", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "resourcenodes.lua")));
    }

    [Fact]
    public void Live_resourcenodes_lua_test_case_is_registered_in_the_test_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("tests", "cases", "manifest.lua"));
        Assert.Contains("\"resourcenodes\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("tests", "cases", "resourcenodes.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_resourcenodes_wire_shape()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("resourcenodes.list", doc, StringComparison.Ordinal);
        Assert.Contains("resourcenodes.set", doc, StringComparison.Ordinal);
    }

    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
