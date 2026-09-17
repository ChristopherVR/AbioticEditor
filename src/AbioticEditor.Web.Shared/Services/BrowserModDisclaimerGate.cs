using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The browser build's mod-content warning, shown before every save open (not just once per
/// browser - there is no "don't show again"). The web app can never mount the installed game's
/// paks the way the desktop host's <c>GameAssetProvider</c> does, so it has no way to tell whether
/// a save was played with mods; editing modded content here can leave it in a state the game
/// itself would never produce.
/// </summary>
public sealed class BrowserModDisclaimerGate(ModalService modals, HostLanguageService language)
    : IModDisclaimerGate
{
    /// <summary>Same releases page the browser's Game Pass block guard points at - see
    /// <see cref="BrowserGamePassBlockGuard.ReleasesUrl"/>.</summary>
    public const string ReleasesUrl = BrowserGamePassBlockGuard.ReleasesUrl;

    public Task ShowAsync(Func<Task> proceed, Func<Task>? declined = null)
    {
        ArgumentNullException.ThrowIfNull(proceed);

        modals.Show(new ModalRequest(
            language.Resource("ModDisclaimer_Title"),
            Message(language.Resource("ModDisclaimer_Message"), language.Resource("ModDisclaimer_DesktopLink")),
            ConfirmText: language.Resource("ModDisclaimer_Continue"),
            OnConfirm: proceed,
            CancelText: language.Resource("Common_Cancel"),
            OnCancel: declined));
        return Task.CompletedTask;
    }

    private static RenderFragment Message(string text, string linkText) => builder =>
    {
        var sequence = 0;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            builder.OpenElement(sequence++, "p");
            builder.AddContent(sequence++, line);
            builder.CloseElement();
        }

        // A plain link, not IExternalNavigationService: this dialog only ever exists on the
        // browser host, where opening a URL is exactly what a normal target="_blank" anchor does
        // on its own, with no JS interop required.
        builder.OpenElement(sequence++, "p");
        builder.OpenElement(sequence++, "a");
        builder.AddAttribute(sequence++, "href", ReleasesUrl);
        builder.AddAttribute(sequence++, "target", "_blank");
        builder.AddAttribute(sequence++, "rel", "noopener noreferrer");
        builder.AddContent(sequence++, linkText);
        builder.CloseElement();
        builder.CloseElement();
    };
}
