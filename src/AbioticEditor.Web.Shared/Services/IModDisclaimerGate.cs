namespace AbioticEditor.Web.Services;

/// <summary>
/// Gates opening a save with a warning that this host cannot see the player's mods, before the
/// editor loads it. Registered differently per host - see <see cref="BrowserModDisclaimerGate"/> -
/// the same per-host-behaviour idiom as <c>IGamePassSafetyGuard</c>. The desktop host reads the
/// installed game (and, through it, whatever mods it is running) directly, so it has nothing to
/// warn about and always proceeds straight through with no dialog.
/// </summary>
public interface IModDisclaimerGate
{
    /// <summary>
    /// Runs <paramref name="proceed"/>, first showing the mod disclaimer when this host requires
    /// one. <paramref name="declined"/> runs instead if the player backs out of the warning, so
    /// nothing opens.
    /// </summary>
    /// <remarks>
    /// Like <c>UnsavedChangesGuard.ConfirmAsync</c>, a host that shows a dialog returns as soon as
    /// the question is on screen; the open happens later, through the dialog's confirm. Callers
    /// must not assume a save has opened once this has been awaited, and must use
    /// <paramref name="declined"/> rather than the returned task to know an attempt is over.
    /// </remarks>
    Task ShowAsync(Func<Task> proceed, Func<Task>? declined = null);
}

/// <summary>The desktop host's registration: nothing to warn about, so it always proceeds.</summary>
public sealed class DesktopModDisclaimerGate : IModDisclaimerGate
{
    public Task ShowAsync(Func<Task> proceed, Func<Task>? declined = null)
    {
        ArgumentNullException.ThrowIfNull(proceed);
        return proceed();
    }
}
