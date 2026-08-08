using System.Text.RegularExpressions;

namespace AbioticEditor.Tests;

/// <summary>
/// Links and navigation inside the editor must be relative to the page's base address, never
/// rooted at the domain.
/// </summary>
/// <remarks>
/// The desktop host serves the editor from the root of its own local address, so a link written
/// as "/browse" works there and looks perfectly fine. The published browser editor is served
/// from a sub-folder of the docs site, and there the same link resolves to the DOMAIN root -
/// leaving the editor entirely and landing on a "404 not found" page.
///
/// It is worse than an ordinary broken link: it escapes the folder the deep-link recovery script
/// watches, so nothing catches it and the player is simply thrown out of the editor. Nothing in a
/// local run can reveal this, which is why it is asserted here.
///
/// Relative forms ("browse", "./") are correct on BOTH hosts, because each resolves against
/// whatever base address the page declares.
/// </remarks>
public sealed class SubpathNavigationTests
{
    private static IEnumerable<string> EditorComponents()
    {
        foreach (var root in new[] { "AbioticEditor.Web.Shared", "AbioticEditor.Web" })
        {
            var dir = Path.Combine(UiSource.RepositoryRoot, "src", root, "Components");
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories))
            {
                yield return file;
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.razor.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    [Fact]
    public void InAppLinks_AreRelativeToTheBaseAddress()
    {
        // Only in-app routes. "//" is protocol-relative and "/_content", "/_framework" are
        // framework-served paths that genuinely do live at the host root.
        var rooted = new Regex(@"href=""/(?!/|_content|_framework)[a-z][-a-z0-9/]*""", RegexOptions.IgnoreCase);
        var offenders = new List<string>();

        foreach (var file in EditorComponents())
        {
            foreach (Match match in rooted.Matches(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These links start at the domain root, so they leave the editor entirely when it is "
            + "published to a sub-folder and the player gets a 404. Drop the leading slash "
            + $"(href=\"browse\", not href=\"/browse\"):{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void GoingHome_StaysInsideTheEditor()
    {
        var offenders = new List<string>();

        foreach (var file in EditorComponents())
        {
            var text = File.ReadAllText(file);
            // NavigateTo("/...") resolves against the domain root for the same reason.
            foreach (Match match in Regex.Matches(text, @"NavigateTo\(""/[^""]*""").Cast<Match>())
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These navigate to the domain root rather than the editor's own home, which throws the "
            + $"player out of the editor when it is published to a sub-folder. Use \"./\":{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }
}
