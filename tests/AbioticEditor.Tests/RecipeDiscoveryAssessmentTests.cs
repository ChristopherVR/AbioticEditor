using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class RecipeDiscoveryAssessmentTests
{
    [Fact]
    public void No_known_grants_or_catalogs_remain_unknown()
    {
        var result = RecipeDiscoveryAssessment.Assess("recipe", null, null, []);

        Assert.False(result.HasKnownSources);
        Assert.Empty(result.Routes);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.RecordedSourceState);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.SourceAccessState);
    }

    [Fact]
    public void Mapped_email_uses_the_established_region_flag_without_claiming_it_was_read()
    {
        var email = Email("Email_Labs_Test", "recipe");
        var result = RecipeDiscoveryAssessment.Assess("RECIPE", [email], [], [], []);
        var route = Assert.Single(result.Routes);

        Assert.Equal(RecipeDiscoverySourceKind.Email, route.Kind);
        Assert.Equal(email.Subject, route.EmailSubject);
        Assert.Equal(FlagGate.RegionChapterForRowId(email.Id)!.TriggerFlag, route.RequiredWorldFlag);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, route.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, route.RecordedSourceState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, result.SourceAccessState);

        var reached = RecipeDiscoveryAssessment.Assess("recipe", [email], [], [route.RequiredWorldFlag!.ToLowerInvariant()]);
        Assert.Equal(RecipeDiscoveryState.KnownMet, reached.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.Unknown, reached.RecordedSourceState);
    }

    [Theory]
    [InlineData("Email_FutureSector_Grant")]
    [InlineData("Email_Office_Grant")]
    public void Unmapped_email_regions_stay_unknown_even_with_known_world(string sourceId)
    {
        var result = RecipeDiscoveryAssessment.Assess("recipe", [Email(sourceId, "recipe")], [], []);

        Assert.Equal(RecipeDiscoveryState.Unknown, result.WorldAccessState);
        Assert.Null(Assert.Single(result.Routes).RequiredWorldFlag);
    }

    [Fact]
    public void Missing_world_context_is_unknown_for_email_and_fish()
    {
        var result = RecipeDiscoveryAssessment.Assess("recipe",
            [Email("Email_Labs_Grant", "recipe")], [Fish("Fish", "recipe")], null);

        Assert.Equal(RecipeDiscoveryState.Unknown, result.WorldAccessState);
        Assert.All(result.Routes, route => Assert.Equal(RecipeDiscoveryState.Unknown, route.WorldAccessState));
    }

    [Fact]
    public void Multiple_email_routes_are_alternatives_regardless_of_order()
    {
        var labs = Email("Email_Labs_Grant", "recipe");
        var reactors = Email("Email_Reactors_Grant", "recipe");
        var labsFlag = FlagGate.RegionChapterForRowId(labs.Id)!.TriggerFlag!;
        var result = RecipeDiscoveryAssessment.Assess("recipe", [reactors, labs], [], [labsFlag]);

        Assert.Equal(2, result.Routes.Count);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, result.Routes[0].WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.Routes[1].WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.WorldAccessState);
    }

    [Fact]
    public void An_unknown_alternative_prevents_claiming_every_route_is_unmet()
    {
        var result = RecipeDiscoveryAssessment.Assess("recipe",
            [Email("Email_Labs_Grant", "recipe"), Email("Email_NewRegion_Grant", "recipe")], [], []);

        Assert.Contains(result.Routes, route => route.WorldAccessState == RecipeDiscoveryState.KnownUnmet);
        Assert.Contains(result.Routes, route => route.WorldAccessState == RecipeDiscoveryState.Unknown);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.SourceAccessState);
    }

    [Fact]
    public void Fish_uses_its_direct_world_flag_without_inventing_a_region_mapping()
    {
        var fish = Fish("RareCatch", "recipe", flag: "Specific_FishFlag");
        var blocked = RecipeDiscoveryAssessment.Assess("recipe", [], [fish], []);
        var route = Assert.Single(blocked.Routes);

        Assert.Equal(RecipeDiscoverySourceKind.Fish, route.Kind);
        Assert.Equal("item_RareCatch", route.FishItemId);
        Assert.Null(route.ChapterTitle);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, route.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.KnownMet,
            RecipeDiscoveryAssessment.Assess("recipe", [], [fish], ["specific_fishflag"]).WorldAccessState);
    }

    [Fact]
    public void Ungated_fish_meets_only_the_world_criterion_and_is_an_alternative_to_email()
    {
        var result = RecipeDiscoveryAssessment.Assess("recipe", [Email("Email_Labs_Grant", "recipe")],
            [Fish("Catch", "recipe")], []);

        Assert.Equal(RecipeDiscoveryState.KnownMet, result.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.RecordedSourceState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.SourceAccessState);
    }

    [Theory]
    [InlineData("dlc", null)]
    [InlineData(null, "Fishing.Bait.Special")]
    [InlineData("dlc", "Fishing.Bait.Special")]
    public void Unverified_DLC_or_bait_keeps_source_access_unknown(string? dlc, string? bait)
    {
        var fish = Fish("Catch", "recipe") with { RequiredDlcId = dlc, RequiredBaitTag = bait };
        var result = RecipeDiscoveryAssessment.Assess("recipe", [], [fish], []);
        var route = Assert.Single(result.Routes);

        Assert.True(route.HasUnverifiedConditions);
        Assert.Equal(dlc, route.RequiredDlcId);
        Assert.Equal(bait, route.RequiredBaitTag);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.SourceAccessState);
    }

    [Fact]
    public void Time_preferences_are_not_assumed_to_match_the_current_game_time()
    {
        var fish = Fish("NightCatch", "recipe") with { NoonMult = 0 };
        var result = RecipeDiscoveryAssessment.Assess("recipe", [], [fish], []);

        Assert.True(Assert.Single(result.Routes).HasTimePreference);
        Assert.Equal(RecipeDiscoveryState.Unknown, result.SourceAccessState);
    }

    [Fact]
    public void A_recorded_source_demonstrates_prior_access_without_overwriting_current_world_evidence()
    {
        var fish = Fish("Catch", "recipe", "MissingFlag") with { RequiredDlcId = "unknown_dlc" };
        var result = RecipeDiscoveryAssessment.Assess("recipe", [], [fish], [], fishCaught: ["CATCH"]);

        Assert.Equal(RecipeDiscoveryState.KnownUnmet, result.WorldAccessState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.RecordedSourceState);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.SourceAccessState);
    }

    [Fact]
    public void Null_and_empty_player_histories_are_distinct()
    {
        var email = Email("Email_Labs_Grant", "recipe");
        var unknown = RecipeDiscoveryAssessment.Assess("recipe", [email], [], []);
        var absent = RecipeDiscoveryAssessment.Assess("recipe", [email], [], [], []);

        Assert.Equal(RecipeDiscoveryState.Unknown, unknown.RecordedSourceState);
        Assert.Equal(RecipeDiscoveryState.KnownUnmet, absent.RecordedSourceState);
        var mixed = RecipeDiscoveryAssessment.Assess("recipe", [email], [Fish("Catch", "recipe")], [], [], null);
        Assert.Equal(RecipeDiscoveryState.Unknown, mixed.RecordedSourceState);
    }

    [Fact]
    public void One_recorded_route_suffices_across_email_and_fish_and_case_aliases_are_deduplicated()
    {
        var email = Email("Email_Labs_Grant", "Recipe");
        var result = RecipeDiscoveryAssessment.Assess("recipe", [email, email with { Id = "email_labs_grant" }],
            [Fish("Catch", "RECIPE")], [], emailsRead: ["EMAIL_LABS_GRANT"], fishCaught: []);

        Assert.Equal(2, result.Routes.Count);
        Assert.Equal(RecipeDiscoveryState.KnownMet, result.RecordedSourceState);
    }

    [Fact]
    public void Recorded_grants_find_missed_recipes_without_any_pickup_history_and_ignore_unknown_or_learned_ids()
    {
        var emails = new[] { Email("Email_Labs_Grant", "email_recipe", "not_in_catalog") };
        var fish = new[] { Fish("Catch", "fish_recipe"), Fish("OtherCatch", "unrecorded") };
        string[] ids = ["email_recipe", "EMAIL_RECIPE", "fish_recipe", "unrecorded", "ordinary"];
        var result = RecipeDiscoveryAssessment.FindRecordedMissing(ids, [], emails, fish,
            ["EMAIL_LABS_GRANT"], ["catch"]);

        Assert.Equal(["email_recipe", "fish_recipe"], result.Select(item => item.RecipeId));
        Assert.All(result, item => Assert.Equal(RecipeDiscoveryState.KnownMet, item.RecordedSourceState));
        Assert.Single(RecipeDiscoveryAssessment.FindRecordedMissing(ids, ["EMAIL_RECIPE"], emails, fish,
            ["Email_Labs_Grant"], ["Catch"]));
        Assert.Empty(RecipeDiscoveryAssessment.FindRecordedMissing(ids, [], emails, fish, null, null));
        Assert.Empty(RecipeDiscoveryAssessment.FindRecordedMissing(ids, [], emails, fish, [], []));
    }

    private static EmailEntry Email(string id, params string[] recipes) => new(id, $"Subject {id}", [], recipes, []);

    private static FishDefinition Fish(string id, string recipe, string? flag = null)
        => new(id, $"item_{id}", false, UnlockRecipeId: recipe, RequiredWorldFlag: flag);
}
