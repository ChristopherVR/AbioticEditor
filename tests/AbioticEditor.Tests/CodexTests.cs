using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class CodexTests
{
    private readonly ITestOutputHelper _output;

    public CodexTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_EmailsJournalsCompendiumState()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        _output.WriteLine($"emails read: {data.EmailsRead.Count}, journals: {data.Journals.Count}, compendium: {data.CompendiumUnlocked.Count}");
        Assert.True(data.EmailsRead.Count > 50);
        Assert.True(data.Journals.Count > 20);
        Assert.True(data.CompendiumUnlocked.Count > 50);
        Assert.Contains("Email_ThisDamnFacility", data.EmailsRead);
    }

    [Fact]
    public void CodexCatalog_LoadsFullContent()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var emails = CodexCatalog.LoadEmails(provider);
        var journals = CodexCatalog.LoadJournals(provider);
        var compendium = CodexCatalog.LoadCompendium(provider);

        _output.WriteLine($"emails: {emails.Count}, journals: {journals.Count}, compendium: {compendium.Count}");
        Assert.True(emails.Count >= 190);
        Assert.True(journals.Count >= 130);
        Assert.True(compendium.Count >= 180);

        var dam = emails.First(e => e.Id == "Email_ThisDamnFacility");
        Assert.Equal("THIS DAMN FACILITY", dam.Subject);
        Assert.Equal(2, dam.Sections.Count);
        Assert.Contains("power sockets", dam.Sections[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("lsimmons@", dam.FirstSender);

        var crossbow = emails.First(e => e.Id == "Email_Crossbow");
        Assert.Contains("recipe_crossbow", crossbow.AttachmentRecipes);

        var journal = journals.First(j => j.Id == "BuildCraftingBench");
        Assert.Equal("BUILD A CRAFTING BENCH", journal.Title);
        Assert.NotEmpty(journal.Note);

        var bot = compendium.First(c => c.Id == "SecurityBot");
        Assert.Equal("G.A.S.S.", bot.Title);
        Assert.Equal("Security Bot", bot.Subtitle);
        Assert.NotEmpty(bot.SectionTexts);
    }

    [Fact]
    public void Fish_TimeOfDayAndBaitTagsParse()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var fish = CodexCatalog.LoadFish(provider);

        // MoonFish: night-only (midnight high, dawn/noon/dusk zero).
        var moon = fish.First(f => f.Id == "MoonFish");
        Assert.True(moon.MidnightMult > 1);
        Assert.Equal(0, moon.DawnMult);
        Assert.Equal(0, moon.NoonMult);
        Assert.True(moon.HasTimePreference);

        // Eel_rare3: cannot be caught at night (midnight multiplier 0).
        var eel3 = fish.First(f => f.Id == "Eel_rare3");
        Assert.Equal(0, eel3.MidnightMult);

        // Antefish bites best during the day (noon highest), neutral at night.
        var ante = fish.First(f => f.Id == "Antefish");
        Assert.True(ante.NoonMult > ante.MidnightMult);
        Assert.True(ante.HasTimePreference);

        // Rare variants name the specific bait they need.
        var anteRare = fish.First(f => f.Id == "Antefish_rare1");
        Assert.Equal("Fishing.Bait.Antefish", anteRare.RequiredBaitTag);
        Assert.True(anteRare.RequiresSpecialCatch);

        // Most bait gameplay tags resolve to a real craftable bait item (a few fish, e.g.
        // Fogfish, have no craftable bait; the UI just omits the bait row for those).
        var cat = ItemCatalog.LoadFrom(provider);
        var baitByTag = cat.Entries
            .SelectMany(e => e.Tags.Where(t => t.StartsWith("Fishing.Bait", StringComparison.OrdinalIgnoreCase))
                .Select(t => (t, e)))
            .ToDictionary(x => x.t, x => x.e, StringComparer.OrdinalIgnoreCase);
        Assert.True(baitByTag.ContainsKey("Fishing.Bait.Antefish"));
        Assert.True(baitByTag.ContainsKey("Fishing.Bait.GemCrab"));
        Assert.True(baitByTag.ContainsKey("Fishing.Bait.MoonFish"));

        var needed = fish.Where(f => f.RequiredBaitTag is not null).Select(f => f.RequiredBaitTag!).Distinct().ToList();
        Assert.True(needed.Count(t => baitByTag.ContainsKey(t)) >= needed.Count - 3,
            "Most fish bait tags should resolve to an item.");
    }

    [Fact]
    public void Fish_CarryUnlockAndCatchRequirements()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var fish = CodexCatalog.LoadFish(provider);
        Assert.True(fish.Count >= 30);

        // Antefish unlocks a bait recipe on catch.
        var antefish = fish.First(f => f.Id == "Antefish");
        Assert.Equal("recipe_bait_antefish", antefish.UnlockRecipeId);
        Assert.False(string.IsNullOrWhiteSpace(antefish.Location));
        Assert.True(antefish.XpGain > 0);

        // Portalfish is gated behind a story flag.
        var portal = fish.First(f => f.Id == "Portalfish");
        Assert.Equal("Office_ThirdFloorReached", portal.RequiredWorldFlag);
        Assert.Equal("recipe_bait_portal", portal.UnlockRecipeId);

        // The recipe a fish unlocks resolves to a craftable bait item.
        var recipe = RecipeCatalog.LoadInfosFrom(provider)
            .FirstOrDefault(r => r.Id == antefish.UnlockRecipeId);
        Assert.NotNull(recipe);
        Assert.False(string.IsNullOrWhiteSpace(recipe!.CreatesItemId));

        // Rare variants carry a non-trivial catch tag-query.
        var rare = fish.First(f => f.Id == "Antefish_rare1");
        Assert.True(rare.IsRare);
        Assert.True(rare.RequiresSpecialCatch);
    }

    [Fact]
    public void ApplyEmailsAndJournals_RoundTripThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var emails = data.EmailsRead.Append("Email_Test_Sentinel").ToList();
        var journals = data.Journals.Where(j => j != data.Journals[0]).ToList();
        PlayerSaveWriter.ApplyEmailsRead(data, emails);
        PlayerSaveWriter.ApplyJournals(data, journals);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(emails, reloaded.EmailsRead);
        Assert.Equal(journals, reloaded.Journals);
    }
}
