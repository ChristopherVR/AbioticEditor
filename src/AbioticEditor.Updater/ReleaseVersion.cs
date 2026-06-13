using System.Globalization;

namespace AbioticEditor.Updater;

/// <summary>
/// A small semver-ish version: numeric <c>major.minor.patch</c> plus an optional
/// pre-release tag, parsed leniently from a GitHub tag or an assembly version string.
/// Enough to answer the only question the updater asks - "is the release newer than what
/// is installed?" - without taking a dependency on a full SemVer library.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string? prerelease, string original)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Original = original;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release suffix (e.g. <c>beta.2</c>), or null for a stable release.</summary>
    public string? Prerelease { get; }

    /// <summary>True when this version carries a pre-release suffix.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>The exact text this was parsed from (tag as published, including any leading "v").</summary>
    public string Original { get; }

    /// <summary>
    /// Parses a tag like <c>v1.4.2</c>, <c>1.4</c>, <c>2.0.0-rc.1</c> or an assembly
    /// informational version like <c>1.4.2+sha.abcdef</c> (build metadata after "+" is
    /// ignored). Returns null when no leading numeric version can be found.
    /// </summary>
    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var original = text.Trim();
        var core = original;

        // Drop a leading "v"/"V" (the common Git tag convention).
        if (core.Length > 0 && (core[0] == 'v' || core[0] == 'V'))
        {
            core = core[1..];
        }

        // Build metadata after "+" never affects ordering.
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            core = core[..plus];
        }

        // Split the pre-release suffix off the numeric core.
        string? prerelease = null;
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = core[(dash + 1)..];
            core = core[..dash];
        }

        var parts = core.Split('.');
        if (parts.Length == 0 || !TryInt(parts[0], out var major))
        {
            return null;
        }

        var minor = parts.Length > 1 && TryInt(parts[1], out var mn) ? mn : 0;
        var patch = parts.Length > 2 && TryInt(parts[2], out var pt) ? pt : 0;

        return new ReleaseVersion(major, minor, patch, string.IsNullOrEmpty(prerelease) ? null : prerelease, original);
    }

    private static bool TryInt(string s, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCore = Major.CompareTo(other.Major);
        if (byCore != 0)
        {
            return byCore;
        }

        byCore = Minor.CompareTo(other.Minor);
        if (byCore != 0)
        {
            return byCore;
        }

        byCore = Patch.CompareTo(other.Patch);
        if (byCore != 0)
        {
            return byCore;
        }

        // Per SemVer: a version WITH a pre-release tag is LOWER than the same version
        // without one (1.0.0-rc < 1.0.0). Two pre-releases compare by ordinal text.
        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }
        if (Prerelease is null)
        {
            return 1;
        }
        if (other.Prerelease is null)
        {
            return -1;
        }
        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    /// <summary>True when <paramref name="other"/> is strictly newer than this version.</summary>
    public bool IsOlderThan(ReleaseVersion other) => CompareTo(other) < 0;

    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as ReleaseVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() => Original;

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => !(left == right);

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right)
        => Compare(left, right) < 0;

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right)
        => Compare(left, right) <= 0;

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right)
        => Compare(left, right) > 0;

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right)
        => Compare(left, right) >= 0;

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right)
        => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
