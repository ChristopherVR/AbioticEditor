namespace AbioticEditor.Tests;

using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

/// <summary>
/// Structural contract for round-76's live RECIPES/CODEX/GENERAL editing: the same pattern
/// <c>IPlayerVitalsSession</c> established for vitals (see <c>PlayerVitals.cs</c>) - a narrow
/// interface implemented by both the file-backed <see cref="PlayerSaveSession"/> and a live
/// session, with the shared tab bound to that interface instead of the concrete file session.
/// Mirrors <see cref="PlayerUiParityContractTests"/>'s source-matching style for the Razor side.
/// </summary>
public sealed class LivePlayerProgressionParityContractTests
{
    [Fact]
    public void PlayerSaveSession_and_the_live_session_both_implement_IPlayerRecipesSession()
    {
        Assert.True(typeof(IPlayerRecipesSession).IsAssignableFrom(typeof(PlayerSaveSession)));
        Assert.True(typeof(IPlayerRecipesSession).IsAssignableFrom(typeof(LivePlayerRecipesSession)));
    }

    [Fact]
    public void PlayerSaveSession_and_the_live_session_both_implement_IPlayerCodexSession()
    {
        Assert.True(typeof(IPlayerCodexSession).IsAssignableFrom(typeof(PlayerSaveSession)));
        Assert.True(typeof(IPlayerCodexSession).IsAssignableFrom(typeof(LivePlayerCodexSession)));
    }

    [Fact]
    public void PlayerSaveSession_and_the_live_session_both_implement_IPlayerGeneralSession()
    {
        Assert.True(typeof(IPlayerGeneralSession).IsAssignableFrom(typeof(PlayerSaveSession)));
        Assert.True(typeof(IPlayerGeneralSession).IsAssignableFrom(typeof(LivePlayerGeneralSession)));
    }

    [Fact]
    public void File_session_can_lock_and_unset_known_but_live_sessions_cannot()
    {
        // The file session stages edits, so a recipe/codex toggle can always be flipped back
        // before Save - the live sessions can only ever go one direction (see
        // LivePlayerRecipesChannel/LivePlayerCodexChannel's remarks: no lock/relock/un-know
        // function exists anywhere in the running game's own component).
        if (Fixtures.CascadeDir is null) return;
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").First();
        var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
        Assert.True(((IPlayerRecipesSession)session).CanLock);
        Assert.True(((IPlayerCodexSession)session).CanUnsetKnown);
        Assert.False(((IPlayerRecipesSession)session).AppliesImmediately);
        Assert.False(((IPlayerCodexSession)session).AppliesImmediately);
    }

    [Fact]
    public void File_session_general_slice_allows_owner_id_changes_and_all_three_discovery_sections()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").First();
        var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
        Assert.True(((IPlayerGeneralSession)session).CanChangeOwnerId);
        Assert.True(((IPlayerGeneralSession)session).ItemsSeen.CanDiscoverAll);
        Assert.True(((IPlayerGeneralSession)session).ItemsCrafted.CanDiscoverAll);
        Assert.True(((IPlayerGeneralSession)session).Maps.CanDiscoverAll);
    }

    [Theory]
    [InlineData("PlayerRecipesTab.razor", "IPlayerRecipesSession")]
    [InlineData("PlayerCodexTab.razor", "IPlayerCodexSession")]
    [InlineData("PlayerGeneralTab.razor", "IPlayerGeneralSession")]
    public void Shared_tabs_bind_to_the_narrow_interface_not_the_concrete_file_session(string file, string interfaceName)
    {
        var source = UiSource.ReadAllText("Components", "Player", file);
        Assert.Contains(interfaceName, source, StringComparison.Ordinal);
        Assert.DoesNotContain("public PlayerSaveSession Session", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_renders_the_same_shared_tabs_the_file_editor_uses()
    {
        var source = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("<PlayerRecipesTab Session=\"_recipes\"", source, StringComparison.Ordinal);
        Assert.Contains("<PlayerCodexTab Session=\"_codex\"", source, StringComparison.Ordinal);
        Assert.Contains("<PlayerGeneralTab General=\"_general\" Recipes=\"_recipes\"", source, StringComparison.Ordinal);
        Assert.Contains("LivePlayerRecipesSession.ConnectAsync", source, StringComparison.Ordinal);
        Assert.Contains("LivePlayerCodexSession.ConnectAsync", source, StringComparison.Ordinal);
        Assert.Contains("LivePlayerGeneralSession.ConnectAsync", source, StringComparison.Ordinal);
    }
}
