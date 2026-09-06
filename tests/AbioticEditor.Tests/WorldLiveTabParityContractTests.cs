namespace AbioticEditor.Tests;

/// <summary>
/// Structural contract for round 76: BASES/VEHICLES/PETS must reuse the exact same
/// <c>WorldBasesTab</c>/<c>WorldVehiclesTab</c>/<c>WorldPetsTab</c> Razor components in both the
/// file editor and LiveConnect, bound to a narrow interface both <c>WorldSaveSession</c> and a
/// live session implement - the pattern <c>IPlayerVitalsSession</c>/<c>PlayerVitalsTab</c> set.
/// Guards against a future change quietly forking a separate "Live&lt;Area&gt;Tab" the way the
/// round-75 areas did, which would leave the two editors' behaviour free to drift apart.
/// </summary>
public sealed class WorldLiveTabParityContractTests
{
    [Theory]
    [InlineData("WorldBasesTab.razor", "IWorldBasesSession")]
    [InlineData("WorldVehiclesTab.razor", "IWorldVehiclesSession")]
    [InlineData("WorldPetsTab.razor", "IWorldPetsSession")]
    public void Shared_world_tab_binds_to_the_narrow_session_interface_not_the_concrete_file_session(string file, string sessionInterface)
    {
        var source = WorldSource(file);
        Assert.Contains($"public {sessionInterface} Session", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public WorldSaveSession Session", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveConnect_renders_the_same_shared_bases_vehicles_and_pets_tabs_as_the_file_editor()
    {
        var liveConnect = PagesSource("LiveConnect.razor");
        foreach (var tab in new[] { "WorldBasesTab", "WorldVehiclesTab", "WorldPetsTab" })
            Assert.Contains($"<{tab} Session=", liveConnect, StringComparison.Ordinal);

        // Every world area connects independently and degrades gracefully (round-75 pattern),
        // BASES/VEHICLES/PETS included.
        Assert.Contains("_bases = await TryConnectWorldAreaAsync", liveConnect, StringComparison.Ordinal);
        Assert.Contains("_vehicles = await TryConnectWorldAreaAsync", liveConnect, StringComparison.Ordinal);
        Assert.Contains("_pets = await TryConnectWorldAreaAsync", liveConnect, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldSaveSession_implements_all_three_narrow_world_session_interfaces()
    {
        var source = ModelsSource("WorldSaveSession.cs");
        Assert.Contains("IWorldBasesSession", source, StringComparison.Ordinal);
        Assert.Contains("IWorldVehiclesSession", source, StringComparison.Ordinal);
        Assert.Contains("IWorldPetsSession", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_sessions_are_honest_about_what_still_has_no_grounded_live_path()
    {
        // Container peek (opening a bench's contents inline from the BASES tab) still has no
        // live equivalent - unrelated to bench upgrades, which round 77 closed below.
        var liveBases = ModelsSource("LiveBasesSession.cs");
        Assert.Contains("SupportsContainerPeek => false", liveBases, StringComparison.Ordinal);
        // Bench upgrade REMOVAL still has no evidenced live function (installation does, since
        // round 77 - AddUpgrade) - the live session says so via NotSupportedException rather
        // than silently no-op.
        Assert.Contains("NotSupportedException", liveBases, StringComparison.Ordinal);

        // Round 77: wrecked/destroyed IS now grounded (PendingDestroy) - the flag flipped to true.
        var liveVehicles = ModelsSource("LiveVehiclesSession.cs");
        Assert.Contains("SupportsWreckedState => true", liveVehicles, StringComparison.Ordinal);

        // Round 77: pets ARE now partially available (Pest/Skink family, matched by Guid) -
        // species change and removal still have no grounded live path.
        var livePets = ModelsSource("LivePetsSession.cs");
        Assert.Contains("SupportsSpeciesChange => false", livePets, StringComparison.Ordinal);
        Assert.Contains("SupportsRemoval => false", livePets, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", livePets, StringComparison.Ordinal);
    }

    [Fact]
    public void Pets_tab_shows_the_unavailable_reason_instead_of_an_empty_list()
    {
        var tab = WorldSource("WorldPetsTab.razor");
        Assert.Contains("Session.IsAvailable", tab, StringComparison.Ordinal);
        Assert.Contains("Session.UnavailableReason", tab, StringComparison.Ordinal);
    }

    private static string WorldSource(string file) => UiSource.ReadAllText("Components", "World", file);
    private static string PagesSource(string file) => UiSource.ReadAllText("Components", "Pages", file);
    private static string ModelsSource(string file) => UiSource.ReadAllText("Models", file);
}
