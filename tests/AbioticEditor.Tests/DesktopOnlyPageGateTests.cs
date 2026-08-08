namespace AbioticEditor.Tests;

/// <summary>
/// Screens that can only work when the editor can reach paths on the machine must say so in the
/// browser, rather than rendering controls that fail when used.
/// </summary>
/// <remarks>
/// This has now gone wrong twice. Create-world hid its link on the home screen but left the page
/// itself reachable, so the address still opened a wizard that would fail at the first folder it
/// tried to write. The settings editor had no gate at all: in a browser it rendered normally and
/// FIND SETTINGS FILES then failed on an empty path with "Settings files could not be checked",
/// which reads as a broken editor rather than something a browser tab cannot do.
///
/// Hiding the link is never enough - the route stays reachable by typing it, by a bookmark, or by
/// a refresh. The gate has to be on the page.
/// </remarks>
public sealed class DesktopOnlyPageGateTests
{
    [Theory]
    [InlineData("Compare.razor")]
    [InlineData("CreateWorld.razor")]
    [InlineData("IniEditor.razor")]
    public void DesktopOnlyPages_TellBrowserPlayersInsteadOfFailing(string page)
    {
        var source = UiSource.ReadAllText("Components", "Pages", page);

        Assert.True(
            source.Contains("HasLocalPaths", StringComparison.Ordinal),
            $"{page} needs a Workspace.HasLocalPaths gate. It only works when the editor can reach "
            + "paths on the machine, so in a browser it must show the 'needs the desktop editor' "
            + "panel. Hiding the link elsewhere does not help - the route is still reachable.");

        Assert.True(
            source.Contains("Host_NeedsDesktop", StringComparison.Ordinal),
            $"{page} gates on HasLocalPaths but does not show the shared 'needs the desktop editor' "
            + "wording (Host_NeedsDesktopTitle / Host_NeedsDesktopBody), so browser players get a "
            + "blank screen instead of an explanation.");
    }
}
