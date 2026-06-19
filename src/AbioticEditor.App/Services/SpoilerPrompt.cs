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
            LocalizationResourceManager.Instance["Spoiler_OverrideTitle"],
            LocalizationResourceManager.Instance.Format("Spoiler_OverrideMessage", what),
            LocalizationResourceManager.Instance["Spoiler_OverrideAccept"],
            LocalizationResourceManager.Instance["Spoiler_OverrideKeepSealed"]);
        if (ok) SpoilerService.Reveal(key);
        return ok;
    }
}
