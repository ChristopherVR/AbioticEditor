using System.Text;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Steam;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Services;

#pragma warning disable CA1716 // Namespace matches the existing Razor component folder.
namespace AbioticEditor.Web.Components.Shared;
#pragma warning restore CA1716

/// <summary>
/// Turns the game's internal names into something a player can read.
///
/// <para>Save files are full of machine names: level tokens like <c>Facility_MFWest</c>,
/// blueprint classes like <c>ABF_Deployable_Storage_Locker_C</c>, story markers like
/// <c>MF_MetFrake</c> and long account numbers. None of that means anything to somebody who
/// just plays the game, so every screen routes those through here before showing them. The
/// original text is never changed in the save - this only affects what is displayed.</para>
///
/// <para>Each helper falls back gracefully: a curated name first, then a tidied-up version of
/// the machine name (underscores and run-together words split into real words), and only as a
/// last resort the machine name itself.</para>
/// </summary>
public static class PlainNames
{
    /// <summary>
    /// Splits a machine name into ordinary words: <c>LeftArm</c> becomes "Left Arm",
    /// <c>Office_TalkedToWarren</c> becomes "Office Talked To Warren". Returns an empty
    /// string for nothing. Plain numbers are already readable and come back untouched.
    /// </summary>
    public static string Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();

        // Stored values are not always names: a lot of world settings are just numbers, and
        // "1234.5" is already perfectly readable. Hand those straight back, so the dot is
        // never mistaken for the separator in a qualified name and the whole number survives.
        if (NumericValue.IsMatch(text)) return text;

        // Enum values arrive as "E_LimbType::NewEnumerator3" or "SomeType::Value"; only the
        // part after the last separator carries meaning.
        var separator = text.LastIndexOf("::", StringComparison.Ordinal);
        if (separator >= 0) text = text[(separator + 2)..];

        // Same idea for a dotted name such as "Package.Thing": the tail is the only part
        // worth showing. A tail made only of digits is a fraction, not a name, so it stays.
        var dot = text.LastIndexOf('.');
        if (dot >= 0 && dot < text.Length - 1 && !IsAllDigits(text.AsSpan(dot + 1)))
            text = text[(dot + 1)..];

        var builder = new StringBuilder(text.Length + 8);
        foreach (var chunk in text.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var word in SplitRunTogether(chunk))
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1) builder.Append(word[1..]);
            }
        }
        return builder.Length == 0 ? text : builder.ToString();
    }

    /// <summary>Whole numbers and decimals, with or without a sign or an exponent.</summary>
    private static readonly Regex NumericValue =
        new(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant);

    private static bool IsAllDigits(ReadOnlySpan<char> text)
    {
        foreach (var character in text) { if (!char.IsAsciiDigit(character)) return false; }
        return text.Length > 0;
    }

    /// <summary>
    /// A readable name for a story marker: the chapter it belongs to when the editor knows
    /// one, otherwise the marker's own friendly wording ("MF: Met Frake").
    /// </summary>
    public static string Flag(HostLanguageService language, string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return string.Empty;
        if (StoryProgressionCatalog.ChapterForFlag(flag) is { } chapter)
        {
            var title = language.ResourceOrNull($"WorldStory_ChapterTitle_{chapter.Row}");
            if (!string.IsNullOrWhiteSpace(title)) return title;
            if (!string.IsNullOrWhiteSpace(chapter.Title)) return chapter.Title;
        }
        var info = QuestFlagCatalog.Lookup(flag);
        return string.IsNullOrWhiteSpace(info.FriendlyName) ? Words(flag) : info.FriendlyName;
    }

    /// <summary>Several story markers as one readable list.</summary>
    public static string Flags(HostLanguageService language, IEnumerable<string> flags)
        => string.Join(", ", flags.Select(flag => Flag(language, flag)));

    /// <summary>
    /// A readable name for one piece of stored save data. The game tags each one with a build
    /// code (something like <c>Hunger_2_A6C5CC6E…</c>) that changes between game updates and
    /// means nothing to a player, so the code is dropped and the rest spelled out.
    /// </summary>
    public static string Property(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var leaf = path.Trim();
        var dot = leaf.LastIndexOf('.');
        if (dot >= 0 && dot < leaf.Length - 1) leaf = leaf[(dot + 1)..];
        var match = BuildCodeSuffix.Match(leaf);
        if (match.Success) leaf = match.Groups[1].Value;
        return Words(leaf);
    }

    private static readonly Regex BuildCodeSuffix = new(@"^(.*?)_\d+_[0-9A-Fa-f]{8,}$", RegexOptions.CultureInvariant);

    /// <summary>The in-game area a level token belongs to ("Manufacturing West").</summary>
    public static string Area(string? levelToken)
        => WorldAreaCatalog.FriendlyName(levelToken) is { Length: > 0 } friendly ? friendly : Words(levelToken);

    /// <summary>
    /// A readable name for a placed object built from its blueprint name: the package path,
    /// the editor's own prefixes and the trailing marker are dropped, then the remaining
    /// words are separated ("ABF_Deployable_Storage_Locker_C" becomes "Storage Locker").
    /// </summary>
    public static string Thing(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return string.Empty;
        var tail = className.Trim();
        var slash = tail.LastIndexOfAny(['/', '.', ':']);
        if (slash >= 0 && slash < tail.Length - 1) tail = tail[(slash + 1)..];
        if (tail.EndsWith("_C", StringComparison.Ordinal)) tail = tail[..^2];
        foreach (var prefix in Prefixes)
        {
            if (tail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = tail[prefix.Length..].Trim('_');
                if (trimmed.Length > 0) tail = trimmed;
                break;
            }
        }
        return Words(tail);
    }

    private static readonly string[] Prefixes =
    [
        "ABF_Deployable_", "ABF_Vehicle_", "ABF_Item_", "ABF_NPC_", "ABF_",
        "BP_Deployable_", "BP_Vehicle_", "BP_Item_", "BP_", "Deployable_", "Vehicle_",
    ];

    /// <summary>
    /// What to call a player. Uses the name their account shows in Steam on this machine when
    /// it is known, and otherwise a short tail of the account number so two co-op players are
    /// still told apart without printing seventeen digits.
    /// </summary>
    public static string Player(HostLanguageService language, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return language.Resource("PlayerUi_Unknown");
        if (Personas.Value.TryGetValue(accountId, out var persona))
        {
            var clean = PersonaNames.Sanitize(persona);
            if (clean.Length > 0) return clean;
        }
        return accountId.Length > 6 ? "…" + accountId[^6..] : accountId;
    }

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Personas =
        new(SteamPersonaIndex.LoadMachineAccounts);

    /// <summary>
    /// Splits run-together words on the usual boundaries: a capital after a lower-case letter,
    /// the last capital of a run that starts a new word, and letter/digit changes.
    /// </summary>
    private static IEnumerable<string> SplitRunTogether(string chunk)
    {
        var start = 0;
        for (var i = 1; i < chunk.Length; i++)
        {
            var previous = chunk[i - 1];
            var current = chunk[i];
            var boundary =
                (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                || (char.IsUpper(current) && char.IsUpper(previous) && i + 1 < chunk.Length && char.IsLower(chunk[i + 1]))
                || (char.IsDigit(current) && char.IsLetter(previous));
            if (!boundary) continue;
            yield return chunk[start..i];
            start = i;
        }
        if (start < chunk.Length) yield return chunk[start..];
    }
}
