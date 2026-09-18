namespace AbioticEditor.Tests;

/// <summary>
/// Round 109: live species change for MATCHED (Pest/Skink-family) pets, closing the gap round
/// 76/77/105 all re-confirmed but left refused (the game's own <c>GameMode.SpawnPet</c> needs an
/// <c>FTransform</c>, and this project had no construction precedent for one). Pins the shape of
/// that closure in source: <c>pets.lua</c> reads the OLD pet actor's own transform fresh via
/// <c>K2_GetActorTransform</c> and passes it through UNCHANGED (never a fabricated table - the
/// exact mistake that crashed the BASES tab in round 79), verifies the spawned actor's identity
/// before destroying the original, and the new capability threads end to end
/// (<c>supportsSpeciesChange</c> on the wire -&gt; <c>LivePetDirectory</c> -&gt;
/// <c>LivePetsSession</c>) so an older live-agent build that never reports it still hides the
/// shared tab's control. See <c>docs/reference/live-editing-protocol.md</c>'s <c>pets.set</c>
/// section and <c>docs/PROGRESS.md</c>'s Round-109 entry for the full research writeup, including
/// the one assumption (struct-userdata pass-through for a nested/composite struct) that is still
/// unverified against the real game. Mirrors the structural (source-text) assertion style
/// <see cref="WorldLivePetsGapContractTests"/> and <see cref="WorldLiveAreaParityContractTests"/>
/// already use for other live-editing slices - deliberately a new file, not an addition to either.
/// </summary>
public sealed class WorldLivePetsSpeciesChangeContractTests
{
    [Fact]
    public void pets_lua_reports_the_new_capability_and_never_a_hardcoded_leaf_class_list()
    {
        var source = PetsLuaSource();
        Assert.Contains("supportsSpeciesChange = true", source, StringComparison.Ordinal);
        // No hardcoded leaf class list: the target species class is resolved from whatever path
        // pets.set's own payload carries, not a table of known species names.
        Assert.Contains("local function resolveClass(path)", source, StringComparison.Ordinal);
        Assert.Contains("StaticFindObject(full)", source, StringComparison.Ordinal);
        Assert.Contains("LoadAsset(full)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void pets_lua_reads_the_old_actor_transform_fresh_and_never_builds_one()
    {
        var source = PetsLuaSource();
        Assert.Contains("npc:K2_GetActorTransform()", source, StringComparison.Ordinal);
        // Every value handed to SpawnPet is read straight off the old actor - not constructed.
        Assert.Contains("gm:SpawnPet(targetClass, transform, guid, name, owner, dynamic, true)", source,
            StringComparison.Ordinal);
        // The one honest caveat this project applies nowhere else as loudly: pcall cannot be
        // trusted to catch a wrong-shaped native-call argument (round 79's BASES crash).
        Assert.Contains("pcall` cannot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void pets_lua_verifies_identity_before_destroying_the_original_pet()
    {
        var source = PetsLuaSource();
        var spawnIndex = source.IndexOf("local okSpawn, newPet = pcall", StringComparison.Ordinal);
        var guidCheckIndex = source.IndexOf("newGuid ~= guid", StringComparison.Ordinal);
        var destroyIndex = source.IndexOf("local okDestroy = pcall", StringComparison.Ordinal);
        Assert.True(spawnIndex >= 0 && guidCheckIndex >= 0 && destroyIndex >= 0,
            "trySpeciesChange should contain a spawn call, a guid comparison, and a destroy call");
        Assert.True(spawnIndex < guidCheckIndex, "the spawn happens before the identity check");
        Assert.True(guidCheckIndex < destroyIndex, "identity is verified before the old actor is destroyed");
    }

    [Fact]
    public void pets_lua_refuses_species_change_for_unmatched_pets_with_a_named_warning()
    {
        var source = PetsLuaSource();
        Assert.Contains("this pet can't be matched to a save record, so", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePetsChannel_carries_npcClass_on_the_set_wire_and_documents_the_mechanism()
    {
        var source = CoreSource("LiveEditing", "World", "LivePetsChannel.cs");
        Assert.Contains("string? NpcClass = null", source, StringComparison.Ordinal);
        Assert.Contains("K2_GetActorTransform", source, StringComparison.Ordinal);
        Assert.Contains("cannot be trusted to catch", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePetsSession_relays_the_agents_reported_capability_instead_of_hardcoding_it()
    {
        var source = ModelSource("LivePetsSession.cs");
        Assert.DoesNotContain("SupportsSpeciesChange => false", source, StringComparison.Ordinal);
        Assert.Contains("SupportsSpeciesChange => _supportsSpeciesChange", source, StringComparison.Ordinal);
        Assert.Contains("_supportsSpeciesChange = directory.SupportsSpeciesChange", source, StringComparison.Ordinal);
        Assert.Contains("_channel.SetAsync(id, isDead, customName, xp, limbHealth, npcClass, cancellationToken)",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_round_109_species_change_closure()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("K2_GetActorTransform", doc, StringComparison.Ordinal);
        Assert.Contains("Round 109", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_log_has_a_round_109_entry_naming_the_unverified_assumption()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "PROGRESS.md"));
        Assert.Contains("Round-109", doc, StringComparison.Ordinal);
        Assert.Contains("K2_GetActorTransform", doc, StringComparison.Ordinal);
    }

    private static string PetsLuaSource() => File.ReadAllText(LiveAgentPath("Scripts", "areas", "pets.lua"));

    private static string CoreSource(params string[] parts)
        => File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Core", Path.Combine(parts)));

    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);
}
