namespace AbioticEditor.Tests;

/// <summary>
/// Source-contract check that the live-editing INVENTORY/TRANSMOG tabs are the SAME shared
/// components the file-backed player editor uses, not their own duplicate render - the product
/// requirement behind round 76's inventory/transmog slice ("we are supposed to be using the same
/// UI components as the offline editing capabilities"). See <see cref="PlayerUiParityContractTests"/>
/// for the sibling contract over the file-backed player editor's own tabs.
/// </summary>
public sealed class LiveInventoryUiParityTests
{
    [Fact]
    public void LiveConnect_renders_the_shared_player_inventory_and_transmog_tabs()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<PlayerInventoryTab Session=\"_inventory\"", source, StringComparison.Ordinal);
        Assert.Contains("<PlayerTransmogTab Session=\"_inventory\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_bespoke_live_inventory_tab_exists_any_more()
    {
        // Deleted in favour of the shared PlayerInventoryTab/PlayerTransmogTab above; a
        // re-introduced Live*Tab.razor for inventory would mean the pages diverged again.
        Assert.False(UiSource.Exists("Components", "Player", "LiveInventoryTab.razor"));
        Assert.False(UiSource.Exists("Components", "Player", "LiveTransmogTab.razor"));
    }

    [Fact]
    public void Player_inventory_and_transmog_tabs_bind_to_the_narrow_session_interfaces()
    {
        // Both tabs must depend on the host-neutral interface, not the concrete file-backed
        // PlayerSaveSession, or LiveConnect.razor could never bind its live session to them.
        var inventoryTab = UiSource.ReadAllText("Components", "Player", "PlayerInventoryTab.razor");
        var transmogTab = UiSource.ReadAllText("Components", "Player", "PlayerTransmogTab.razor");
        Assert.Contains("public IPlayerInventorySession Session", inventoryTab, StringComparison.Ordinal);
        Assert.Contains("public IPlayerTransmogSession Session", transmogTab, StringComparison.Ordinal);
        Assert.Contains("Session.AppliesImmediately", inventoryTab, StringComparison.Ordinal);
        Assert.Contains("Session.AppliesImmediately", transmogTab, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveInventorySession_implements_both_shared_session_interfaces()
    {
        var source = UiSource.ReadAllText("Models", "LiveInventorySession.cs");
        Assert.Contains("IPlayerInventorySession, IPlayerTransmogSession", source, StringComparison.Ordinal);
    }
}
