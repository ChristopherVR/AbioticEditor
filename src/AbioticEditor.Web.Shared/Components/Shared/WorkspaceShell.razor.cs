using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Ini;
using AbioticEditor.Web.Services;
// Aliased rather than imported: the Windows build turns on Windows Forms, whose global usings
// bring in a MouseEventArgs of its own, and an unqualified name is then ambiguous.
using MouseEventArgs = Microsoft.AspNetCore.Components.Web.MouseEventArgs;

#pragma warning disable CA1716 // Namespace matches the existing Razor component folder.
namespace AbioticEditor.Web.Components.Shared;
#pragma warning restore CA1716

public partial class WorkspaceShell
{
    // ---------- right-click menu on a save row ----------

    private WorkspaceSave? _saveMenuFor;
    private string _saveMenuStyle = string.Empty;

    private void OpenSaveMenu(WorkspaceSave save, MouseEventArgs args)
    {
        _saveMenuFor = save;
        // Positioned against the viewport, which is what the click coordinates are relative to.
        _saveMenuStyle = FormattableString.Invariant($"left:{args.ClientX}px; top:{args.ClientY}px;");
    }

    private void CloseSaveMenu() => _saveMenuFor = null;

    /// <summary>
    /// Hands this one save back to the player, under its own name.
    /// </summary>
    /// <remarks>
    /// The whole-world zip is the safe default, because editing one save can change others. This
    /// is for when the player knows they only want the one file - and in a browser it is the only
    /// way to get a single save out without taking everything.
    ///
    /// It exports what is ON DISK, so an edit still staged in an open editor is not included.
    /// Saying so is better than quietly exporting the old bytes.
    /// </remarks>
    private async Task ExportSelectedSaveAsync()
    {
        var save = _saveMenuFor;
        CloseSaveMenu();
        if (save is null) return;

        try
        {
            await Export.ExportSaveAsync(save).ConfigureAwait(false);
            Toasts.Show(L.Resource("FileSidebar_ExportedOneSave", save.Name), ToastKind.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or Microsoft.JSInterop.JSException)
        {
            EditorLog.Error("Shell", $"Could not export '{save.Path}'", exception);
            Toasts.Show(L.Resource("FileSidebar_ExportOneSaveFailed", save.Name), ToastKind.Error);
        }
    }

    /// <summary>
    /// What "show this save" should actually open.
    ///
    /// <para>Normally the save file itself. For a Game Pass world it is the Xbox container
    /// folder: the editor is working on a temp copy it unpacked, so revealing the save's own
    /// path would open a throwaway directory that is deleted when the editor closes, and none
    /// of it is where the game keeps the world.</para>
    /// </summary>
    public static string ResolveRevealTarget(string? gamePassContainerFolder, string savePath)
        => string.IsNullOrWhiteSpace(gamePassContainerFolder) ? savePath : gamePassContainerFolder;

    /// <summary>Each desktop calls its file manager something different; use its own name.</summary>
    private string RevealLabel => L.Resource(
        OperatingSystem.IsWindows() ? "FileSidebar_ShowInExplorer"
        : OperatingSystem.IsMacOS() ? "FileSidebar_RevealInFinder"
        : "FileSidebar_ShowInFileManager");

    /// <summary>
    /// Shows the save in Explorer or Finder.
    ///
    /// <para>Game Pass worlds are the special case: the editor works on a temp copy it unpacked
    /// from the Xbox container, and opening that folder would show a throwaway directory that
    /// disappears when the editor closes. Those reveal the real container folder instead, which
    /// is the thing anyone would actually want to back up.</para>
    /// </summary>
    private async Task RevealSelectedSaveAsync()
    {
        var save = _saveMenuFor;
        CloseSaveMenu();
        if (save is null) return;

        var target = ResolveRevealTarget(Workspace.Current?.GamePass?.WgsFolder, save.Path);
        try
        {
            await External.RevealPathAsync(target).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            EditorLog.Error("Shell", $"Could not reveal '{target}'", exception);
            Toasts.Show(L.Resource("FileSidebar_RevealFailed"), ToastKind.Error);
        }
    }

    private static string ConfigChipResourceKey(AbioticIniKind kind) => kind switch
    {
        AbioticIniKind.ServerAdmin => "Main_ChipAdmin",
        AbioticIniKind.SandboxSettings => "Main_ChipSandbox",
        AbioticIniKind.ClientConfig => "Main_ChipClient",
        _ => "Main_ChipIni",
    };

    private static string ConfigDetail(AbioticIniFile config) => config.Kind == AbioticIniKind.SandboxSettings
        ? Path.GetFileName(Path.GetDirectoryName(config.FullPath)) ?? string.Empty
        : string.Empty;
}
