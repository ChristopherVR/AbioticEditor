using System.Reflection;
using System.Text.RegularExpressions;

namespace AbioticEditor.Web.Services;

/// <summary>One released version's player-facing notes, in release order (newest first, matching
/// <see cref="ChangelogService.Releases"/>).</summary>
public sealed record ChangelogRelease(string Version, string Date, IReadOnlyList<string> Highlights);

/// <summary>
/// Parses the repo's own <c>CHANGELOG.md</c> (git-cliff output, already written for players - see
/// CLAUDE.md's "Commit message tone") for the "what's new" dialog. Embedded into this assembly at
/// build time (see the csproj) rather than read from disk at runtime: a published build has no
/// guarantee the repo root is anywhere nearby, and the file never changes after publish anyway.
/// </summary>
public static class ChangelogService
{
    // git-cliff's own section headings this dialog is worth showing a player - everything else
    // (Build, CI, Documentation, Miscellaneous Tasks, Refactor, Styling, Testing) is dev-only
    // housekeeping with nothing a player would recognize as a change to the editor they use.
    private static readonly HashSet<string> PlayerFacingSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Features", "Bug Fixes", "Performance",
    };

    private static readonly Regex VersionHeading = new(@"^##\s*\[(?<version>[^\]]+)\]\s*-\s*(?<date>\S+)\s*$", RegexOptions.Compiled);
    private static readonly Regex SectionHeading = new(@"^###\s*(?<section>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletLine = new(@"^-\s*(?<text>.+?)\s*$", RegexOptions.Compiled);

    private static readonly Lazy<IReadOnlyList<ChangelogRelease>> Parsed = new(() => Parse(ReadEmbedded()));

    /// <summary>Every release this build knows about, newest first (the same order the changelog
    /// file itself lists them).</summary>
    public static IReadOnlyList<ChangelogRelease> Releases => Parsed.Value;

    /// <summary>
    /// Every release strictly newer than <paramref name="version"/>, newest first - what a player
    /// who last saw notes for that version has missed since. <paramref name="version"/> being null
    /// or not found in <see cref="Releases"/> (a first-ever launch, or a version string this build
    /// somehow does not recognize) returns every release rather than throwing or coming back
    /// empty, since a caller asking "what changed since X" with no real X to compare against
    /// should get something to show, not nothing.
    /// </summary>
    public static IReadOnlyList<ChangelogRelease> Since(string? version)
    {
        var releases = Releases;
        if (version is null) return releases;
        var index = releases.ToList().FindIndex(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? releases : releases.Take(index).ToArray();
    }

    private static string ReadEmbedded()
    {
        using var stream = typeof(ChangelogService).Assembly.GetManifestResourceStream("CHANGELOG.md")
            ?? throw new InvalidOperationException("CHANGELOG.md was not embedded in this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<ChangelogRelease> Parse(string text)
    {
        var releases = new List<ChangelogRelease>();
        string? version = null, date = null;
        string? section = null;
        var highlights = new List<string>();

        void Flush()
        {
            if (version is not null && highlights.Count > 0) releases.Add(new(version, date ?? "", highlights.ToArray()));
            highlights = new List<string>();
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (VersionHeading.Match(line) is { Success: true } versionMatch)
            {
                Flush();
                version = versionMatch.Groups["version"].Value;
                date = versionMatch.Groups["date"].Value;
                section = null;
                continue;
            }
            if (SectionHeading.Match(line) is { Success: true } sectionMatch)
            {
                section = sectionMatch.Groups["section"].Value;
                continue;
            }
            if (version is null || section is null || !PlayerFacingSections.Contains(section)) continue;
            if (BulletLine.Match(line) is { Success: true } bulletMatch) highlights.Add(bulletMatch.Groups["text"].Value);
        }
        Flush();
        return releases;
    }
}
