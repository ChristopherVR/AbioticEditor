using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>Coordinates circuit-local application dialogs without a platform UI dependency.</summary>
public sealed class ModalService
{
    public ModalRequest? Current { get; private set; }
    public event Action? Changed;

    public void Show(ModalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Current = request;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (Current is null) return;
        Current = null;
        Changed?.Invoke();
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
