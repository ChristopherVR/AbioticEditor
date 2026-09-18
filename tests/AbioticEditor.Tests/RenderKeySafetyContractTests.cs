using System.Text.RegularExpressions;

namespace AbioticEditor.Tests;

/// <summary>
/// Guards against the class of crash fixed in round 122: a Blazor <c>@key</c> that repeats on two
/// sibling elements in the same render throws inside <c>RenderTreeDiffBuilder</c> ("More than one
/// sibling of element '...' has the same key value"), which no <c>ErrorBoundary</c> can catch -
/// it kills the whole page circuit. The live TRIGGERS tab hit this for real: several placed
/// trigger volumes can share one game-authored <c>UniqueTriggerID</c>, and the row list keyed
/// straight off that id, so two loaded volumes with the same id produced duplicate <c>@key</c>
/// values.
///
/// <para>The fix has two parts. <see cref="AbioticEditor.Web.Services.RenderKeys"/> (see its own
/// remarks and <see cref="RenderKeysTests"/>) turns any sequence into always-unique keys, and
/// every <c>@key=</c> loop whose id comes from data this app does not fully control (live-agent
/// rows, save-file entries, catalog/game-registry ids) was routed through it - see
/// <c>docs/PROGRESS.md</c>'s Round-122 entry for the full per-site audit. This test is the
/// regression guard for that audit: it scans every <c>.razor</c> file under <c>Components</c> for
/// a BARE <c>@key="loopVariable.Id"</c> / <c>.Key</c> / <c>.Row</c> / <c>.Name</c> expression -
/// exactly the shape that crashed - and fails unless that exact site is explicitly allowlisted
/// below with a comment proving why it cannot collide. A future tab that keys a loop off
/// <c>row.Id</c> directly (instead of a RenderKeys-derived value) fails this test immediately,
/// rather than shipping and crashing a player's page the way TRIGGERS did.</para>
///
/// <para>This intentionally only catches the BARE-property shape (a dotted identifier chain with
/// nothing else in it). A composite key like <c>@($"{row.Id}:{nonce}")</c> or a RenderKeys-derived
/// local (<c>rowRenderKey</c>) does not match the bare-property regex at all, so it is not flagged
/// here - those are exactly the patterns the round-122 fix moved every risky site to.</para>
/// </summary>
public sealed class RenderKeySafetyContractTests
{
    // Property names that read as "the loop item's own id" - the exact shape that crashed.
    // Keep in sync with the task brief's own enumeration (Id/Key/Row/Name); Index/SaveIndex/
    // OwnerId/ApiName and similar do not match here on purpose - see the type doc comment.
    private static readonly string[] RiskyPropertyNames = ["Id", "Key", "Row", "Name"];

    // A bare dotted identifier chain, e.g. "worldBase.Name" or "skill.Definition.SaveIndex" -
    // nothing else (no @(...), no string interpolation, no method calls, no literals).
    private static readonly Regex BareDottedChain = new(
        @"^[A-Za-z_][A-Za-zA-Z0-9_]*(\.[A-Za-z_][A-Za-zA-Z0-9_]*)+$", RegexOptions.Compiled);

    private static readonly Regex KeyAttribute = new(
        "@key=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// Every bare risky <c>@key</c> site that is NOT routed through <c>RenderKeys</c>, each with
    /// the reasoning that was verified by hand when the round-122 audit reached it (see the
    /// matching in-file comment at the same site for the full version). A site here must be
    /// impossible to collide by construction, not merely "hasn't collided yet".
    /// </summary>
    private static readonly HashSet<(string File, string Expression)> Allowlist = new()
    {
        // BaseDetector.Detect assigns "Base 1", "Base 2", ... from a counter it owns itself and
        // increments for every base it creates in that one call - not data read from a save or
        // the live agent, so it cannot repeat within one render.
        ("WorldBasesTab.razor", "worldBase.Name"),

        // BenchUpgradeCatalog.All is a hardcoded C# array of 11 known upgrade modules (compile-
        // time constant catalog data, not read from paks/saves/the live agent at runtime).
        ("WorldBasesTab.razor", "upgrade.Row"),

        // StoryProgressionCatalog.Chapters is a hardcoded C# array of story chapters, each with a
        // distinct literal Row string - compile-time constant catalog data.
        ("WorldStoryTab.razor", "chapter.Row"),

        // pet.LimbHealth is a real IReadOnlyDictionary<string, double>; enumerating a Dictionary
        // can never yield two entries with the same Key by definition.
        ("WorldPetsTab.razor", "limb.Key"),
    };

    [Fact]
    public void No_new_bare_id_key_row_or_name_key_sites_exist_outside_the_allowlist()
    {
        var failures = new List<string>();
        var seenAllowlistEntries = new HashSet<(string File, string Expression)>();

        foreach (var file in UiSource.EnumerateFiles("Components", "*.razor", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            var lineStarts = BuildLineStarts(text);

            foreach (Match match in KeyAttribute.Matches(text))
            {
                var expression = match.Groups[1].Value;
                if (!BareDottedChain.IsMatch(expression)) continue;

                var lastDot = expression.LastIndexOf('.');
                var propertyName = expression[(lastDot + 1)..];
                if (!RiskyPropertyNames.Contains(propertyName, StringComparer.Ordinal)) continue;

                var site = (File: fileName, Expression: expression);
                if (Allowlist.Contains(site))
                {
                    seenAllowlistEntries.Add(site);
                    continue;
                }

                var line = LineNumberFor(lineStarts, match.Index);
                failures.Add($"{fileName}:{line}: @key=\"{expression}\" keys a loop bare off "
                    + $"'.{propertyName}'. If this id comes from a save file, the live agent, or a "
                    + "game-registry catalog, route it through RenderKeys.With(...) instead (see "
                    + "RenderKeys.cs and docs/PROGRESS.md's Round-122 entry). If it is genuinely "
                    + "impossible to collide (a compile-time constant catalog, a real Dictionary "
                    + "key, a counter this app owns), add it to this test's Allowlist with a "
                    + "comment proving why.");
            }
        }

        Assert.True(failures.Count == 0, "Unsafe @key sites found:\n" + string.Join("\n", failures));

        // Keeps the allowlist itself honest: an entry for a site that no longer exists (renamed,
        // rewritten, deleted) would otherwise sit here forever, silently proving nothing.
        var stale = Allowlist.Except(seenAllowlistEntries).ToList();
        Assert.True(stale.Count == 0,
            "Stale RenderKeySafetyContractTests.Allowlist entries no longer found in any .razor file: "
            + string.Join(", ", stale.Select(s => $"{s.File}:{s.Expression}")));
    }

    private static List<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') starts.Add(i + 1);
        }
        return starts;
    }

    private static int LineNumberFor(List<int> lineStarts, int index)
    {
        var line = lineStarts.BinarySearch(index);
        if (line < 0) line = ~line - 1;
        return line + 1;
    }
}
