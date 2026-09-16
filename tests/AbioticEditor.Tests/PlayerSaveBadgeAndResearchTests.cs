using System.IO;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the "NEW" badge sync (<c>NewestRecipes_</c>/<c>Compendium_Unread_</c>/
/// <c>Journal_Unread_</c>/<c>Fish_Unread_</c>), the research queue
/// (<c>RecipesRequiringResearch_</c>) and the two small fields
/// (<c>CompletedIntro_</c>/<c>LastControlRotation_</c>) added to
/// <see cref="PlayerSaveWriter"/>. Uses the checked-in <c>SteamSaves/Legacy/Cascade</c>
/// fixture, which carries every one of these tags already (so the missing-tag paths need a
/// stripped/round-tripped save, same technique as <c>PlayerSurvivalStatDefaultTests</c>).
/// </summary>
public sealed class PlayerSaveBadgeAndResearchTests
{
    private static string FixturePlayerSave()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(path));
        return path;
    }

    private static PlayerSaveData Reload(PlayerSaveData data)
    {
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        return PlayerSaveReader.ReadFromStream(ms);
    }

    // ---------- badge sync: unlock adds to the badge array, relock removes it ----------

    [Fact]
    public void ApplyRecipes_Unlocking_AddsToNewestRecipes_AndRelocking_RemovesIt()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        Assert.DoesNotContain("recipe_badge_sentinel", data.NewestRecipes);

        var unlocked = data.Recipes.Append("recipe_badge_sentinel").ToList();
        PlayerSaveWriter.ApplyRecipes(data, unlocked);
        var afterUnlock = Reload(data);
        Assert.Contains("recipe_badge_sentinel", afterUnlock.Recipes);
        Assert.Contains("recipe_badge_sentinel", afterUnlock.NewestRecipes);

        PlayerSaveWriter.ApplyRecipes(afterUnlock, data.Recipes);
        var afterRelock = Reload(afterUnlock);
        Assert.DoesNotContain("recipe_badge_sentinel", afterRelock.Recipes);
        Assert.DoesNotContain("recipe_badge_sentinel", afterRelock.NewestRecipes);
        // Badge entries that were already there before the edit are left alone.
        Assert.Equal(data.NewestRecipes.OrderBy(x => x, StringComparer.Ordinal),
            afterRelock.NewestRecipes.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ApplyRecipes_SyncNewBadgeFalse_LeavesNewestRecipesUntouched()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        var originalBadge = data.NewestRecipes.ToList();

        var unlocked = data.Recipes.Append("recipe_badge_sentinel_2").ToList();
        PlayerSaveWriter.ApplyRecipes(data, unlocked, syncNewBadge: false);
        var reloaded = Reload(data);

        Assert.Contains("recipe_badge_sentinel_2", reloaded.Recipes);
        Assert.DoesNotContain("recipe_badge_sentinel_2", reloaded.NewestRecipes);
        Assert.Equal(originalBadge.OrderBy(x => x, StringComparer.Ordinal),
            reloaded.NewestRecipes.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ApplyJournals_Unlocking_AddsToJournalUnread_AndClearing_RemovesIt()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);

        var added = data.Journals.Append("journal_badge_sentinel").ToList();
        PlayerSaveWriter.ApplyJournals(data, added);
        var afterAdd = Reload(data);
        Assert.Contains("journal_badge_sentinel", afterAdd.JournalUnread);

        PlayerSaveWriter.ApplyJournals(afterAdd, data.Journals);
        var afterRemove = Reload(afterAdd);
        Assert.DoesNotContain("journal_badge_sentinel", afterRemove.JournalUnread);
    }

    [Fact]
    public void ApplyFishCaught_Catching_AddsToFishUnread_AndClearing_RemovesIt()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);

        var added = data.FishCaught.Append("fish_badge_sentinel").ToList();
        PlayerSaveWriter.ApplyFishCaught(data, added);
        var afterAdd = Reload(data);
        Assert.Contains("fish_badge_sentinel", afterAdd.FishUnread);

        PlayerSaveWriter.ApplyFishCaught(afterAdd, data.FishCaught);
        var afterRemove = Reload(afterAdd);
        Assert.DoesNotContain("fish_badge_sentinel", afterRemove.FishUnread);
    }

    [Fact]
    public void ApplyCompendium_Unlocking_AddsToCompendiumUnread_AndClearing_RemovesIt()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);

        var email = data.CompendiumEmail.Append("compendium_badge_sentinel").ToList();
        PlayerSaveWriter.ApplyCompendium(data, email, data.CompendiumNarrative, data.CompendiumExploration);
        var afterAdd = Reload(data);
        Assert.Contains("compendium_badge_sentinel", afterAdd.CompendiumUnread);

        PlayerSaveWriter.ApplyCompendium(afterAdd, data.CompendiumEmail, data.CompendiumNarrative, data.CompendiumExploration);
        var afterRemove = Reload(afterAdd);
        Assert.DoesNotContain("compendium_badge_sentinel", afterRemove.CompendiumUnread);
    }

    // ---------- research queue ----------

    [Fact]
    public void ApplyResearchQueue_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        var updated = data.RecipesRequiringResearch.Append("recipe_research_sentinel").ToList();
        PlayerSaveWriter.ApplyResearchQueue(data, updated);

        var reloaded = Reload(data);
        Assert.Contains("recipe_research_sentinel", reloaded.RecipesRequiringResearch);
    }

    [Fact]
    public void ApplyResearchQueue_MissingTag_CreatesFullHashName_AndReadsBack()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        RemoveTag(data, "RecipesRequiringResearch_");
        var stripped = Reload(data);
        Assert.Empty(stripped.RecipesRequiringResearch);

        var queue = new List<string> { "recipe_from_missing_tag" };
        PlayerSaveWriter.ApplyResearchQueue(stripped, queue);
        var reloaded = Reload(stripped);
        Assert.Contains("recipe_from_missing_tag", reloaded.RecipesRequiringResearch);
        Assert.Contains(GetCharacterSaveData(reloaded.Raw),
            tag => tag.Name?.Value == PlayerSaveWriterFullNames.RecipesRequiringResearch);
    }

    [Fact]
    public void ApplyResearchQueue_EmptyAgainstMissingTag_StaysByteIdentical()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        RemoveTag(data, "RecipesRequiringResearch_");
        var stripped = Reload(data);
        using var before = new MemoryStream();
        stripped.Raw.WriteTo(before);

        PlayerSaveWriter.ApplyResearchQueue(stripped, Array.Empty<string>());
        using var after = new MemoryStream();
        stripped.Raw.WriteTo(after);
        Assert.Equal(before.ToArray(), after.ToArray());
    }

    // ---------- CompletedIntro_ / LastControlRotation_ ----------

    [Fact]
    public void ApplyCompletedIntro_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        PlayerSaveWriter.ApplyCompletedIntro(data, !data.CompletedIntro);
        var reloaded = Reload(data);
        Assert.Equal(!data.CompletedIntro, reloaded.CompletedIntro);
    }

    [Fact]
    public void ApplyCompletedIntro_MissingTag_CreatesFullHashName()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        RemoveTag(data, "CompletedIntro_");
        var stripped = Reload(data);
        Assert.False(stripped.CompletedIntro);

        PlayerSaveWriter.ApplyCompletedIntro(stripped, true);
        var reloaded = Reload(stripped);
        Assert.True(reloaded.CompletedIntro);
        Assert.Contains(GetCharacterSaveData(reloaded.Raw),
            tag => tag.Name?.Value == PlayerSaveWriterFullNames.CompletedIntro);
    }

    [Fact]
    public void ApplyLastControlRotation_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        PlayerSaveWriter.ApplyLastControlRotation(data, 12.5, 250.25, 0);
        var reloaded = Reload(data);
        Assert.Equal(12.5, reloaded.LastControlRotationPitch, 3);
        Assert.Equal(250.25, reloaded.LastControlRotationYaw, 3);
        Assert.Equal(0, reloaded.LastControlRotationRoll, 3);
    }

    [Fact]
    public void ApplyLastControlRotation_MissingTag_CreatesFullHashName()
    {
        var path = FixturePlayerSave();
        var data = PlayerSaveReader.ReadFromFile(path);
        RemoveTag(data, "LastControlRotation_");
        var stripped = Reload(data);
        Assert.Equal(0, stripped.LastControlRotationPitch);
        Assert.Equal(0, stripped.LastControlRotationYaw);

        PlayerSaveWriter.ApplyLastControlRotation(stripped, 5, 10, 15);
        var reloaded = Reload(stripped);
        Assert.Equal(5, reloaded.LastControlRotationPitch, 3);
        Assert.Equal(10, reloaded.LastControlRotationYaw, 3);
        Assert.Equal(15, reloaded.LastControlRotationRoll, 3);
        Assert.Contains(GetCharacterSaveData(reloaded.Raw),
            tag => tag.Name?.Value == PlayerSaveWriterFullNames.LastControlRotation);
    }

    // ---------- helpers ----------

    /// <summary>Removes a top-level <c>CharacterSaveData</c> tag by prefix, simulating a save
    /// where the game delta-serialized it away, the same technique
    /// <c>PlayerSurvivalStatDefaultTests</c> uses for survival stats.</summary>
    private static void RemoveTag(PlayerSaveData data, string prefix)
    {
        var root = GetCharacterSaveData(data.Raw);
        var tag = root.FirstOrDefault(t => t.Name?.Value?.StartsWith(prefix, StringComparison.Ordinal) == true);
        if (tag is not null) root.Remove(tag);
    }

    private static IList<FPropertyTag> GetCharacterSaveData(SaveGame save)
    {
        var top = save.Properties!.Single(t => t.Name.Value == "CharacterSaveData");
        return ((PropertiesStruct)((StructProperty)top.Property!).Value!).Properties;
    }
}

/// <summary>
/// Local mirror of the full hash-suffixed names asserted against
/// <see cref="PlayerSaveWriter"/>'s internal <c>FullNames</c> table (that table is
/// <c>internal</c>, so tests can't reference it directly across assemblies without
/// <c>InternalsVisibleTo</c> - this keeps the assertions honest without adding one).
/// </summary>
internal static class PlayerSaveWriterFullNames
{
    public const string RecipesRequiringResearch = "RecipesRequiringResearch_89_0A2778A74B6F1090075D8A9BEE7A0361";
    public const string CompletedIntro = "CompletedIntro_26_7F0FCDEA4BA0DD4D229BF38724FF442C";
    public const string LastControlRotation = "LastControlRotation_69_33E2359F425EBFDFB5CE2D84DCE6AD1B";
}
