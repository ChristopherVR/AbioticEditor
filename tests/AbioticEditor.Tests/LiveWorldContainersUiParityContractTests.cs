namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for the world-containers and dropped-items slice of live editing: the
/// offline <c>WorldContainersTab</c>/<c>WorldDroppedItemsTab</c> widgets are the SAME
/// components live editing renders (bound to <c>IWorldContainersSession</c>/
/// <c>IWorldDroppedItemsSession</c>), not a duplicate <c>Live*Tab</c> - mirrors the shared-tab
/// pattern already proven by <c>IPlayerVitalsSession</c>/<c>PlayerVitalsTab</c>. See
/// <c>LiveContainersSession</c>/<c>LiveDroppedItemsSession</c> and
/// <c>docs/reference/live-editing-protocol.md</c>.
/// </summary>
public sealed class LiveWorldContainersUiParityContractTests
{
    [Fact]
    public void LiveConnect_renders_the_shared_world_containers_and_dropped_items_tabs()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<WorldContainersTab", source, StringComparison.Ordinal);
        Assert.Contains("<WorldDroppedItemsTab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<LiveContainersTab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<LiveDroppedItemsTab", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_duplicate_live_container_or_dropped_item_tab_files_exist()
    {
        Assert.False(UiSource.Exists("Components", "World", "LiveContainersTab.razor"));
        Assert.False(UiSource.Exists("Components", "World", "LiveDroppedItemsTab.razor"));
    }

    [Fact]
    public void WorldContainersTab_and_WorldDroppedItemsTab_bind_to_the_narrow_session_interfaces()
    {
        var containersTab = UiSource.ReadAllText("Components", "World", "WorldContainersTab.razor");
        var droppedTab = UiSource.ReadAllText("Components", "World", "WorldDroppedItemsTab.razor");
        Assert.Contains("IWorldContainersSession Session", containersTab, StringComparison.Ordinal);
        Assert.Contains("IWorldDroppedItemsSession Session", droppedTab, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_and_the_live_sessions_implement_the_shared_container_interfaces()
    {
        var worldSaveSession = UiSource.ReadAllText("Models", "WorldSaveSession.cs");
        var liveContainers = UiSource.ReadAllText("Models", "LiveContainersSession.cs");
        var liveDropped = UiSource.ReadAllText("Models", "LiveDroppedItemsSession.cs");
        // The file session implements several shared interfaces on one declaration line, in
        // whatever order they were added - check membership, not the exact spelling.
        var declaration = worldSaveSession.Split((char)10).First(line => line.Contains("class WorldSaveSession", StringComparison.Ordinal));
        Assert.Contains("IWorldContainersSession", declaration, StringComparison.Ordinal);
        Assert.Contains("IWorldDroppedItemsSession", declaration, StringComparison.Ordinal);
        Assert.Contains(": IWorldContainersSession", liveContainers, StringComparison.Ordinal);
        Assert.Contains(": IWorldDroppedItemsSession", liveDropped, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_sessions_are_honest_about_what_only_the_file_session_supports()
    {
        // A live session applies immediately and only ever the running game's own host can
        // change it; the file-only mutators throw instead of silently no-opping.
        var liveDropped = UiSource.ReadAllText("Models", "LiveDroppedItemsSession.cs");
        Assert.Contains("AppliesImmediately => true", liveDropped, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", liveDropped, StringComparison.Ordinal);

        var fileSession = UiSource.ReadAllText("Models", "WorldSaveSession.cs");
        Assert.Contains("AppliesImmediately => false", fileSession, StringComparison.Ordinal);
    }
}
