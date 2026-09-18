namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the live "Elevators" area (round 79/95/97; round 125 fixed a retry-storm
/// bug and added the <c>powered</c> read-only field) - the same style <c>WorldLiveButtonsAreaTests</c>
/// uses for its own area. This area had no dedicated test file before round 125 (unlike every other
/// settable live area), which is likely exactly how the round-125 gap (a missing
/// <c>catch (LiveAgentException</c> in <c>SetMapFeatureField</c>) went unnoticed for as long as it did -
/// see <c>WorldLiveEditFailureContractTests</c> for the cross-area assertion that would have caught it.
/// </summary>
public sealed class WorldLiveElevatorsAreaTests
{
    [Fact]
    public void LiveElevatorsFeatureSession_implements_the_same_interface_and_is_scoped_to_elevators_only()
    {
        var source = ModelSource("LiveElevatorsFeatureSession.cs");
        Assert.Contains(": IWorldFeaturesSession", source, StringComparison.Ordinal);
        Assert.Contains("ElevatorsFeatureId", source, StringComparison.Ordinal);
        Assert.Contains("this feature has no live equivalent", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 125: the actual bug. <c>SetMapFeatureField</c> used to let the Lua handler's own
    /// refusal reach it as an uncaught <c>LiveAgentException</c> instead of a returned
    /// <c>WorldEditResult.Failure</c> - every sibling live area already caught this exception here
    /// (see <c>WorldLiveEditFailureContractTests</c> for the cross-area version of this same
    /// assertion). An uncaught exception skipped <c>WorldFeaturesTab.SetFieldAsync</c>'s own error
    /// handling and its revert, which is what produced the observed "elevator is not powered" toast
    /// repeating every couple of seconds instead of once (docs/PROGRESS.md's Round-125 entry).
    /// </summary>
    [Fact]
    public void LiveElevatorsFeatureSession_SetMapFeatureField_catches_LiveAgentException_from_the_channel()
    {
        var source = ModelSource("LiveElevatorsFeatureSession.cs");
        Assert.Contains("catch (LiveAgentException", source, StringComparison.Ordinal);
        Assert.Contains("SetTopOpenAsync", source, StringComparison.Ordinal);
    }

    /// <summary>Round 125: shown on the row so the player can see why a move might be refused
    /// (the game's own <c>elevators.set</c> already gates a press on <c>IsPowered()</c>) before
    /// clicking, not only from the refusal toast afterward.</summary>
    [Fact]
    public void LiveElevatorsFeatureSession_exposes_a_read_only_powered_field()
    {
        var source = ModelSource("LiveElevatorsFeatureSession.cs");
        Assert.Contains("\"powered\"", source, StringComparison.Ordinal);
        Assert.Contains("IsPowered", source, StringComparison.Ordinal);

        var channelSource = File.ReadAllText(Path.Combine(
            UiSource.RepositoryRoot, "src", "AbioticEditor.Core", "LiveEditing", "World", "LiveElevatorsChannel.cs"));
        Assert.Contains("Powered", channelSource, StringComparison.Ordinal);

        var luaSource = File.ReadAllText(LiveAgentPath("Scripts", "areas", "elevators.lua"));
        Assert.Contains("powered = readPowered(elevator)", luaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_the_shared_features_tab_for_elevators()
    {
        var source = PagesSource("LiveConnect.razor");
        Assert.Contains("<WorldFeaturesTab Session=\"_elevators\" FeatureId=\"@LiveElevatorsFeatureSession.ElevatorsFeatureId\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_TabElevators_resource_key_exists_in_AppResources()
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
    public void Live_elevators_lua_test_case_is_registered_in_the_test_manifest()
    {
        var manifest = File.ReadAllText(LiveAgentPath("tests", "cases", "manifest.lua"));
        Assert.Contains("\"elevators\"", manifest, StringComparison.Ordinal);
        Assert.True(File.Exists(LiveAgentPath("tests", "cases", "elevators.lua")));
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_elevators_wire_shape()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("elevators.list", doc, StringComparison.Ordinal);
        Assert.Contains("elevators.set", doc, StringComparison.Ordinal);
        Assert.Contains("powered", doc, StringComparison.Ordinal);
    }

    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
