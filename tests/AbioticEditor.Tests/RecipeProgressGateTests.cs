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
}
