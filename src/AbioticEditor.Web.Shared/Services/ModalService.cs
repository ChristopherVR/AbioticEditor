using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>Coordinates circuit-local application dialogs without a platform UI dependency.</summary>
public sealed class ModalService
{
    public ModalRequest? Current { get; private set; }
    public event Action? Changed;

    // Set only while ModalHost is actively awaiting the current dialog's own OnConfirm - the
    // window in which that callback is allowed to chain straight into a follow-up question
    // (repairing a save and then being refused again; see ModalHost.ConfirmAsync). A Show()
    // outside that window means whatever is currently up got superseded by something
    // unrelated - a caller that navigated away rather than answering it, say - not answered by
    // the player at all.
    private bool _resolving;

    public void Show(ModalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Replacing an unanswered dialog used to just drop its OnConfirm/OnCancel on the floor.
        // Whatever flag it was guarding (WorkspaceShell's "a save switch is in flight", say)
        // then never got reset, wedging that action's buttons dead for the rest of the session
        // with no visible sign why. Treat the abandoned dialog as declined, exactly like the
        // player pressing Cancel on it, so its OnCancel still runs and releases that flag.
        var abandoned = _resolving ? null : Current;
        Current = request;
        Changed?.Invoke();
        if (abandoned?.OnCancel is { } declined) _ = declined();
    }

    public void Close()
    {
        if (Current is null) return;
        Current = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Marks the current dialog's OnConfirm as running, for the duration of the returned scope -
    /// see <see cref="Show"/>. Only <c>ModalHost.ConfirmAsync</c> should call this.
    /// </summary>
    internal IDisposable BeginResolving()
    {
        _resolving = true;
        return new ResolvingScope(this);
    }

    private sealed class ResolvingScope(ModalService owner) : IDisposable
    {
        public void Dispose() => owner._resolving = false;
    }
}

/// <summary>
/// A dialog request. <paramref name="OnCancel"/> runs when the dialog is dismissed without
/// confirming (cancel button, backdrop, Escape) - for prompts that must undo staged state
/// on decline. It never runs after a successful confirm.
/// </summary>
public sealed record ModalRequest(string Title, RenderFragment Body, string? ConfirmText = null,
    Func<Task>? OnConfirm = null, string CancelText = "Cancel", bool IsDestructive = false,
    bool CloseOnBackdrop = true, Func<Task>? OnCancel = null);
