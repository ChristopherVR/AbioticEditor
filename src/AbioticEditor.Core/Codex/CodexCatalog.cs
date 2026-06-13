using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Codex;

/// <summary>One paragraph of an email: who wrote it and what it says.</summary>
public sealed record EmailSection(string? Sender, string Text);

/// <summary>One email from <c>DT_Emails</c>; the save's <c>EmailsRead_</c> stores row names.</summary>
public sealed record EmailEntry(
    string Id,
    string Subject,
    IReadOnlyList<EmailSection> Sections,
    IReadOnlyList<string> AttachmentRecipes,
    IReadOnlyList<string> UnlocksJournals)
{
    public string? FirstSender => Sections.Count > 0 ? Sections[0].Sender : null;
}

/// <summary>One journal objective from <c>DT_JournalEntries</c> (<c>JournalEntries_</c> in saves).</summary>
public sealed record JournalEntry(string Id, string Title, string Note);

/// <summary>One compendium lore entry from <c>DT_Compendium</c>.</summary>
/// <param name="SectionTypes">
/// Each section's unlock requirement (<c>Email</c>/<c>Narrative</c>/<c>Exploration</c>)
/// determines which of the save's three compendium arrays carry this row when unlocked.
/// </param>
public sealed record CompendiumEntry(
    string Id,
    string Title,
    string? Subtitle,
    string? Tag,
    IReadOnlyList<string> SectionTexts,
    IReadOnlyList<string> SectionTypes,
    int? KillRequired = null);

/// <summary>
/// One catchable fish from <c>DT_Fish</c>. Display name/icon resolve via <see cref="ItemId"/>;
/// the remaining fields drive the journal detail: what catching it unlocks and what is
/// required to land it.
/// </summary>
/// <param name="Location">The water/biome the fish lives in (the row's <c>FishName</c> FText).</param>
/// <param name="UnlockRecipeId">Recipe row caught-for-the-first-time unlocks (usually a bait), or null.</param>
/// <param name="RequiredWorldFlag">A story flag the world must have before the fish can bite, or null.</param>
/// <param name="RequiredDlcId">A DLC row gating the fish, or null.</param>
/// <param name="RequiredBaitTag">Gameplay tag of the bait this fish needs to bite (rare variants), e.g. <c>Fishing.Bait.Antefish</c>; null when none.</param>
/// <param name="MidnightMult">Catch-chance multiplier at night (midnight); 1 = neutral, 0 = never, &gt;1 = best.</param>
/// <param name="DawnMult">Catch-chance multiplier at dawn.</param>
/// <param name="NoonMult">Catch-chance multiplier at midday (noon).</param>
/// <param name="DuskMult">Catch-chance multiplier at dusk.</param>
/// <param name="XpGain">XP awarded for catching it.</param>
public sealed record FishDefinition(
    string Id,
    string? ItemId,
    bool IsRare,
    string? Location = null,
    string? UnlockRecipeId = null,
    string? RequiredWorldFlag = null,
    string? RequiredDlcId = null,
    string? RequiredBaitTag = null,
    double MidnightMult = 1,
    double DawnMult = 1,
    double NoonMult = 1,
    double DuskMult = 1,
    int XpGain = 0)
{
    /// <summary>True when the row needs a specific bait/condition to bite (rare variants).</summary>
    public bool RequiresSpecialCatch => RequiredBaitTag is not null;

    /// <summary>True when some time-of-day differs from the neutral multiplier of 1.</summary>
    public bool HasTimePreference =>
        MidnightMult != 1 || DawnMult != 1 || NoonMult != 1 || DuskMult != 1;
}

/// <summary>
/// Loads the game's narrative content tables - emails, journal objectives and the
/// compendium - so the editor can show full text alongside per-save read/found state.
/// </summary>
public static class CodexCatalog
{
    public static IReadOnlyList<EmailEntry> LoadEmails(GameAssetProvider provider)
        => Load(provider, "AbioticFactor/Content/Blueprints/DataTables/Communications/DT_Emails", BuildEmail);

    public static IReadOnlyList<JournalEntry> LoadJournals(GameAssetProvider provider)
        => Load(provider, "AbioticFactor/Content/Blueprints/DataTables/Communications/DT_JournalEntries", BuildJournal);

    public static IReadOnlyList<CompendiumEntry> LoadCompendium(GameAssetProvider provider)
        => Load(provider, "AbioticFactor/Content/Blueprints/DataTables/DT_Compendium", BuildCompendium);

    public static IReadOnlyList<FishDefinition> LoadFish(GameAssetProvider provider)
        => Load(provider, "AbioticFactor/Content/Blueprints/DataTables/Fishing/DT_Fish", BuildFish);

    private static IReadOnlyList<T> Load<T>(
        GameAssetProvider provider, string path, Func<string, FStructFallback, T?> build) where T : class
    {
        if (!provider.HasMappings) return Array.Empty<T>();
        try
        {
            var pkg = provider.LoadPackageInternal(path);
            var result = new List<T>();
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                foreach (var kv in dt.RowMap)
                {
                    var id = kv.Key.Text;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (build(id, kv.Value) is { } entry) result.Add(entry);
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    // ---------- row builders ----------

    private static EmailEntry? BuildEmail(string id, FStructFallback row)
    {
        string subject = id;
        var sections = new List<EmailSection>();
        var attachmentRecipes = new List<string>();
        var unlocksJournals = new List<string>();

        foreach (var p in row.Properties)
        {
            var name = p.Name.Text;
            if (name.StartsWith("SubjectLineText_", StringComparison.Ordinal))
            {
                subject = p.Tag?.GenericValue?.ToString() ?? id;
            }
            else if (name.StartsWith("EmailSections_", StringComparison.Ordinal))
            {
                foreach (var section in StructArray(p.Tag?.GenericValue))
                {
                    string? sender = null, text = null;
                    foreach (var sp in section.Properties)
                    {
                        if (sp.Name.Text.StartsWith("EmailSenderName_", StringComparison.Ordinal))
                            sender = sp.Tag?.GenericValue?.ToString();
                        else if (sp.Name.Text.StartsWith("EmailText_", StringComparison.Ordinal))
                            text = sp.Tag?.GenericValue?.ToString();
                    }
                    if (!string.IsNullOrEmpty(text)) sections.Add(new EmailSection(sender, text!));
                }
            }
            else if (name.StartsWith("Attachments_", StringComparison.Ordinal))
            {
                foreach (var attachment in StructArray(p.Tag?.GenericValue))
                {
                    foreach (var ap in attachment.Properties)
                    {
                        if (!ap.Name.Text.StartsWith("RecipeToUnlock_", StringComparison.Ordinal)) continue;
                        var recipe = RowNameOf(ap.Tag?.GenericValue);
                        if (!string.IsNullOrEmpty(recipe) && recipe != "None") attachmentRecipes.Add(recipe!);
                    }
                }
            }
            else if (name.StartsWith("JournalEntriesToUnlock_", StringComparison.Ordinal))
            {
                if (p.Tag?.GenericValue is UScriptArray arr)
                {
                    foreach (var jp in arr.Properties)
                    {
                        var journal = RowNameOf(jp.GenericValue);
                        if (!string.IsNullOrEmpty(journal) && journal != "None") unlocksJournals.Add(journal!);
                    }
                }
            }
        }
        return new EmailEntry(id, subject, sections, attachmentRecipes, unlocksJournals);
    }

    private static JournalEntry? BuildJournal(string id, FStructFallback row)
    {
        string title = id, note = string.Empty;
        foreach (var p in row.Properties)
        {
            if (p.Name.Text.StartsWith("Title_", StringComparison.Ordinal))
                title = p.Tag?.GenericValue?.ToString() ?? id;
            else if (p.Name.Text.StartsWith("Note_", StringComparison.Ordinal))
                note = p.Tag?.GenericValue?.ToString() ?? string.Empty;
        }
        return new JournalEntry(id, title, note);
    }

    private static CompendiumEntry? BuildCompendium(string id, FStructFallback row)
    {
        string title = id;
        string? subtitle = null, tag = null;
        var sections = new List<string>();
        var sectionTypes = new List<string>();
        var hasKillSection = false;
        int? killRequired = null;

        foreach (var p in row.Properties)
        {
            switch (p.Name.Text)
            {
                case "bHasKillRequirementSection":
                    hasKillSection = p.Tag?.GenericValue is true;
                    break;
                case "KillRequirementSection":
                    var v = p.Tag?.GenericValue;
                    if (v is FScriptStruct kss) v = kss.StructType;
                    if (v is FStructFallback ksf)
                    {
                        var count = ksf.Properties.FirstOrDefault(sp => sp.Name.Text == "RequiredCount")?.Tag?.GenericValue;
                        killRequired = count switch { int i => i, byte b => b, _ => null };
                    }
                    break;
                case "Title": title = p.Tag?.GenericValue?.ToString() ?? id; break;
                case "Subtitle": subtitle = p.Tag?.GenericValue?.ToString(); break;
                case "Tags":
                    tag = p.Tag?.GenericValue?.ToString();
                    var paren = tag?.LastIndexOf('(') ?? -1;
                    if (paren > 0) tag = tag![..paren].Trim();
                    break;
                case "Sections":
                    foreach (var section in StructArray(p.Tag?.GenericValue))
                    {
                        var text = section.Properties
                            .FirstOrDefault(sp => sp.Name.Text == "SectionText")?.Tag?.GenericValue?.ToString();
                        if (!string.IsNullOrEmpty(text)) sections.Add(text!);

                        // "ECompendiumUnlockType::Exploration" -> "Exploration"
                        var req = section.Properties
                            .FirstOrDefault(sp => sp.Name.Text == "UnlockRequirement")?.Tag?.GenericValue?.ToString();
                        if (!string.IsNullOrEmpty(req))
                        {
                            var idx = req.LastIndexOf(':');
                            var type = idx >= 0 ? req[(idx + 1)..] : req;
                            if (!sectionTypes.Contains(type, StringComparer.Ordinal)) sectionTypes.Add(type);
                        }
                    }
                    break;
            }
        }
        return new CompendiumEntry(id, title, subtitle, tag, sections, sectionTypes,
            hasKillSection ? killRequired : null);
    }

    private static FishDefinition? BuildFish(string id, FStructFallback row)
    {
        string? itemId = null, location = null, unlockRecipe = null, worldFlag = null, dlc = null, baitTag = null;
        var isRare = false;
        double midnight = 1, dawn = 1, noon = 1, dusk = 1;
        var xp = 0;
        foreach (var p in row.Properties)
        {
            switch (p.Name.Text)
            {
                case "NormalItemRow":
                    itemId = NullIfNone(RowNameOf(p.Tag?.GenericValue));
                    break;
                case "FishName":
                    location = p.Tag?.GenericValue?.ToString()?.Trim() is { Length: > 0 } s ? s : null;
                    break;
                case "ChangeableDataTags":
                    isRare = p.Tag?.GenericValue?.ToString()?.Contains("Rare", StringComparison.OrdinalIgnoreCase) == true;
                    break;
                case "RecipeToUnlock":
                    unlockRecipe = NullIfNone(RowNameOf(p.Tag?.GenericValue));
                    break;
                case "WorldFlagRequirement":
                    worldFlag = NullIfNone(RowNameOf(p.Tag?.GenericValue));
                    break;
                case "RequiredDLC":
                    dlc = NullIfNone(RowNameOf(p.Tag?.GenericValue));
                    break;
                case "XPGain":
                    xp = p.Tag?.GenericValue switch { int i => i, byte b => b, _ => 0 };
                    break;
                case "CatchRequirement":
                    // A GameplayTagQuery; its TagDictionary names the bait the catch needs
                    // (rare variants), e.g. Fishing.Bait.Antefish.
                    baitTag = FirstBaitTag(p.Tag?.GenericValue);
                    break;
                case "TimeOfDayCatchChance":
                    var tod = AsStruct(p.Tag?.GenericValue);
                    if (tod is not null)
                    {
                        midnight = ReadDouble(tod, "MidnightMultiplier", 1);
                        dawn = ReadDouble(tod, "DawnMultiplier", 1);
                        noon = ReadDouble(tod, "NoonMultiplier", 1);
                        dusk = ReadDouble(tod, "DuskMultiplier", 1);
                    }
                    break;
            }
        }
        return new FishDefinition(
            id, itemId, isRare, location, unlockRecipe, worldFlag, dlc, baitTag, midnight, dawn, noon, dusk, xp);
    }

    private static string? NullIfNone(string? rowName)
        => string.IsNullOrEmpty(rowName) || string.Equals(rowName, "None", StringComparison.Ordinal)
            ? null
            : rowName;

    private static FStructFallback? AsStruct(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        return value as FStructFallback;
    }

    private static double ReadDouble(FStructFallback s, string field, double fallback)
        => s.Properties.FirstOrDefault(p => p.Name.Text == field)?.Tag?.GenericValue switch
        {
            float f => f,
            double d => d,
            int i => i,
            _ => fallback,
        };

    /// <summary>First <c>Fishing.Bait.*</c> tag named in a CatchRequirement's TagDictionary.</summary>
    private static string? FirstBaitTag(object? catchRequirement)
    {
        if (AsStruct(catchRequirement)?.Properties
                .FirstOrDefault(p => p.Name.Text == "TagDictionary")?.Tag?.GenericValue is not UScriptArray arr)
        {
            return null;
        }
        foreach (var e in arr.Properties)
        {
            var name = AsStruct(e.GenericValue)?.Properties
                .FirstOrDefault(p => p.Name.Text == "TagName")?.Tag?.GenericValue?.ToString();
            if (name is { Length: > 0 } && name.StartsWith("Fishing.Bait", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return null;
    }

    // ---------- helpers ----------

    private static IEnumerable<FStructFallback> StructArray(object? value)
    {
        if (value is not UScriptArray arr) yield break;
        foreach (var p in arr.Properties)
        {
            var v = p.GenericValue;
            if (v is FScriptStruct ss) v = ss.StructType;
            if (v is FStructFallback sf) yield return sf;
        }
    }

    private static string? RowNameOf(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        return sf.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
    }
}
