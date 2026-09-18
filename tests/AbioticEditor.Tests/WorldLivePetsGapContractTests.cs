namespace AbioticEditor.Tests;

/// <summary>
/// Round 105: closing the Peccary/Lamogi live-pets gap "as far as the game allows" instead of
/// re-shipping round 77/79's blanket omission. Pins the shape of that closure in source: a
/// <c>Matched</c> flag threading from the Lua wire through <c>LivePetsChannel</c>/
/// <c>LivePetsSession</c> into the shared <c>WorldPetsTab</c> (so an unmatched row still shows,
/// with only the fields the game genuinely exposes for it), the generic (not hardcoded)
/// tamed-marker sweep in <c>pets.lua</c>, and the still-refused live species change with its new,
/// more specific evidence (a real <c>GameMode.SpawnPet</c> function, blocked on constructing an
/// <c>FTransform</c> - not a vaguer "no despawn/respawn precedent"). See
/// <c>docs/reference/live-editing-protocol.md</c>'s <c>pets.list</c> section and
/// <c>docs/PROGRESS.md</c>'s Round-105 entry for the full research writeup. Mirrors the structural
/// (source-text) assertion style <see cref="WorldLiveAreaParityContractTests"/> already uses for
/// other live-editing slices.
/// </summary>
public sealed class WorldLivePetsGapContractTests
{
    [Fact]
    public void WorldPet_record_carries_a_Matched_flag_defaulting_true_for_every_existing_caller()
    {
        var source = CoreSource("Domain", "World", "WorldPet.cs");
        Assert.Contains("bool Matched = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePetsChannel_maps_the_matched_flag_from_the_wire_in_both_directions()
    {
        var source = CoreSource("LiveEditing", "World", "LivePetsChannel.cs");
        // PetWire, LivePet and the GetAsync projection all carry the flag through end to end.
        Assert.Equal(2, CountOccurrences(source, "bool Matched = true"));
        Assert.Contains("p.Matched", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePetsChannel_documents_why_the_real_SpawnPet_function_stays_refused()
    {
        // Round 105 found the game's own species-change function (unlike round 76/77's vaguer "no
        // despawn/respawn precedent") but it takes an FTransform - a struct this project has no
        // safe construction precedent for, the exact shape that crashed the BASES tab in round 79.
        var source = CoreSource("LiveEditing", "World", "LivePetsChannel.cs");
        Assert.Contains("SpawnPet", source, StringComparison.Ordinal);
        Assert.Contains("FTransform", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LivePetsSession_passes_the_matched_flag_through_to_WorldPet()
    {
        var source = ModelSource("LivePetsSession.cs");
        Assert.Contains("Matched: p.Matched", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldPetsTab_gates_name_and_level_fields_by_Matched_but_keeps_health_available()
    {
        var source = WorldSource("WorldPetsTab.razor");
        Assert.Contains("identityEditable = editable && pet.Matched", source, StringComparison.Ordinal);
        Assert.Contains("WorldPets_UnmatchedLiveNotice", source, StringComparison.Ordinal);
        // Health/alive-state stays gated by `editable` alone (universal AbioticCharacter fields),
        // not folded into the identity-only gate.
        Assert.Contains("@if (editable)", source, StringComparison.Ordinal);
        Assert.Contains("@if (identityEditable)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void pets_lua_sweeps_the_generic_tamed_marker_instead_of_a_hardcoded_leaf_class_list()
    {
        var source = File.ReadAllText(LiveAgentPath("Scripts", "areas", "pets.lua"));
        // Owner rule: discover via the parent class (hierarchy-inclusive), not a leaf-class list.
        Assert.Contains("local ALL_NPC_CLASS = \"NPC_Base_ParentBP_C\"", source, StringComparison.Ordinal);
        Assert.Contains("Default__AbioticFunctionLibrary", source, StringComparison.Ordinal);
        Assert.Contains("lib:IsTamedPet(npc)", source, StringComparison.Ordinal);
        Assert.Contains("matched = false", source, StringComparison.Ordinal);
        Assert.Contains("matched = true", source, StringComparison.Ordinal);
        // Unmatched rows never get a name/xp write attempted - it is reported as a warning instead.
        Assert.Contains("no name field to write live", source, StringComparison.Ordinal);
        Assert.Contains("no XP/level field to write", source, StringComparison.Ordinal);
    }

    [Fact]
    public void pets_lua_still_refuses_species_change_and_explains_why_with_the_new_evidence()
    {
        var source = File.ReadAllText(LiveAgentPath("Scripts", "areas", "pets.lua"));
        Assert.Contains("supportsSpeciesChange = false", source, StringComparison.Ordinal);
        Assert.Contains("SpawnPet(Class, SpawnTransform, Guid, Name,", source, StringComparison.Ordinal);
        Assert.Contains("FTransform", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Round_105_resource_key_exists_in_every_shipped_locale()
    {
        foreach (var locale in new[] { "", ".de", ".es", ".fr", ".ru" })
        {
            var path = Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Web.Shared",
                "Localization", $"AppResources{locale}.resx");
            var resources = System.Xml.Linq.XDocument.Load(path)
                .Descendants("data").Select(node => node.Attribute("name")?.Value)
                .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("WorldPets_UnmatchedLiveNotice", resources);
        }
    }

    [Fact]
    public void Live_editing_protocol_doc_describes_the_round_105_pets_gap_closure()
    {
        var doc = File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "docs", "reference", "live-editing-protocol.md"));
        Assert.Contains("IsTamedPet", doc, StringComparison.Ordinal);
        Assert.Contains("matched", doc, StringComparison.Ordinal);
    }

    private static string CoreSource(params string[] parts)
        => File.ReadAllText(Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Core", Path.Combine(parts)));

    private static string WorldSource(string file) => UiSource.ReadAllText("Components", "World", file);
    private static string ModelSource(string file) => UiSource.ReadAllText("Models", file);

    private static string LiveAgentPath(params string[] parts)
        => Path.Combine([UiSource.RepositoryRoot, "live-agent", "AbioticEditorLiveAgentLua", .. parts]);

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
