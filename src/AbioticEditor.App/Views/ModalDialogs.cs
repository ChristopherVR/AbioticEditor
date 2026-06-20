namespace AbioticEditor.App.Views;

/// <summary>
/// Alert/confirm helpers for the code-built modal sheets (Settings, Compare, plugin pages).
///
/// The app-wide <see cref="ViewModels.DialogViewModel"/> overlay is rendered in the main page
/// tree, so it appears invisible behind a pushed modal page. These helpers route through the
/// native <c>DisplayAlertAsync</c> instead, which is drawn on top of the active modal and so
/// stays visible (e.g. the "mods reloaded" notice over the settings sheet).
/// </summary>
internal static class ModalDialogs
{
    /// <summary>Informational alert shown on top of the modal page.</summary>
    public static Task AlertAsync(this Page page, string title, string message)
        => page.DisplayAlertAsync(title, message, Services.LocalizationResourceManager.Instance["Common_Ok"]);

    /// <summary>Yes/no confirm shown on top of the modal page.</summary>
    public static Task<bool> ConfirmAsync(this Page page, string title, string message, string accept, string cancel)
        => page.DisplayAlertAsync(title, message, accept, cancel);
}
