using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 124: the NPCS tab's Dead checkbox used to be enabled for every narrative NPC even
/// though the hint text (<see cref="NpcIdentityCatalog.LabelFor"/>) already told players some
/// classes ("Story hologram (scripted scene, cannot die)", "Static trader stand") can never
/// actually die. <see cref="NpcIdentityCatalog.CanBeKilled"/> is the data backing the fix: it
/// gates the checkbox without touching the underlying save/live data, so a row that already
/// carries <c>IsDead</c> from the game's own story scripting still displays correctly - see
/// docs/reference/research/research-narrative-npcs.md.
/// </summary>
public sealed class NpcIdentityCatalogTests
{
    [Fact]
    public void Holograms_cannot_be_killed()
    {
        Assert.False(NpcIdentityCatalog.CanBeKilled(
            "PersistentLevel.NarrativeNPC_Human_Hologram_C_0", "NarrativeNPC_Human_Hologram_C_0"));
    }

    [Fact]
    public void Static_trader_stands_cannot_be_killed()
    {
        Assert.False(NpcIdentityCatalog.CanBeKilled(
            "PersistentLevel.NarrativeNPC_Human_TRADER_C_1", "NarrativeNPC_Human_TRADER_C_1"));
    }

    [Fact]
    public void Killable_story_npcs_can_be_killed()
    {
        Assert.True(NpcIdentityCatalog.CanBeKilled(
            "PersistentLevel.NarrativeNPC_Human_Killable_C_0", "NarrativeNPC_Human_Killable_C_0"));
    }

    [Theory]
    [InlineData("NarrativeNPC_Ela_C_1")]
    [InlineData("NarrativeNPC_HastaTria_C_1")]
    [InlineData("NarrativeNPC_Larva_C_1")]
    [InlineData("NarrativeNPC_MGT_CKCore_C_1")]
    [InlineData("NarrativeNPC_Human_ParentBP_C_1")]
    public void Named_and_generic_story_characters_can_be_killed(string actorName)
    {
        Assert.True(NpcIdentityCatalog.CanBeKilled($"PersistentLevel.{actorName}", actorName));
    }

    [Fact]
    public void Unknown_classes_default_to_killable_editable()
    {
        // The table only turns the control off on positive evidence - an unrecognised class
        // (e.g. a future actor class this build's catalog hasn't been updated for) must default
        // to editable, not silently lock the control for something we simply don't know about.
        Assert.True(NpcIdentityCatalog.CanBeKilled(
            "PersistentLevel.NarrativeNPC_SomeFutureClass_C_0", "NarrativeNPC_SomeFutureClass_C_0"));
    }
}
