namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the live "Power Sockets" area (round 103), the same style
/// <c>WorldLiveButtonsAreaTests</c> uses - kept in its own file rather than added to the shared
/// <c>WorldLiveAreaParityContractTests</c> so a change here never collides with concurrent work on
/// other live areas' own entries there.
/// </summary>
public sealed class WorldLivePowerSocketsAreaTests
{
    [Fact]
    public void LivePowerSocketsFeatureSession_implements_the_same_interface_and_is_scoped_to_power_sockets_only()
    {
        var source = ModelSource("LivePowerSocketsFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("PowerSocketsFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_features_tab_for_power_sockets()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_powerSockets\" FeatureId=\"@LivePowerSocketsFeatureSession.PowerSocketsFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TabPowerSockets_resource_key_exists_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Live_TabPowerSockets", resources);
    }

    [Fact]
    public void Live_powersockets_lua_module_is_registered_in_the_areas_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("Scripts", "areas", "manifest.lua"));
        Assert.Contains("areas.powersockets", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("Scripts", "areas", "powersockets.lua")));
    }

    [Fact]
    public void Live_powersockets_lua_test_case_is_registered_in_the_test_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("tests", "cases", "manifest.lua"));
        Assert.Contains("\"powersockets\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("tests", "cases", "powersockets.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_powersockets_wire_shape()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("powersockets.list", doc, StringComparison.Ordinal);
        Assert.Contains("powersockets.set", doc, StringComparison.Ordinal);
    }

    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
