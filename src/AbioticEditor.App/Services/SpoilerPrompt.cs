namespace AbioticEditor.App.Services;

/// <summary>
/// View-model-callable clearance-override prompt for classified (spoiler) items. Uses the
/// app-global in-app dialog (<see cref="ViewModels.DialogViewModel"/>) so any row view model
/// can offer tap-to-reveal without a per-view code-behind handler or page reference. On
/// confirm the item is revealed permanently via <see cref="SpoilerService.Reveal"/>.
/// </summary>
public static class SpoilerPrompt
{
    /// <summary>
    /// Prompts to reveal one sealed item. <paramref name="what"/> names it in-prompt, e.g.
    /// "This quest flag". Returns true (and records the override) if the user confirmed.
    /// </summary>
    public static async Task<bool> RevealAsync(string what, string key)
    {
        if (SpoilerService.IsRevealed(key)) return true;

        var ok = await ViewModels.DialogViewModel.Current.ConfirmAsync(
            "OVERRIDE CLEARANCE?",
            $"{what} is sealed above your current clearance because it may spoil content you "
            + "haven't reached yet.\n\nReveal it anyway? It will stay visible from now on.",
            "OVERRIDE", "KEEP SEALED");
        if (ok) SpoilerService.Reveal(key);
        return ok;
    }
}
