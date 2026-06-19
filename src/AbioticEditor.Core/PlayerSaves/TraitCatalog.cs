using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Full detail for one trait/background row from <c>CDT_AllTraits</c>.
/// </summary>
public sealed record TraitDetail(
    string Id,
    string DisplayName,
    string? Description,
    int PointCost,
    bool IsBackground,
    bool AvailableOnStart = true);

/// <summary>
/// Internal-id -> display-name catalog for traits and backgrounds (jobs/PhDs), dumped from
/// the game's <c>CDT_AllTraits</c> / <c>DT_PhDs</c> DataTables. Positive/negative split
/// follows the wiki's trait listing.
/// </summary>
public static class TraitCatalog
{
    public static IReadOnlyDictionary<string, string> Backgrounds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NoPhD"] = "Lab Assistant",
        ["PhD_Medicine"] = "Epimedical Bionomicist",
        ["PhD_HumanBio"] = "Trans-Kinematic Researcher",
        ["PhD_MechEng"] = "Archotechnic Consultant",
        ["PhD_Agriculture"] = "Phytogenetic Botanist",
        ["PhD_NutritonalSci"] = "Somatic Gastrologist",
        ["PhD_TheoreticalPhys"] = "Paratheoretical Physicist",
        ["PhD_DefenseSecurity"] = "Defense Analyst",
        ["PhD_Intern"] = "Summer Intern",
        ["PhD_Iron"] = "Iron Mode",
    };

    public static IReadOnlyDictionary<string, string> Traits { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Positive traits
        ["Trait_Decathlon"] = "Decathlon Competitor",
        ["Trait_FormerGuard"] = "Tough as Nails",
        ["Trait_WrinklyBrainmeat"] = "Wrinkly Brainmeat",
        ["Trait_NightOwl"] = "Night Owl",
        ["Trait_Chef"] = "Hobbyist Chef",
        ["Trait_Inconspicuous"] = "Inconspicuous",
        ["Trait_Outdoorsman"] = "Weathered",
        ["Trait_FannyPack"] = "Fanny Pack",
        ["Trait_ThickSkinned"] = "Thick Skinned",
        ["Trait_FirstAidCert"] = "First Aid Certification",
        ["Trait_Gardener"] = "Gardener",
        ["Trait_LightEater"] = "Light Eater",
        ["Trait_Moist"] = "Naturally Moist",
        ["Trait_SelfDefense"] = "Self Defense",
        ["Trait_SteelBladder"] = "Bladder of Steel",
        ["Trait_Strong"] = "Buff Brainiac",
        ["Trait_LeadBelly"] = "Lead Belly",
        // Negative traits
        ["Trait_FearOfViolence"] = "Fear of Violence",
        ["Trait_EasilyStartled"] = "Easily Startled",
        ["Trait_Feeble"] = "Feeble",
        ["Trait_Narcoleptic"] = "Narcoleptic",
        ["Trait_Agoraphobic"] = "Agoraphobic",
        ["Trait_Fumbler"] = "Fumbler",
        ["Trait_Asthmatic"] = "Asthmatic",
        ["Trait_Claustrophobic"] = "Claustrophobic",
        ["Trait_Clumsy"] = "Clumsy",
        ["Trait_Conspicuous"] = "Painfully Obvious",
        ["Trait_HeartyAppetite"] = "Hearty Appetite",
        ["Trait_Dry"] = "Dry Skin",
        ["Trait_Hemophobic"] = "Hemophobic",
        ["Trait_Dyslexia"] = "Dyslexia",
        ["Trait_RestlessSleeper"] = "Restless Sleeper",
        ["Trait_Smoker"] = "Smoker",
        ["Trait_SlowHealer"] = "Slow Healer",
        ["Trait_SlowLearner"] = "Slow Learner",
        ["Trait_Hemophilia"] = "Hemophilia",
        ["Trait_Unlucky"] = "Unlucky",
        ["Trait_WeakBladder"] = "Weak Bladder",
        ["Trait_Cannibal"] = "Forbidden Diet",
        // World-obtained
        ["Trait_SunDisk"] = "Sun-Touched",
    };

    public static string DisplayNameFor(string id)
        => Traits.TryGetValue(id, out var t) ? t
         : Backgrounds.TryGetValue(id, out var b) ? b
         : id;

    /// <summary>
    /// Loads full trait details (descriptions, point costs) from the game's
    /// <c>CDT_AllTraits</c> table. Returns an empty dictionary when assets are
    /// unavailable - callers fall back to the static name maps above.
    /// </summary>
    private const string PrimaryTable = "AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits";

    public static IReadOnlyDictionary<string, TraitDetail> LoadDetailsFrom(GameAssetProvider provider)
    {
        var result = new Dictionary<string, TraitDetail>(StringComparer.Ordinal);
        if (!provider.HasMappings) return result;

        string? structName = null;
        try
        {
            var primary = provider.TryLoadDataTable(PrimaryTable);
            structName = primary?.RowStructName;
            ParseRows(primary, result);
        }
        catch
        {
            // Details are optional polish.
        }

        // Traits are id-keyed (not positional), so merging mod/patch trait tables that share
        // the row struct is safe. Base rows already present win on id conflict.
        foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, structName, new[] { PrimaryTable }))
        {
            ParseRows(dt, result);
        }
        return result;
    }

    private static void ParseRows(UDataTable? dt, Dictionary<string, TraitDetail> result)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            var id = kv.Key.Text;
            if (string.IsNullOrEmpty(id) || result.ContainsKey(id)) continue;

            string? name = null, description = null;
            var cost = 0;
            var availableOnStart = true;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                if (n.StartsWith("TraitName_", StringComparison.Ordinal))
                {
                    name = p.Tag?.GenericValue?.ToString();
                }
                else if (n.StartsWith("TraitDescription_", StringComparison.Ordinal))
                {
                    description = p.Tag?.GenericValue?.ToString();
                }
                else if (n.StartsWith("PointCost_", StringComparison.Ordinal))
                {
                    cost = p.Tag?.GenericValue switch { int i => i, byte b => b, _ => 0 };
                }
                else if (n.StartsWith("AvailableOnStart", StringComparison.Ordinal))
                {
                    availableOnStart = p.Tag?.GenericValue is not bool b || b;
                }
            }
            // Cut traits (Dyslexia, Fumbler, ...) carry genuinely empty FText
            // descriptions - normalize to null so the UI can fall back.
            if (string.IsNullOrWhiteSpace(description)) description = null;
            var isBackground = Backgrounds.ContainsKey(id) || !id.StartsWith("Trait_", StringComparison.Ordinal);
            result[id] = new TraitDetail(id, name ?? DisplayNameFor(id), description, cost, isBackground, availableOnStart);
        }
    }
}
