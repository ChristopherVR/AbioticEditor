using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The warning shown every time a base move, export or import is opened (offline and live): moving a
/// base is experimental and can cause problems in a world. The tool opens only after the player
/// confirms; cancelling leaves everything as it was.
/// </summary>
public sealed class BaseMoveWarning(ModalService modals, HostLanguageService language)
{
    /// <summary>Asks first, then runs <paramref name="proceed"/> when the player says to go on.</summary>
    /// <param name="live">True when the game is running (live changes happen at once, with no SAVE step or backup).</param>
    public Task ShowAsync(Func<Task> proceed, bool live = false)
    {
        ArgumentNullException.ThrowIfNull(proceed);
        modals.Show(new ModalRequest(
            language.Resource("BaseMoveWarning_Title"),
            Body(language.Resource("BaseMoveWarning_Message"), language.Resource(live ? "BaseMoveWarning_Live" : "BaseMoveWarning_Offline")),
            ConfirmText: language.Resource("BaseMoveWarning_Continue"),
            OnConfirm: proceed,
            CancelText: language.Resource("Common_Cancel"),
            CloseOnBackdrop: false));
        return Task.CompletedTask;
    }

    private static RenderFragment Body(string message, string how) => builder =>
    {
        var sequence = 0;
        builder.OpenElement(sequence++, "p");
        builder.AddAttribute(sequence++, "data-base-move", "warning");
        builder.AddContent(sequence++, message);
        builder.CloseElement();
        builder.OpenElement(sequence++, "p");
        builder.AddContent(sequence++, how);
        builder.CloseElement();
    };
}
