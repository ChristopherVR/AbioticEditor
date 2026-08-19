using AbioticEditor.Core.GamePass;
using AbioticEditor.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The browser build's answer to <see cref="GamePassSafetyGuard"/>: Game Pass saves live behind
/// the File System Access API, and even where that API is present the editor cannot reliably hold
/// a save open against Xbox's own cloud sync from inside a tab the way it can from the desktop
/// host. Rather than let a player discover that the hard way (a save that silently fails to write,
/// or reopens the cloud-sync warning every session with no way to actually keep it saved), the
/// browser build declines the open outright and points at the desktop download instead.
/// </summary>
public sealed class BrowserGamePassBlockGuard(ModalService modals, HostLanguageService language, IExternalNavigationService externalNavigation)
    : IGamePassSafetyGuard
{
    public const string ReleasesUrl = "https://github.com/ChristopherVR/AbioticEditor/releases";

    public Task OpenAsync(string? folder, Func<Task> open, Func<Task>? declined = null)
    {
        ArgumentNullException.ThrowIfNull(open);

        if (!IsGamePassFolder(folder))
        {
            return open();
        }

        modals.Show(new ModalRequest(
            language.Resource("Main_GpBrowserUnsupportedTitle"),
            Message(language.Resource("Main_GpBrowserUnsupportedMessage")),
            ConfirmText: language.Resource("Main_GpBrowserUnsupportedOpenReleases"),
            OnConfirm: async () =>
            {
                await externalNavigation.OpenUrlAsync(new Uri(ReleasesUrl)).ConfigureAwait(false);
                if (declined is not null) await declined().ConfigureAwait(false);
            },
            CancelText: language.Resource("Common_Close"),
            OnCancel: declined));
        return Task.CompletedTask;
    }

    private static bool IsGamePassFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try { return GamePassSaveSet.IsGamePassFolder(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static RenderFragment Message(string text) => builder =>
    {
        var sequence = 0;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            builder.OpenElement(sequence++, "p");
            builder.AddContent(sequence++, line);
            builder.CloseElement();
        }
    };
}
