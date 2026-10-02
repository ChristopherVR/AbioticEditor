using Photino.NET;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Native file and folder pickers shown by the editor's own window (Photino), owned by it so they
/// open in front of it. Used on Windows whenever the desktop window is open; elsewhere, or without
/// one (headless runs), the host uses the per-platform commands in <see cref="DesktopHostCommandFactory"/>. This
/// replaced a Windows Forms picker, which pulled Windows Forms and the Windows SDK projection
/// (about 45 MB) into the Windows package for two dialogs.
/// </summary>
internal static class WindowDesktopPicker
{
    public static Task<string?> PickFolderAsync(PhotinoWindow window, string? title)
        => OnWindowThreadAsync(window, () => window.ShowOpenFolder(title ?? "Choose save folder", null, false) is [var path, ..] ? path : null);

    public static Task<string[]?> PickFilesAsync(PhotinoWindow window, string? title, bool allowMultiple, IReadOnlyList<AbioticEditor.Ui.FileTypeFilter> fileTypes)
        => OnWindowThreadAsync(window, () => window.ShowOpenFile(title ?? "Choose file", null, allowMultiple, BuildFilters(fileTypes)));

    internal static (string Name, string[] Extensions)[] BuildFilters(IReadOnlyList<AbioticEditor.Ui.FileTypeFilter> fileTypes)
        => fileTypes.Count == 0
            ? [("All files", ["*.*"])]
            : fileTypes.Select(type => (type.Name, type.Extensions.Select(extension => "*" + (extension.StartsWith('.') ? extension : "." + extension)).ToArray())).ToArray();

    // The dialogs belong to the window's own thread; Invoke runs them there and returns when the
    // player closes the dialog. A pool thread waits so no request thread is held.
    private static Task<T?> OnWindowThreadAsync<T>(PhotinoWindow window, Func<T?> show) => Task.Run(() =>
    {
        T? result = default;
        window.Invoke(() => result = show());
        return result;
    });
}
