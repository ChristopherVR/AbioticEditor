namespace AbioticEditor.Core;

/// <summary>Matches legacy Blueprint fields and their native counterparts introduced in 1.5.</summary>
public static class UnrealPropertyNames
{
    public static bool MatchesUnrealProperty(this string name, string prefix)
    {
        if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        var stem = prefix.TrimEnd('_');
        return string.Equals(name, stem, StringComparison.Ordinal)
            || (name.Length == stem.Length + 1 && name[0] == 'b'
                && name.AsSpan(1).SequenceEqual(stem));
    }
}
