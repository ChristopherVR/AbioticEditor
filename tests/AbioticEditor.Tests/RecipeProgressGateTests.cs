using AbioticEditor.Core.Codex;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The recipe progress-gate rule ported from the retired native app: a recipe granted by a
/// known email attachment refuses to unlock while the email's region is unreached, and
/// everything else (unknown world, non-email recipes, unmapped regions) stays allowed.
/// </summary>
public class RecipeProgressGateTests
{
    private static EmailEntry Email(string id, params string[] attachmentRecipes)
        => new(id, $"Subject of {id}", [], attachmentRecipes, []);

    private static string LabsTrigger()
    {
        var chapter = FlagGate.RegionChapterForRowId("Email_Labs_TestMail");
        Assert.NotNull(chapter);
        Assert.NotNull(chapter!.TriggerFlag);
        return chapter.TriggerFlag!;
    }

    [Fact]
    public void UnknownWorld_AllowsEverything()
    {
        var emails = new[] { Email("Email_Labs_TestMail", "GatedRecipe") };
        Assert.Null(RecipeProgressGate.TryFindBlock("GatedRecipe", null, emails));
    }

    [Fact]
    public void RecipeWithoutGrantingEmail_IsAllowed()
    {
        var emails = new[] { Email("Email_Labs_TestMail", "GatedRecipe") };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Null(RecipeProgressGate.TryFindBlock("OrdinaryRecipe", flags, emails));
    }

    [Fact]
    public void EmailInUnreachedRegion_BlocksTheUnlock()
    {
        var emails = new[] { Email("Email_Labs_TestMail", "GatedRecipe") };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Labs never reached

        var block = RecipeProgressGate.TryFindBlock("GatedRecipe", flags, emails);

        Assert.NotNull(block);
        Assert.Equal("GatedRecipe", block!.RecipeId);
        Assert.Equal("Subject of Email_Labs_TestMail", block.EmailSubject);
        Assert.Equal(LabsTrigger(), block.TriggerFlag);
    }

    [Fact]
    public void EmailInReachedRegion_IsAllowed()
    {
        var emails = new[] { Email("Email_Labs_TestMail", "GatedRecipe") };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LabsTrigger() };
        Assert.Null(RecipeProgressGate.TryFindBlock("GatedRecipe", flags, emails));
    }

    [Fact]
    public void RecipeIdMatch_IgnoresCase_LikeNative()
    {
        var emails = new[] { Email("Email_Labs_TestMail", "GatedRecipe") };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(RecipeProgressGate.TryFindBlock("gatedrecipe", flags, emails));
    }

    [Fact]
    public void EmailWithoutMappedRegion_IsAllowed()
    {
        // Most emails embed no recognised area; they carry no fixed story gate.
        var emails = new[] { Email("Email_Random_IsWrestlingReal", "SideRecipe") };
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Null(RecipeProgressGate.TryFindBlock("SideRecipe", flags, emails));
    }

    [Fact]
    public void Later_available_email_route_overrides_an_earlier_unreached_route()
    {
        EmailEntry[] emails = [Email("Email_Reactors_Gift", "Recipe"), Email("Email_Labs_Gift", "Recipe")];
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LabsTrigger() };

        Assert.Null(RecipeProgressGate.TryFindBlock("Recipe", flags, emails));
        Assert.Null(RecipeProgressGate.TryFindBlock("Recipe", flags, emails.Reverse()));
    }

    [Fact]
    public void Unknown_email_alternative_prevents_a_false_universal_block()
    {
        EmailEntry[] emails = [Email("Email_Labs_Gift", "Recipe"), Email("Email_NewSector_Gift", "Recipe")];
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Null(RecipeProgressGate.TryFindBlock("Recipe", flags, emails));
    }

    [Fact]
    public void Every_known_email_region_unreached_preserves_the_existing_block()
    {
        EmailEntry[] emails = [Email("Email_Labs_Gift", "Recipe"), Email("Email_Reactors_Gift", "Recipe")];
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var block = RecipeProgressGate.TryFindBlock("Recipe", flags, emails);

        Assert.NotNull(block);
        Assert.Equal(LabsTrigger(), block.TriggerFlag);
        Assert.Equal("Recipe", block.RecipeId);
    }

    [Fact]
    public void Recorded_granting_email_after_story_rewind_does_not_reblock_the_recipe()
    {
        EmailEntry[] emails = [Email("Email_Labs_Gift", "Recipe")];
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(RecipeProgressGate.TryFindBlock("Recipe", flags, emails, emailsRead: []));
        Assert.Null(RecipeProgressGate.TryFindBlock("Recipe", flags, emails, emailsRead: ["EMAIL_LABS_GIFT"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fish_alternative_is_guidance_instead_of_a_universal_email_block(bool recordedCatch)
    {
        EmailEntry[] emails = [Email("Email_Labs_Gift", "Recipe")];
        FishDefinition[] fish = [new("Catch", "item_fish", false, UnlockRecipeId: "RECIPE",
            RequiredWorldFlag: "FishFlag", RequiredDlcId: "Dlc", RequiredBaitTag: "Fishing.Bait.Special")];
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] catches = recordedCatch ? ["CATCH"] : [];

        Assert.Null(RecipeProgressGate.TryFindBlock("Recipe", flags, emails, fish, [], catches));
    }
}
