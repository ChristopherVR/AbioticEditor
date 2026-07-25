#if WINDOWS
using System.Windows.Forms;

namespace AbioticEditor.Web.Services;

/// <summary>
/// In-process native Windows file/folder pickers using <c>System.Windows.Forms</c>, replacing
/// the previous approach of shelling out to a hidden PowerShell process to do the exact same
/// thing. WinForms dialogs require an STA thread, which an ASP.NET Core request never runs on,
/// so each call spins up a dedicated STA thread, shows the dialog, and marshals the result back.
/// </summary>
internal static class WindowsDesktopPicker
{
    public static Task<string?> PickFolderAsync(string? title) => RunOnStaThreadAsync(owner =>
    {
        using var dialog = new FolderBrowserDialog { Description = title ?? "Choose save folder", UseDescriptionForTitle = true };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    });

    public static async Task<IReadOnlyList<string>> PickFilesAsync(string? title, bool allowMultiple, IReadOnlyList<AbioticEditor.Ui.FileTypeFilter> fileTypes)
        => await RunOnStaThreadAsync(owner =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = title ?? "Choose file",
                Multiselect = allowMultiple,
                Filter = BuildFilter(fileTypes),
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? (IReadOnlyList<string>)dialog.FileNames : Array.Empty<string>();
        }) ?? Array.Empty<string>();

    internal static string BuildFilter(IReadOnlyList<AbioticEditor.Ui.FileTypeFilter> fileTypes)
    {
        if (fileTypes.Count == 0) return "All files (*.*)|*.*";
        var parts = fileTypes.Select(type =>
        {
            var patterns = string.Join(';', type.Extensions.Select(extension => "*" + (extension.StartsWith('.') ? extension : "." + extension)));
            return $"{type.Name} ({patterns})|{patterns}";
        });
        return string.Join('|', parts);
    }

    // A picker launched from the desktop web view otherwise has no native owner and can appear
    // behind the editor, which looks exactly like a dead button. The invisible, topmost owner
    // keeps the dialog in front without adding taskbar chrome.
    private static Task<T?> RunOnStaThreadAsync<T>(Func<Form, T?> action)
    {
        var completion = new TaskCompletionSource<T?>();
        var thread = new Thread(() =>
        {
            using var owner = new Form
            {
                ShowInTaskbar = false,
                TopMost = true,
                Opacity = 0,
                Width = 1,
                Height = 1,
                StartPosition = FormStartPosition.CenterScreen,
            };
            try
            {
                owner.Show();
                completion.SetResult(action(owner));
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { owner.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task;
    }
}
#endif
