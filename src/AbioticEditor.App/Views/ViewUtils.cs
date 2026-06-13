using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Shared plumbing for the split-out views: gesture-context lookup, parent-page
/// resolution for alerts, and the standard bulk-edit confirmation dialog.
/// </summary>
public static class ViewUtils
{
    /// <summary>Walks up from a gesture's owning element to find the first BindingContext of T.</summary>
    public static T? FindBoundContext<T>(object? gestureSender) where T : class
    {
        if (gestureSender is not Element start) return null;
        Element? current = start;
        while (current is not null)
        {
            if (current is BindableObject bo && bo.BindingContext is T match) return match;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>The MainViewModel a view inherits via BindingContext (null before binding).</summary>
    public static MainViewModel? Vm(BindableObject view) => view.BindingContext as MainViewModel;

    /// <summary>The page hosting an element (for alerts), falling back to the window's page.</summary>
    public static Page? ParentPage(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is Page page) return page;
        }
        return Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;
    }

    /// <summary>
    /// Yes/no confirm shown as the in-app dialog. The <paramref name="host"/> is no longer
    /// needed (the dialog is app-global) but is kept so existing callers compile unchanged.
    /// </summary>
    public static Task<bool> ConfirmAsync(Element host, string title, string message, string accept, string cancel)
        => DialogViewModel.Current.ConfirmAsync(title, message, accept, cancel);

    /// <summary>Standard wording for staged bulk edits (easy to fat-finger, hard to undo).</summary>
    public static Task<bool> ConfirmBulkAsync(Element host, string what) =>
        ConfirmAsync(host, "Are you sure?",
            $"This will {what}.\n\nThe change stages until you press SAVE (REVERT undoes it before saving).",
            "YES, DO IT", "Cancel");

    /// <summary>
    /// Clearance-override prompt for a single classified (spoiler) item. <paramref name="what"/>
    /// names the thing being revealed, e.g. "this quest flag" or "the Leyak's containment
    /// record". Confirming reveals it permanently for this install.
    /// </summary>
    public static Task<bool> ConfirmRevealAsync(Element host, string what) =>
        ConfirmAsync(host, "OVERRIDE CLEARANCE?",
            $"{what} is sealed above your current clearance because it may spoil content you "
            + "haven't reached yet.\n\nReveal it anyway? It will stay visible from now on.",
            "OVERRIDE", "KEEP SEALED");

    /// <summary>Informational alert shown as the in-app dialog (host kept for call-site compat).</summary>
    public static Task AlertAsync(Element host, string title, string message)
        => DialogViewModel.Current.AlertAsync(title, message);
}
