namespace AbioticEditor.Tests;

/// <summary>
/// The browser (WebAssembly) deployment can never actually reach a running game - see
/// <c>ILiveEditingCapability</c>'s own doc comment - but it used to simply hide the whole
/// feature, which meant a web player never learned the desktop app could do more. This pins the
/// fix: every place the desktop host offers live editing must still show *something* on the
/// browser build, gated on <c>IBrowserHostMarker</c>, explaining that live editing needs the
/// desktop app rather than rendering a broken or working-looking flow.
/// </summary>
public sealed class LiveEditingBrowserHintTests
{
    // ---- ModeSelectDialog: the Choose step's live card --------------------------------------

    [Fact]
    public void ModeSelectDialog_resolves_the_browser_marker_through_the_service_provider()
    {
        var source = SharedSource("Components", "Shared", "ModeSelectDialog.razor");

        Assert.Contains(
            "private bool IsBrowserHost => Services.GetService<IBrowserHostMarker>() is not null;",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeSelectDialog_gates_the_live_card_on_IsBrowserHost_instead_of_hiding_it()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        // The live card must still exist for both hosts (it is never removed from the Choose
        // step), and it must branch on IsBrowserHost rather than on ILiveEditingCapability - the
        // browser never registers that capability at all, so gating on it would just delete the
        // card again instead of replacing it with an explanation.
        Assert.Contains("mode-option mode-option-live", source, StringComparison.Ordinal);
        Assert.Contains("@if (IsBrowserHost)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_browser_live_card_shows_a_desktop_only_hint_and_a_link_to_the_desktop_download()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        Assert.Contains("L.Resource(\"Editing_LiveDesktopOnlyBadge\")", source, StringComparison.Ordinal);
        Assert.Contains("L.Resource(\"Editing_LiveBrowserHint\")", source, StringComparison.Ordinal);
        Assert.Contains("L.Resource(\"ModDisclaimer_DesktopLink\")", source, StringComparison.Ordinal);
        // Same releases link the other browser-only guards (Game Pass block, mod disclaimer)
        // already point at, not a second, possibly-drifting copy of the URL.
        Assert.Contains("href=\"@BrowserGamePassBlockGuard.ReleasesUrl\"", source, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_desktop_live_card_still_offers_the_real_set_up_flow()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        // The non-browser branch must be untouched: still a real button into Step.LiveTarget,
        // not the informational anchor.
        Assert.Contains(
            "@onclick=\"() => _step = Step.LiveTarget\">@L.Resource(\"Editing_ChooseLive\")</button>",
            source, StringComparison.Ordinal);
    }

    // ---- MainLayout: the header entry point ---------------------------------------------------

    [Fact]
    public void MainLayout_offers_an_informational_header_button_on_the_browser_host()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));

        Assert.Contains("_isBrowserHost = Services.GetService<IBrowserHostMarker>() is not null;",
            source, StringComparison.Ordinal);
        // The browser branch must reuse the same OpenModeSelect() the desktop button already
        // uses (so it goes through the one dialog, not a second bespoke screen), and must not be
        // gated behind _liveEditingAvailable, which is always false in the browser build.
        Assert.Contains("else if (_isBrowserHost)", source, StringComparison.Ordinal);
        Assert.Contains(
            "@onclick=\"OpenModeSelect\" title=\"@Languages.Resource(\"Editing_LiveInfoButtonTitle\")\"",
            source, StringComparison.Ordinal);
    }

    // ---- LiveConnect: a deep link to /live must never render a broken page --------------------

    [Fact]
    public void LiveConnect_always_sends_a_visit_with_no_pending_handoff_back_to_the_mode_select_dialog()
    {
        var source = Flatten(SharedSource("Components", "Pages", "LiveConnect.razor"));

        // Whether the host is desktop or browser, a plain/bookmarked visit to /live with nothing
        // to adopt yet must bounce to the one place that question has a home (ModeSelectDialog),
        // rather than rendering its own "waiting" state indefinitely - which is what would let the
        // browser build show a page nothing can ever complete on.
        Assert.Contains("RedirectToModeSelect();", source, StringComparison.Ordinal);
        Assert.Contains("LiveSession.RequestModeSelect();", source, StringComparison.Ordinal);
    }

    // ---- resource keys --------------------------------------------------------------------

    [Fact]
    public void Browser_live_editing_hint_resource_keys_exist_in_AppResources()
    {
        var resources = System.Xml.Linq.XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Descendants("data").Select(node => node.Attribute("name")?.Value)
            .Where(name => name is not null).ToHashSet(StringComparer.Ordinal);

        foreach (var key in new[]
        {
            "Editing_LiveDesktopOnlyBadge",
            "Editing_LiveBrowserHint",
            "Editing_LiveInfoButtonTitle",
            // Reused rather than duplicated - see BrowserModDisclaimerGate/BrowserGamePassBlockGuard.
            "ModDisclaimer_DesktopLink",
        })
        {
            Assert.Contains(key, resources);
        }
    }

    /// <summary>Whitespace-insensitive source text, so a re-wrap does not fail these.</summary>
    private static string Flatten(string source)
        => string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SharedSource(params string[] parts) => UiSource.ReadAllText(parts);
}
