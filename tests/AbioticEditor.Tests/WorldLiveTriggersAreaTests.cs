namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the live "Triggers" area (round 102), the same style
/// <c>WorldLiveAreaParityContractTests</c> pins for portals/containment/traders - kept in its own
/// file rather than added to that shared one so a change here never collides with concurrent work
/// on other live areas' own entries there.
/// </summary>
public sealed class WorldLiveTriggersAreaTests
{
    [Fact]
    public void LiveTriggersFeatureSession_implements_the_same_interface_and_is_scoped_to_triggers_only()
    {
        var source = ModelSource("LiveTriggersFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("TriggersFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_features_tab_for_triggers()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_triggers\" FeatureId=\"@LiveTriggersFeatureSession.TriggersFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TabTriggers_resource_key_exists_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Live_TabTriggers", resources);
    }

    [Fact]
    public void Live_triggers_lua_module_is_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        Assert.Contains("areas.triggers", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "triggers.lua")));
    }

    [Fact]
    public void Live_triggers_lua_test_case_is_registered_in_the_test_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("tests", "cases", "manifest.lua"));
        Assert.Contains("\"triggers\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("tests", "cases", "triggers.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_triggers_wire_shape()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("triggers.list", doc, StringComparison.Ordinal);
        Assert.Contains("triggers.set", doc, StringComparison.Ordinal);
    }

    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
