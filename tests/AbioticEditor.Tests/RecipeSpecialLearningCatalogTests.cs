using AbioticEditor.Core.Items;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Tests;

public sealed class RecipeSpecialLearningCatalogTests
{
    [Fact]
    public void Conversation_uses_explicit_grant_fields_and_preserves_line_identity()
    {
        var initialLine = Row(("RecipesToUnlock", Recipes("recipe_a")), ("WorldFlagsToTrigger", Recipes("not_a_recipe")));
        var row = Row(("NPCName", "Scientist"),
            ("InitalContactMessages", Array(Row(("DialogToSpeak", "unrelated-dialogue")), initialLine)),
            ("ReturnMessages", Array(Row(("RecipesToUnlock", Recipes("recipe_b"))))));
        var grants = RecipeSpecialLearningCatalog.ConversationGrants("conversation-row", row);

        Assert.Equal(2, grants.Count);
        var initial = Assert.Single(grants, grant => grant.RecipeId == "recipe_a");
        Assert.Equal("Scientist", initial.SourceLabel);
        Assert.Equal("conversation-row", initial.SourceId);
        Assert.Equal("InitalContactMessages", initial.SectionKind);
        Assert.Equal(1, initial.SectionIndex);
        Assert.Contains("DT_NPC_Conversations", initial.SourceAssetPath, StringComparison.Ordinal);
        Assert.DoesNotContain(grants, grant => grant.RecipeId == "not_a_recipe");
    }

    [Fact]
    public void Idle_lines_are_not_presented_as_grants_from_the_verified_parent_state_switch()
    {
        var idle = Row(("RecipesToUnlock", Recipes("idle_not_supported")));
        var beckon = Row(("RecipesToUnlock", Recipes("beckon_supported")));
        var row = Row(("IdleLines", Array(idle)), ("BeckoningLines", Array(beckon)));
        Assert.Equal("beckon_supported", Assert.Single(RecipeSpecialLearningCatalog.ConversationGrants("row", row)).RecipeId);
    }

    [Fact]
    public void Malformed_lines_do_not_shift_the_source_index_of_a_following_valid_line()
    {
        var row = Row(("ReturnMessages", Array("not-a-structure", Row(("RecipesToUnlock", Recipes("recipe"))))));
        Assert.Equal(1, Assert.Single(RecipeSpecialLearningCatalog.ConversationGrants("row", row)).SectionIndex);
    }

    [Fact]
    public void Compendium_kill_grant_keeps_exact_threshold_and_has_to_be_enabled()
    {
        var section = Row(("RequiredCount", 1000), ("RecipesToUnlock", Recipes("taxidermy")));
        var row = Row(("Title", "Designation"), ("Subtitle", "Pest"),
            ("bHasKillRequirementSection", true), ("KillRequirementSection", section));
        var grant = Assert.Single(RecipeSpecialLearningCatalog.CompendiumGrants("Pest", row));
        Assert.Equal("Pest", grant.SourceLabel);
        Assert.Equal("Kill", grant.SectionKind);
        Assert.Equal(1000, grant.RequiredCount);
        Assert.Equal(RecipeSpecialGrantKind.Compendium, grant.Kind);
        Assert.Empty(RecipeSpecialLearningCatalog.CompendiumGrants("Pest",
            Row(("bHasKillRequirementSection", false), ("KillRequirementSection", section))));
    }

    [Fact]
    public void Compendium_section_preserves_its_unlock_type_without_inventing_a_world_condition()
    {
        var row = Row(("Title", "Snowman"), ("Sections", Array(
            Row(("SectionText", "No grant")),
            Row(("UnlockRequirement", "ECompendiumUnlockType::Exploration"), ("RecipesToUnlock", Recipes("snowman_recipe"))))));
        var grant = Assert.Single(RecipeSpecialLearningCatalog.CompendiumGrants("Snowman", row));
        Assert.Equal("Exploration", grant.SectionKind);
        Assert.Equal(1, grant.SectionIndex);
        Assert.Null(grant.RequiredCount);
        Assert.Null(grant.TraitId);
    }

    [Fact]
    public void Empty_and_unknown_recipe_handles_are_ignored_and_ids_are_deduplicated_without_case_sensitivity()
    {
        var ids = RecipeSpecialLearningCatalog.RowIds(Recipes("None", "", "recipe_a", "RECIPE_A", "recipe_b"));
        Assert.Equal(2, ids.Count);
        Assert.Contains("recipe_a", ids);
        Assert.Contains("recipe_b", ids);
    }

    [Fact]
    public void Inspection_grants_preserve_actual_object_label_and_placed_level_provenance()
    {
        var grant = Assert.Single(RecipeSpecialLearningCatalog.InspectableGrants(
            "AbioticFactor/Content/Maps/Facility_Labs.umap", "PersistentLevel.InteractableProp_BP_C_4", "Interesting Diagram", Recipes("recipe_a")));
        Assert.Equal("Interesting Diagram", grant.SourceLabel);
        Assert.Equal("Facility_Labs", grant.LevelId);
        Assert.Equal("PersistentLevel.InteractableProp_BP_C_4", grant.SourceObjectPath);
        Assert.Contains("InteractableProp_BP", grant.TriggerFunction, StringComparison.Ordinal);
        Assert.Equal(RecipeSpecialGrantKind.Inspectable, grant.Kind);
    }

    [Fact]
    public void Inspection_with_no_resolved_label_does_not_invent_an_output_item_name()
    {
        var grant = Assert.Single(RecipeSpecialLearningCatalog.InspectableGrants("map.umap", "actor", string.Empty, Recipes("recipe_output")));
        Assert.Empty(grant.SourceLabel);
        Assert.Empty(RecipeSpecialLearningCatalog.InspectableGrants("map.umap", "actor", "Object", Recipes("None")));
    }

    [Fact]
    public void Bench_access_uses_its_explicit_name_array_without_inventing_ingredient_requirements()
    {
        var grants = RecipeSpecialLearningCatalog.BenchGrants("bench-asset", "Deployable_Bench_Ammo",
            Array("recipe_ammo", "RECIPE_AMMO", "None", 42));
        var grant = Assert.Single(grants);
        Assert.Equal("recipe_ammo", grant.RecipeId);
        Assert.Equal(RecipeSpecialGrantKind.BenchAccess, grant.Kind);
        Assert.Equal("Deployable_Bench_Ammo", grant.SourceLabel);
        Assert.Null(grant.RequiredCount);
    }

    private static FStructFallback Row(params (string Name, object? Value)[] values)
        => new(values.Select(value => new FPropertyTag { Name = new FName(value.Name), Tag = new ValueProperty(value.Value) }).ToList());
    private static UScriptArray Array(params object?[] values)
        => new(values.Select(value => (FPropertyTagType)new ValueProperty(value)).ToList(), "StructProperty");
    private static UScriptArray Recipes(params string[] ids)
        => Array(ids.Select(id => (object)Row(("RowName", id))).ToArray());
    private sealed class ValueProperty(object? value) : FPropertyTagType
    {
        public override object? GenericValue => value;
        public override string ToString() => value?.ToString() ?? string.Empty;
    }
}
