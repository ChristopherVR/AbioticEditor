namespace AbioticEditor.Tests;

/// <summary>
/// Round 115: when a Blazor Server circuit dies (a render exception the way the bench-upgrade
/// crash this round investigated did, the running game's window losing its connection, or the
/// process restarting under it), <c>blazor.web.js</c> only ever TOGGLES CSS classes
/// (<c>components-reconnect-show</c>/<c>-failed</c>/<c>-rejected</c>) on an element with the id
/// <c>components-reconnect-modal</c>; it never creates that element or supplies any styling for
/// it. Without both defined in the page, a dead circuit looked exactly like a frozen app with no
/// indication anything had gone wrong and no way to recover short of knowing to close and reopen
/// the whole window - see the crash report's "can't click any button anymore".
/// </summary>
/// <remarks>
/// Asserted against the source text, matching the rest of this suite's UI-parity/contract tests -
/// there is no way to actually kill a live SignalR circuit from a unit test.
/// </remarks>
public sealed class ReconnectionUiWiringTests
{
    [Fact]
    public void AppRazor_defines_the_reconnect_modal_element_blazor_web_js_toggles()
    {
        var source = UiSource.ReadAllText("Components", "App.razor");

        Assert.Contains("id=\"components-reconnect-modal\"", source, StringComparison.Ordinal);

        // A reload is the one recovery a dead circuit has (a brand-new circuit needs a fresh page
        // load) - the button must actually be wired to do that, not just be present.
        Assert.Contains("location.reload()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRazor_styles_every_reconnect_state_blazor_web_js_can_set()
    {
        var source = UiSource.ReadAllText("Components", "App.razor");

        // These three exact class names are documented, framework-owned behaviour (Microsoft's
        // "Customize the reconnection UI" guidance) - blazor.web.js sets one of them on the modal
        // element above depending on whether it is still retrying, has given up, or the page is
        // out of date. Missing any one of them means that state renders with no visible styling
        // at all, i.e. the app looks frozen again for that specific case.
        Assert.Contains("components-reconnect-show", source, StringComparison.Ordinal);
        Assert.Contains("components-reconnect-failed", source, StringComparison.Ordinal);
        Assert.Contains("components-reconnect-rejected", source, StringComparison.Ordinal);
    }
}
