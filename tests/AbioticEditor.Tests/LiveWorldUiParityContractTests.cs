namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the "one shared component, both hosts" rule the live-editing page is
/// supposed to follow for quest flags, story progression and the world clock/weather: the file
/// editor's own <c>WorldFlagsTab</c>/<c>WorldStoryTab</c> must be the components LiveConnect.razor
/// renders, bound to the host-neutral <c>IWorldFlagsSession</c>/<c>IWorldStorySession</c>
/// boundaries - not a duplicate live-only tab. Style: <see cref="PlayerUiParityContractTests"/>.
/// </summary>
public sealed class LiveWorldUiParityContractTests
{
    [Fact]
    public void LiveConnect_renders_the_shared_flags_and_story_tabs()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<WorldFlagsTab", source, StringComparison.Ordinal);
        Assert.Contains("<WorldStoryTab", source, StringComparison.Ordinal);
        // Bound to the live sessions, not left rendering the file-only WorldSaveSession.
        Assert.Contains("Session=\"_flags\"", source, StringComparison.Ordinal);
        Assert.Contains("Session=\"_story\"", source, StringComparison.Ordinal);
        Assert.Contains("Flags=\"_flags\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_retired_live_only_duplicate_tabs_no_longer_exist()
    {
        Assert.False(UiSource.Exists("Components", "World", "LiveFlagsTab.razor"),
            "LiveFlagsTab.razor should be deleted now that WorldFlagsTab is shared with the live page.");
        Assert.False(UiSource.Exists("Components", "World", "LiveWorldTab.razor"),
            "LiveWorldTab.razor should be deleted now that WorldStoryTab absorbed the clock/weather section.");
        Assert.False(UiSource.Exists("Models", "LiveWorldStateSession.cs"),
            "LiveWorldStateSession.cs should be gone; LiveStorySession wraps LiveWorldStateChannel internally.");
    }

    [Fact]
    public void WorldFlagsTab_and_WorldStoryTab_are_bound_to_the_host_neutral_interfaces()
    {
        var flagsTab = UiSource.ReadAllText("Components", "World", "WorldFlagsTab.razor");
        Assert.Contains("IWorldFlagsSession Session", flagsTab, StringComparison.Ordinal);

        var storyTab = UiSource.ReadAllText("Components", "World", "WorldStoryTab.razor");
        Assert.Contains("IWorldStorySession Session", storyTab, StringComparison.Ordinal);
        Assert.Contains("IWorldFlagsSession Flags", storyTab, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_implements_both_shared_session_interfaces()
    {
        var session = UiSource.ReadAllText("Models", "WorldSaveSession.cs");
        Assert.Contains("IWorldFlagsSession", session, StringComparison.Ordinal);
        Assert.Contains("IWorldStorySession", session, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_sessions_implement_the_same_interfaces_as_the_file_session()
    {
        var liveFlags = UiSource.ReadAllText("Models", "LiveWorldFlagsSession.cs");
        Assert.Contains("IWorldFlagsSession", liveFlags, StringComparison.Ordinal);

        var liveStory = UiSource.ReadAllText("Models", "LiveStorySession.cs");
        Assert.Contains("IWorldStorySession", liveStory, StringComparison.Ordinal);
        // Read-only story chapter live: no grounded write path (see docs/reference/
        // live-editing-protocol.md, "story.get / story.set").
        Assert.Contains("CanSetStoryChapter => false", liveStory, StringComparison.Ordinal);
    }

    [Fact]
    public void Story_area_module_and_manifest_exist_and_are_wired_together()
    {
        var repoRoot = UiSource.RepositoryRoot;
        var storyLua = Path.Combine(repoRoot, "live-agent", "AbioticEditorLiveAgentLua", "Scripts", "areas", "story.lua");
        var manifestLua = Path.Combine(repoRoot, "live-agent", "AbioticEditorLiveAgentLua", "Scripts", "areas", "manifest.lua");
        Assert.True(File.Exists(storyLua), $"expected {storyLua} to exist");
        Assert.True(File.Exists(manifestLua), $"expected {manifestLua} to exist");
        Assert.Contains("areas.story", File.ReadAllText(manifestLua), StringComparison.Ordinal);
        Assert.Contains("story.get", File.ReadAllText(storyLua), StringComparison.Ordinal);
        Assert.Contains("story.set", File.ReadAllText(storyLua), StringComparison.Ordinal);
    }
}
