using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Asks before an action would throw away edits the player has staged but not saved.
/// </summary>
/// <remarks>
/// <para>Editing here is deliberately staged: changes pile up in the open session and only reach
/// the file on SAVE. The cost of that is that anything replacing the open session - picking
/// another save, opening another world - silently discards them. There was no warning at all,
/// so a player who tabbed between two characters lost whatever they had just done to the first
/// one and had no way to tell it had happened.</para>
///
/// <para>Two buttons, not three. Offering "save first" would mean writing the player's files as
/// a side effect of a click that was not a save, which is exactly the kind of surprise the
/// staged model exists to avoid. Staying put leaves everything intact and SAVE is right there.</para>
/// </remarks>
public sealed class UnsavedChangesGuard(
    SaveWorkspaceSessionService workspace, ModalService modals, HostLanguageService language)
{
    /// <summary>
    /// Runs <paramref name="proceed"/> straight away when nothing is staged, and otherwise asks
    /// first, running it only if the player says to carry on. <paramref name="declined"/> runs
    /// instead when they choose to stay put.
    /// </summary>
    /// <remarks>
    /// <para>Returns as soon as the question is on screen, because the dialog is not modal to the
    /// caller - the answer arrives later through <paramref name="proceed"/>. Callers must
    /// therefore not do anything after awaiting this that assumes the action has happened.</para>
    ///
    /// <para>That is what <paramref name="declined"/> is for. A screen that disables its buttons
    /// while an action is under way cannot tell from the returned task whether the action ever
    /// started, so without a way to hear "no" it would either leave those buttons dead for the
    /// rest of the session or re-enable them while the question was still on screen. Exactly one
    /// of the two callbacks runs, so a refusal ends the same way a completed action does.</para>
    /// </remarks>
    public Task ConfirmAsync(Func<Task> proceed, Func<Task>? declined = null)
    {
        ArgumentNullException.ThrowIfNull(proceed);
        if (!workspace.HasStagedEdits) return proceed();

        modals.Show(new ModalRequest(
            language.Resource("Unsaved_LeaveTitle"),
            Message(language.Resource("Unsaved_LeaveMessage", OpenSaveName())),
            ConfirmText: language.Resource("Unsaved_LeaveConfirm"),
            OnConfirm: proceed,
            CancelText: language.Resource("Unsaved_LeaveCancel"),
            IsDestructive: true,
            OnCancel: declined));
        return Task.CompletedTask;
    }

    /// <summary>The name of the save holding the staged edits, for the question's wording.</summary>
    private string OpenSaveName()
        => workspace.Current?.SelectedSave?.Name is { Length: > 0 } name
            ? name
            : language.Resource("Unsaved_ThisSave");

    private static RenderFragment Message(string text) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, text);
        builder.CloseElement();
    };
}
