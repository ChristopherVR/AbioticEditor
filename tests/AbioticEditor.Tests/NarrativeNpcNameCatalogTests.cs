using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 99 follow-up: the "anonymous slot" verdict in
/// <c>docs/reference/research/research-narrative-npcs.md</c> was wrong - every placed
/// <c>NarrativeNPC_*</c> actor carries its own <c>NarrativeNPC_ConversationRow</c>, which names
/// it via <c>DT_NPC_Conversations</c>. These tests ground <see cref="NarrativeNpcNameCatalog"/>'s
/// key shape against real fixture ids (not a guess), so the wiring is proven now even though the
/// bundled registry itself is only regenerated once the coordinator re-runs
/// <c>dump-registry --all-cultures</c> (guarded below).
/// </summary>
public class NarrativeNpcNameCatalogTests
{
    // ---------- KeyFor / KeyForActorPath: pure, no game install needed ----------

    [Fact]
    public void KeyForActorPath_parses_the_real_file_actor_path_shape()
    {
        // A literal id read straight out of the raw bytes of
        // tests/fixtures/SteamSaves/Legacy/Cascade/WorldSave_Facility_Pens.sav (grepped, not
        // guessed) - the exact NarrativeNPCMap key for the Ela entry the probe evidence names.
        var id = "/Game/Maps/Facility_Pens.Facility_Pens:PersistentLevel.NarrativeNPC_Ela_C_1";
        Assert.Equal("Facility_Pens:NarrativeNPC_Ela_C_1", NarrativeNpcNameCatalog.KeyForActorPath(id));
        Assert.Equal(NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Ela_C_1"),
            NarrativeNpcNameCatalog.KeyForActorPath(id));
    }

    [Fact]
    public void KeyForActorPath_also_accepts_the_live_GetFullName_form()
    {
        // DoorIdParser.Parse (reused unchanged here) already strips a leading "<ClassName> "
        // token - the shape a live narrative-NPC session's own id carries.
        var id = "NarrativeNPC_Ela_C /Game/Maps/Facility_Pens.Facility_Pens:PersistentLevel.NarrativeNPC_Ela_C_1";
        Assert.Equal("Facility_Pens:NarrativeNPC_Ela_C_1", NarrativeNpcNameCatalog.KeyForActorPath(id));
    }

    [Fact]
    public void KeyForActorPath_returns_null_for_an_id_with_no_recognizable_actor_path()
    {
        Assert.Null(NarrativeNpcNameCatalog.KeyForActorPath(null));
        Assert.Null(NarrativeNpcNameCatalog.KeyForActorPath(string.Empty));
    }

    // ---------- Resolve: pure, no game install needed ----------

    [Fact]
    public void Resolve_matches_a_built_dictionary_by_the_same_key_shape()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Ela_C_1")] = "Ela",
            [NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Human_Hologram_C_0")] = "Dr. Manse",
        };
        Assert.Equal("Ela", NarrativeNpcNameCatalog.Resolve(names,
            "/Game/Maps/Facility_Pens.Facility_Pens:PersistentLevel.NarrativeNPC_Ela_C_1"));
        Assert.Equal("Dr. Manse", NarrativeNpcNameCatalog.Resolve(names,
            "/Game/Maps/Facility_Pens.Facility_Pens:PersistentLevel.NarrativeNPC_Human_Hologram_C_0"));
    }

    [Fact]
    public void Resolve_returns_null_for_an_unmatched_actor_or_missing_dictionary()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Ela_C_1")] = "Ela",
        };
        Assert.Null(NarrativeNpcNameCatalog.Resolve(names,
            "/Game/Maps/Facility_Office1.Facility_Office1:PersistentLevel.NarrativeNPC_Human_ParentBP_C_0"));
        Assert.Null(NarrativeNpcNameCatalog.Resolve(new Dictionary<string, string>(), "anything"));
        Assert.Null(NarrativeNpcNameCatalog.Resolve(null, "anything"));
    }

    // ---------- GameDataRegistry: bundled payload round-trips ----------

    [Fact]
    public void GameDataRegistry_round_trips_NarrativeNpcNames()
    {
        var registry = new GameDataRegistry
        {
            NarrativeNpcNames = new Dictionary<string, string>
            {
                [NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Ela_C_1")] = "Ela",
            },
        };
        var dir = Directory.CreateTempSubdirectory("narrative-npc-names-registry");
        try
        {
            var path = Path.Combine(dir.FullName, "registry.json");
            registry.Save(path);
            var loaded = GameDataRegistry.TryLoad(path);
            Assert.NotNull(loaded);
            Assert.Equal("Ela", loaded!.NarrativeNpcNames?[NarrativeNpcNameCatalog.KeyFor("Facility_Pens", "NarrativeNPC_Ela_C_1")]);
        }
        finally { dir.Delete(recursive: true); }
    }

    // ---------- Fixture: the real save's own ids parse to the shape BuildFrom would key by ----------

    /// <summary>
    /// Reads the real Cascade fixture's Facility_Pens region save (the exact region/actors the
    /// probe evidence in the round-99 follow-up names), proving the id shape a live
    /// <see cref="WorldNpc"/> actually carries - not a hand-typed guess - normalizes to the same
    /// key <see cref="NarrativeNpcNameCatalog.BuildFrom"/> would produce for that same actor. Also
    /// resolves against a small inline sample dictionary (the shape proof the coordinator asked
    /// for), then separately checks the real bundled registry when it already carries this field
    /// (skipped until the coordinator re-runs <c>dump-registry --all-cultures</c> - this field is
    /// new this round and the shipped <c>assets/registry/*.json</c> files were not regenerated).
    /// </summary>
    [Fact]
    public void Fixture_NarrativeNpcMap_ids_normalize_to_the_expected_key_shape()
    {
        var dir = Fixtures.CascadeDir;
        if (dir is null) return; // fixtures absent: skip

        var path = Path.Combine(dir, "WorldSave_Facility_Pens.sav");
        if (!File.Exists(path)) return; // skip

        var save = AbioticEditor.Core.WorldSaves.WorldSaveReader.ReadFromFile(path);
        var ela = save.Npcs.FirstOrDefault(n => n.Id.Contains("NarrativeNPC_Ela_C_1", StringComparison.Ordinal));
        Assert.NotNull(ela);
        Assert.Equal("Facility_Pens:NarrativeNPC_Ela_C_1", NarrativeNpcNameCatalog.KeyForActorPath(ela!.Id));

        var abe = save.Npcs.FirstOrDefault(n => n.Id.Contains("NarrativeNPC_Human_ParentBP_C_2", StringComparison.Ordinal));
        Assert.NotNull(abe);
        Assert.Equal("Facility_Pens:NarrativeNPC_Human_ParentBP_C_2", NarrativeNpcNameCatalog.KeyForActorPath(abe!.Id));

        // Shape proof: an inline sample dictionary keyed exactly the way BuildFrom would key it
        // (level base file name + actor instance name) resolves both real fixture ids right now,
        // with no game install and no bundled registry needed.
        var sample = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Facility_Pens:NarrativeNPC_Ela_C_1"] = "Ela",
            ["Facility_Pens:NarrativeNPC_Human_ParentBP_C_2"] = "Abe",
        };
        Assert.Equal("Ela", NarrativeNpcNameCatalog.Resolve(sample, ela.Id));
        Assert.Equal("Abe", NarrativeNpcNameCatalog.Resolve(sample, abe.Id));

        // The real bundled registry: only asserts once the coordinator has re-dumped it with this
        // new field (NarrativeNpcNames is null on the registry this round shipped with).
        var bundled = GameDataRegistry.LoadBundled();
        if (bundled?.NarrativeNpcNames is not { Count: > 0 } names) return; // not re-dumped yet: skip
        Assert.Equal("Ela", NarrativeNpcNameCatalog.Resolve(names, ela.Id));
    }
}
