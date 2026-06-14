using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>The four pet families the editor groups creatures into.</summary>
public enum PetCategory
{
    Pest,
    Peccary,
    Skink,
    Other,
}

/// <summary>
/// One selectable pet creature: its blueprint class, family, and friendly name.
/// </summary>
/// <param name="ClassPath">Full soft-object class path
/// (<c>/Game/Blueprints/Characters/NPCs/NPC_Peccary_Sow.NPC_Peccary_Sow_C</c>) - the value
/// written into a pet's <c>NPCClass_</c> when upgrading / downgrading.</param>
/// <param name="ShortClass">Blueprint name without the <c>_C</c> suffix
/// (<c>NPC_Peccary_Sow</c>).</param>
/// <param name="FriendlyName">Wiki display name (e.g. "Peccary Sow").</param>
/// <param name="IsSummon">Armor-set summon (Exor / Mystagogue) - shown, not a valid
/// tame-able / editable target.</param>
/// <param name="IsEditable">False for summons; the upgrade picker should not offer it.</param>
public sealed record PetVariant(
    string ClassPath,
    string ShortClass,
    string FriendlyName,
    PetCategory Category,
    bool IsSummon = false,
    bool IsEditable = true);

/// <summary>
/// Reference data for the pet system: the family/variant catalog and the XP->level curve.
///
/// "Do both" sourcing (see plan): a curated table (below) provides friendly names,
/// categories, and a working offline fallback; <see cref="BuildVariants"/> merges in the
/// real classes enumerated from the mounted game paks so new variants the game adds appear
/// automatically. New variants of a known family are picked up with no code change; a
/// genuinely new family needs one line added to <see cref="CategoryTokens"/>.
///
/// Pets are level 0-20; level is derived from a cumulative-XP curve fit to the two values
/// the wiki publishes (level 1 = 4 XP, level 20 = 750 XP). The stored <c>XP</c> integer is
/// the source of truth; the level is a derived label.
/// </summary>
public static class PetCatalog
{
    /// <summary>Highest pet level.</summary>
    public const int MaxLevel = 20;

    private const string NpcFolder = "/Game/Blueprints/Characters/NPCs/";

    private static string ClassPathFor(string shortClass) => $"{NpcFolder}{shortClass}.{shortClass}_C";

    /// <summary>
    /// Family-detection tokens, scanned against a class short-name (case-insensitive),
    /// first match wins. This is the extensibility seam: add a token here to teach the
    /// editor a brand-new family. "ccary" also catches "Tareccary".
    /// </summary>
    private static readonly (string Token, PetCategory Category, bool IsSummon)[] CategoryTokens =
    {
        ("Skink", PetCategory.Skink, false),
        ("Peccary", PetCategory.Peccary, false),
        ("ccary", PetCategory.Peccary, false),
        ("Pest", PetCategory.Pest, false),
        ("Exor", PetCategory.Other, true),
        ("Mystagogue", PetCategory.Other, true),
    };

    /// <summary>
    /// Curated variants seeded from the wiki. Blueprint short-names follow the confirmed
    /// convention (<c>NPC_Monster_Pest_Electro</c>, <c>NPC_Peccary_Sow</c> verified against
    /// real saves); the rest are best-effort and get overwritten by the real class path
    /// when the paks are available (matched by short-name in <see cref="BuildVariants"/>).
    /// </summary>
    private static readonly (string Short, string Friendly, PetCategory Cat, bool Summon)[] CuratedSeed =
    {
        // Pests
        ("NPC_Monster_Pest",             "Pest",            PetCategory.Pest, false),
        ("NPC_Monster_Pest_Carbonated",  "Carbonated Pest", PetCategory.Pest, false),
        ("NPC_Monster_Pest_Electro",     "Electro Pest",    PetCategory.Pest, false),
        ("NPC_Monster_Pest_Enlightened", "Enlightened Pest",PetCategory.Pest, false),
        ("NPC_Monster_Pest_Leyak",       "Leyak Pest",      PetCategory.Pest, false),
        ("NPC_Monster_Pest_Magma",       "Magma Pest",      PetCategory.Pest, false),
        ("NPC_Monster_Pest_Rattus",      "Rattus Pestis",   PetCategory.Pest, false),
        ("NPC_Monster_Pest_Snow",        "Snow Pest",       PetCategory.Pest, false),
        ("NPC_Monster_Pest_Volatile",    "Volatile Pest",   PetCategory.Pest, false),
        // Peccaries
        ("NPC_Peccary",                  "Peccary",         PetCategory.Peccary, false),
        ("NPC_Peccary_Electro",          "Electro Peccary", PetCategory.Peccary, false),
        ("NPC_Peccary_Mushroom",         "Mushroom Peccary",PetCategory.Peccary, false),
        ("NPC_Peccary_Alpha",            "Peccary Alpha",   PetCategory.Peccary, false),
        ("NPC_Peccary_Sow",              "Peccary Sow",     PetCategory.Peccary, false),
        ("NPC_Peccary_Snow",            "Snow Peccary",     PetCategory.Peccary, false),
        ("NPC_Peccary_Tareccary",        "Tareccary",       PetCategory.Peccary, false),
        ("NPC_Peccary_Volatile",         "Volatile Peccary",PetCategory.Peccary, false),
        // Skinks
        ("NPC_Skink",                    "Skink",           PetCategory.Skink, false),
        ("NPC_Skink_Magma",              "Magma Skink",     PetCategory.Skink, false),
        // Other / summons (shown, not editable)
        ("NPC_Exor",                     "Exor Spirit",     PetCategory.Other, true),
        ("NPC_Mystagogue",               "Mystagogue Drone",PetCategory.Other, true),
    };

    private static readonly IReadOnlyList<PetVariant> _curated = BuildCurated();

    private static List<PetVariant> BuildCurated()
    {
        var list = new List<PetVariant>(CuratedSeed.Length);
        foreach (var (shortClass, friendly, cat, summon) in CuratedSeed)
        {
            list.Add(new PetVariant(ClassPathFor(shortClass), shortClass, friendly, cat, summon, IsEditable: !summon));
        }
        return list;
    }

    /// <summary>The offline curated variant list (no paks required).</summary>
    public static IReadOnlyList<PetVariant> Curated => _curated;

    private const string CompendiumDir = "/Game/Textures/GUI/Compendium/Entries/T_Compendium_";

    // Pets whose bestiary texture name the heuristic below can't reconstruct from the class.
    private static readonly Dictionary<string, string> CompendiumOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NPC_Monster_Pest_Rattus"] = "RattusPestis", // not "RattusPest"/"PestRattus"
        };

    /// <summary>
    /// Ordered in-pak compendium portrait refs to try for a pet's appearance
    /// (<c>/Game/...</c> object paths for
    /// <see cref="Assets.GameAssetProvider.ExtractTextureByGameRef"/>), most-likely first.
    /// The game's bestiary art is named by variant, e.g. <c>T_Compendium_ElectroPest</c>,
    /// <c>T_Compendium_PeccarySow</c>; this reconstructs both word orders from the blueprint
    /// class and falls back to the family base. Empty for an empty input; a class with no
    /// portrait (e.g. the Mystagogue summon) simply yields refs that don't resolve.
    /// </summary>
    public static IReadOnlyList<string> CompendiumTextureRefs(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        if (shortClass.Length == 0) return Array.Empty<string>();

        var names = new List<string>();
        if (CompendiumOverrides.TryGetValue(shortClass, out var ov)) names.Add(ov);

        var s = shortClass;
        foreach (var p in new[] { "NPC_Monster_", "NPC_" })
        {
            if (s.StartsWith(p, StringComparison.Ordinal)) { s = s[p.Length..]; break; }
        }
        var parts = s.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            names.Add(string.Concat(parts));                                  // PeccarySow, Pest
            if (parts.Length >= 2)
            {
                names.Add(parts[^1] + string.Concat(parts[..^1]));           // ElectroPest (variant+family)
            }
            names.Add(parts[^1]);                                             // family base
            names.Add(parts[0]);
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => CompendiumDir + n)
            .ToList();
    }

    /// <summary>The short class-name (without <c>_C</c>) of a full path or short string.</summary>
    public static string ShortOf(string? classOrShort)
    {
        if (string.IsNullOrEmpty(classOrShort)) return string.Empty;
        var tail = classOrShort[(classOrShort.LastIndexOf('.') + 1)..];
        return tail.EndsWith("_C", StringComparison.Ordinal) ? tail[..^2] : tail;
    }

    /// <summary>The family a class belongs to (by curated entry, else token heuristic).</summary>
    public static PetCategory Categorize(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        foreach (var v in _curated)
        {
            if (string.Equals(v.ShortClass, shortClass, StringComparison.OrdinalIgnoreCase)) return v.Category;
        }
        foreach (var (token, cat, _) in CategoryTokens)
        {
            if (shortClass.Contains(token, StringComparison.OrdinalIgnoreCase)) return cat;
        }
        return PetCategory.Other;
    }

    /// <summary>True when a class short-name looks like a pet/creature we should list.</summary>
    public static bool IsPetClass(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        if (shortClass.Length == 0) return false;
        foreach (var v in _curated)
        {
            if (string.Equals(v.ShortClass, shortClass, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var (token, _, _) in CategoryTokens)
        {
            if (shortClass.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>True for armor-set summon classes (Exor / Mystagogue) - not editable.</summary>
    public static bool IsSummon(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        foreach (var v in _curated)
        {
            if (string.Equals(v.ShortClass, shortClass, StringComparison.OrdinalIgnoreCase)) return v.IsSummon;
        }
        foreach (var (token, _, summon) in CategoryTokens)
        {
            if (summon && shortClass.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Friendly name for a class: the curated label when known, otherwise a prettified
    /// short-name. Null only for an empty input.
    /// </summary>
    public static string? FriendlyName(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        if (shortClass.Length == 0) return null;
        foreach (var v in _curated)
        {
            if (string.Equals(v.ShortClass, shortClass, StringComparison.OrdinalIgnoreCase)) return v.FriendlyName;
        }
        return Prettify(shortClass);
    }

    private static string Prettify(string shortClass)
    {
        var s = shortClass;
        foreach (var prefix in new[] { "NPC_Monster_", "NPC_" })
        {
            if (s.StartsWith(prefix, StringComparison.Ordinal)) { s = s[prefix.Length..]; break; }
        }
        return s.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// The merged variant list: curated entries (with real class paths substituted when the
    /// paks have them) plus any pet-looking NPC classes the paks expose that aren't curated.
    /// Falls back to <see cref="Curated"/> when <paramref name="provider"/> is null or
    /// enumeration fails (graceful-degradation rule). Ordered by family then name.
    /// </summary>
    public static IReadOnlyList<PetVariant> BuildVariants(GameAssetProvider? provider)
    {
        var byShort = new Dictionary<string, PetVariant>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _curated) byShort[v.ShortClass] = v;

        if (provider is not null)
        {
            try
            {
                foreach (var (shortClass, classPath) in EnumerateNpcClasses(provider))
                {
                    if (!IsPetClass(shortClass)) continue;
                    if (byShort.TryGetValue(shortClass, out var curated))
                    {
                        // Keep curated metadata, adopt the real (verified) class path.
                        byShort[shortClass] = curated with { ClassPath = classPath };
                    }
                    else
                    {
                        var summon = IsSummon(shortClass);
                        byShort[shortClass] = new PetVariant(
                            classPath, shortClass, Prettify(shortClass), Categorize(shortClass),
                            summon, IsEditable: !summon);
                    }
                }
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("PetCatalog", $"Pak enumeration failed; using curated list. {ex.Message}");
            }
        }

        return byShort.Values
            .OrderBy(v => v.Category)
            .ThenBy(v => v.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<(string Short, string ClassPath)> EnumerateNpcClasses(GameAssetProvider provider)
    {
        foreach (var key in provider.AssetPaths)
        {
            if (key.IndexOf("Characters/NPCs/", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileNameWithoutExtension(key);
            if (!name.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase)) continue;

            // "AbioticFactor/Content/Blueprints/Characters/NPCs/NPC_Peccary_Sow.uasset"
            //   -> "/Game/Blueprints/Characters/NPCs/NPC_Peccary_Sow.NPC_Peccary_Sow_C"
            var pkg = key[..^".uasset".Length];
            var idx = pkg.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
            pkg = idx >= 0 ? "/Game" + pkg[(idx + "/Content".Length)..] : pkg;
            yield return (name, $"{pkg}.{name}_C");
        }
    }

    // ---------- XP <-> level ----------

    private static readonly int[] _thresholds = BuildThresholds();

    private static int[] BuildThresholds()
    {
        // Cumulative XP per level, fit to the wiki's two anchors: level 1 = 4, level 20 = 750.
        // cumulative(L) = 4 * L^b, with b chosen so cumulative(20) = 750.
        var t = new int[MaxLevel + 1];
        var b = Math.Log(750.0 / 4.0) / Math.Log(MaxLevel);
        for (var l = 1; l <= MaxLevel; l++)
        {
            t[l] = (int)Math.Round(4 * Math.Pow(l, b));
        }
        t[0] = 0;
        t[MaxLevel] = 750; // pin the published endpoint exactly
        // Guarantee monotonic non-decreasing (rounding can't break it here, but be safe).
        for (var l = 1; l <= MaxLevel; l++)
        {
            if (t[l] <= t[l - 1]) t[l] = t[l - 1] + 1;
        }
        return t;
    }

    /// <summary>Derives the level (0-20) for a stored XP value.</summary>
    public static int LevelForXp(int xp)
    {
        if (xp <= 0) return 0;
        var level = 0;
        for (var l = 1; l <= MaxLevel; l++)
        {
            if (xp >= _thresholds[l]) level = l; else break;
        }
        return level;
    }

    /// <summary>The minimum cumulative XP for a level (0-20), for editing by level.</summary>
    public static int XpForLevel(int level)
    {
        if (level <= 0) return 0;
        if (level >= MaxLevel) return _thresholds[MaxLevel];
        return _thresholds[level];
    }
}
