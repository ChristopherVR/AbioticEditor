using System.Text.RegularExpressions;

namespace AbioticEditor.Tests;

/// <summary>
/// The world-summary clock editors (world day, time of day, day discovered) must tell the
/// workspace they changed something, not just the session.
/// </summary>
/// <remarks>
/// Found by driving the browser build end to end: typing a new world day staged the edit and the
/// panel even read "Unsaved changes", but SAVE and REVERT stayed greyed out forever, so the change
/// could not be written at all. The session knew; the toolbar that watches the workspace did not.
/// It affected the desktop host too, since both render this same screen.
///
/// Asserted against the source text rather than a rendered component because that is how the rest
/// of the UI parity tests in this suite work, and it pins the exact thing that was missing.
/// </remarks>
public sealed class WorldClockEditorWiringTests
{
    [Theory]
    [InlineData("SetWorldDay")]
    [InlineData("SetWorldTime")]
    [InlineData("SetDayDiscovered")]
    public void ClockHandlers_TellTheWorkspaceTheyEdited(string handler)
    {
        var source = UiSource.ReadAllText("Components", "Pages", "SaveEditorSurface.razor");

        // The handler body: from its signature to the closing brace of the method.
        var match = Regex.Match(
            source,
            @"private\s+void\s+" + Regex.Escape(handler) + @"\s*\([^)]*\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"Could not find the body of {handler} in SaveEditorSurface.razor.");

        var body = match.Groups["body"].Value;
        Assert.True(
            body.Contains("NotifyEdited", StringComparison.Ordinal),
            $"{handler} stages an edit but never calls Workspace.NotifyEdited(), so SAVE and REVERT "
            + "stay disabled and the change can never be written.");
    }
}
