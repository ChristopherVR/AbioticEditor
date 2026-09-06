namespace AbioticEditor.Tests;

/// <summary>
/// Source-contract tests for round 76's live SPAWN and COMPANIONS areas, in the style of
/// <see cref="PlayerUiParityContractTests"/>: asserts the pieces that make
/// <c>PlayerSpawnTab.razor</c>/<c>PlayerCompanionsTab.razor</c> the SAME component rendered by
/// both the file editor and <c>LiveConnect.razor</c> are actually wired up, and that the two new
/// Lua area modules are registered and reference the exact hash-suffixed fields the file writers
/// already use for the same data.
/// </summary>
public sealed class LiveSpawnCompanionsContractTests
{
    [Fact]
    public void PlayerSpawnTab_binds_to_the_narrow_interface_and_hides_file_only_pickers_live()
    {
        var tab = PlayerSource("PlayerSpawnTab.razor");
        Assert.Contains("public IPlayerSpawnSession Session", tab, StringComparison.Ordinal);
        Assert.Contains("Session.SupportsWorldIntegration", tab, StringComparison.Ordinal);
        Assert.Contains("Session.SupportsLiveActions", tab, StringComparison.Ordinal);
        Assert.Contains("Session.LivePosition", tab, StringComparison.Ordinal);
        Assert.Contains("TeleportHereAsync", tab, StringComparison.Ordinal);
        Assert.Contains("ClaimRespawnTerminalAsync", tab, StringComparison.Ordinal);
        // Native/file-only behavior must still be intact - see PlayerUiParityContractTests'
        // own PlayerSpawnTab.razor row for the pre-existing half of this contract.
        Assert.Contains("RespawnTerminalCatalog", tab, StringComparison.Ordinal);
        Assert.Contains("SetSpawnToBed", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerCompanionsTab_binds_to_the_narrow_interface_and_commits_through_it()
    {
        var tab = PlayerSource("PlayerCompanionsTab.razor");
        Assert.Contains("public IPlayerCompanionsSession Session", tab, StringComparison.Ordinal);
        Assert.Contains("Session.ApplyPetAsync", tab, StringComparison.Ordinal);
        Assert.Contains("Session.RemovePetAsync", tab, StringComparison.Ordinal);
        Assert.Contains("Session.AppliesImmediately", tab, StringComparison.Ordinal);
        // Sending a pet to a world bed stays file-only.
        Assert.Contains("Session.SupportsWorldIntegration", tab, StringComparison.Ordinal);
        Assert.Contains("SendToBed", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerSaveSession_implements_both_new_narrow_interfaces()
    {
        var session = UiSource.ReadAllText("Models", "PlayerSaveSession.cs");
        Assert.Contains("IPlayerSpawnSession", session, StringComparison.Ordinal);
        Assert.Contains("IPlayerCompanionsSession", session, StringComparison.Ordinal);
        // File session never applies immediately and always has the disk-backed pickers.
        Assert.Contains("IPlayerSpawnSession.SupportsWorldIntegration => true", session, StringComparison.Ordinal);
        Assert.Contains("IPlayerCompanionsSession.SupportsWorldIntegration => true", session, StringComparison.Ordinal);
        Assert.Contains("IPlayerCompanionsSession.AppliesImmediately => false", session, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_wires_up_spawn_and_companions_tabs_and_offline_only_notes()
    {
        var page = UiSource.ReadAllText("Components", "Pages", "LiveConnect.razor");
        Assert.Contains("LivePlayerSpawnSession.ConnectAsync", page, StringComparison.Ordinal);
        Assert.Contains("LivePlayerCompanionsSession.ConnectAsync", page, StringComparison.Ordinal);
        Assert.Contains("<PlayerSpawnTab Session=\"_spawn\"", page, StringComparison.Ordinal);
        Assert.Contains("<PlayerCompanionsTab Session=\"_companions\"", page, StringComparison.Ordinal);
        // Achievements/raw data have no live equivalent - a shared note explains why instead of
        // rendering nothing.
        Assert.Contains("Live_OfflineOnlyNote", page, StringComparison.Ordinal);
        Assert.Contains("PlayerAchievements_SteamAchievements", page, StringComparison.Ordinal);
        Assert.Contains("PlayerRaw_RawSaveJson", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_resource_keys_exist_in_AppResources()
    {
        var resx = UiSource.ReadAllText("Localization", "AppResources.resx");
        foreach (var key in new[] { "LiveSpawn_Title", "LiveCompanions_Title", "Live_OfflineOnlyNote",
                     "PlayerSpawn_TeleportHere", "PlayerSpawn_SetAsMyRespawnPoint", "PlayerSpawn_UseCurrentPosition" })
        {
            Assert.Contains($"name=\"{key}\"", resx, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Spawn_and_companions_area_modules_are_registered_in_the_manifest()
    {
        var manifest = LuaSource("areas", "manifest.lua");
        Assert.Contains("\"areas.spawn\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"areas.companions\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Spawn_lua_uses_the_reference_mods_own_TeleportPlayer_call_shape()
    {
        var spawn = LuaSource("areas", "spawn.lua");
        Assert.Contains("handlers[\"spawn.get\"]", spawn, StringComparison.Ordinal);
        Assert.Contains("handlers[\"spawn.set\"]", spawn, StringComparison.Ordinal);
        // Verbatim call shape from AFUtils.TeleportPlayerToPlayer / LocationsManager.LoadLocation.
        Assert.Contains("TeleportPlayer(target, rotation, true, false)", spawn, StringComparison.Ordinal);
        Assert.Contains("K2_GetActorRotation", spawn, StringComparison.Ordinal);
        // TerminalRespawnID has no reference-mod precedent - found in the game's own class
        // layout only, so it must stay pcall-guarded.
        Assert.Contains("controller.TerminalRespawnID", spawn, StringComparison.Ordinal);
        Assert.Contains("pcall(function()", spawn, StringComparison.Ordinal);
    }

    [Fact]
    public void Companions_lua_reuses_the_exact_hash_suffixed_fields_the_file_writer_uses()
    {
        var companions = LuaSource("areas", "companions.lua");
        Assert.Contains("handlers[\"companions.list\"]", companions, StringComparison.Ordinal);
        Assert.Contains("handlers[\"companions.set\"]", companions, StringComparison.Ordinal);
        // The exact hash-suffixed field names PlayerSaveWriter.FullNames also uses for the same
        // struct members - see Serialization/Player/PlayerSaveWriter.cs.
        Assert.Contains("PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9", companions, StringComparison.Ordinal);
        Assert.Contains("CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8", companions, StringComparison.Ordinal);
        Assert.Contains("MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B", companions, StringComparison.Ordinal);
        // The genuinely new, unverified access path (see the file's own header comment).
        Assert.Contains("DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7", companions, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_editing_protocol_doc_documents_spawn_and_companions()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("## `spawn.get` / `spawn.set`", doc, StringComparison.Ordinal);
        Assert.Contains("## `companions.list` / `companions.set`", doc, StringComparison.Ordinal);
    }

    private static string PlayerSource(string relative) => UiSource.ReadAllText("Components", "Player", relative);

    private static string LuaSource(params string[] parts)
        => File.ReadAllText(Path.Combine(
            UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", "Scripts",
            Path.Combine(parts)));
}
