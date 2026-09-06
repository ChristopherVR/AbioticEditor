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
    public void Live_sessions_are_honest_about_what_has_no_grounded_live_path()
    {
        // Bench upgrades: no confirmed live write path (see areas/bases.lua) - the live session
        // must say so via the interface's own capability flags, not silently no-op.
        var liveBases = ModelsSource("LiveBasesSession.cs");
        Assert.Contains("SupportsContainerPeek => false", liveBases, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", liveBases, StringComparison.Ordinal);

        // Wrecked/destroyed: no evidenced live property (see areas/vehicles.lua).
        var liveVehicles = ModelsSource("LiveVehiclesSession.cs");
        Assert.Contains("SupportsWreckedState => false", liveVehicles, StringComparison.Ordinal);

        // Pets: no general live path at all (see areas/pets.lua) - reported, not guessed.
        var livePets = ModelsSource("LivePetsSession.cs");
        Assert.Contains("IsAvailable => false", livePets, StringComparison.Ordinal);
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
