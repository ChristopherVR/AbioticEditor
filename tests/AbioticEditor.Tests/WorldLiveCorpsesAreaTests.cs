namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the live "Corpses" area (round 100), the same style
/// <c>WorldLiveAreaParityContractTests</c> pins for portals/containment/traders - kept in its own
/// file rather than added to that shared one so a change here never collides with concurrent work
/// on other live areas' own entries there.
/// </summary>
public sealed class WorldLiveCorpsesAreaTests
{
    [Fact]
    public void LiveCorpsesFeatureSession_implements_the_same_interface_and_is_scoped_to_corpses_only()
    {
        var source = ModelSource("LiveCorpsesFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("CorpsesFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_features_tab_for_corpses()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_corpses\" FeatureId=\"@LiveCorpsesFeatureSession.CorpsesFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TabCorpses_resource_key_exists_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Live_TabCorpses", resources);
    }

    [Fact]
    public void Live_corpses_lua_module_is_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        Assert.Contains("areas.corpses", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "corpses.lua")));
    }

    [Fact]
    public void Live_corpses_lua_test_case_is_registered_in_the_test_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("tests", "cases", "manifest.lua"));
        Assert.Contains("\"corpses\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("tests", "cases", "corpses.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_corpses_wire_shape()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("corpses.list", doc, StringComparison.Ordinal);
        Assert.Contains("corpses.remove", doc, StringComparison.Ordinal);
    }

    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
