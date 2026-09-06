namespace AbioticEditor.Tests;

/// <summary>
/// Guards the DOORS slice of "same UI components live as offline": the live-editing page must
/// render the very same <c>WorldDoorsTab</c> the file-backed editor uses (bound to the narrow
/// <c>IWorldDoorsSession</c> boundary), not a separate, duplicate live-only tab. Mirrors
/// <c>PlayerUiParityContractTests</c>'s style of asserting structure straight from source text.
/// </summary>
public sealed class LiveDoorsUiParityContractTests
{
    [Fact]
    public void LiveConnect_renders_the_shared_WorldDoorsTab_for_doors()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<WorldDoorsTab", source, StringComparison.Ordinal);
        Assert.Contains("IWorldDoorsSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<LiveDoorsTab", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDoorsTab_no_longer_exists_as_its_own_duplicate_component()
    {
        Assert.False(UiSource.Exists("Components", "World", "LiveDoorsTab.razor"));
    }

    [Fact]
    public void WorldDoorsTab_binds_to_the_shared_narrow_session_interface()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldDoorsTab.razor");
        Assert.Contains("IWorldDoorsSession Session", source, StringComparison.Ordinal);
        // Live has no meaning for "keep state" and no game-host authority of its own -
        // the shared tab must account for both rather than assuming a loaded file.
        Assert.Contains("AppliesImmediately", source, StringComparison.Ordinal);
        Assert.Contains("IsHost", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_and_LiveDoorsSession_both_implement_the_shared_doors_interface()
    {
        var worldSaveSession = UiSource.ReadAllText("Models", "WorldSaveSession.cs");
        Assert.Contains(": IWorldDoorsSession", worldSaveSession, StringComparison.Ordinal);

        var liveDoorsSession = UiSource.ReadAllText("Models", "LiveDoorsSession.cs");
        Assert.Contains(": IWorldDoorsSession", liveDoorsSession, StringComparison.Ordinal);
    }
}
