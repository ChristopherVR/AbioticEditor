using AbioticEditor.App.ViewModels;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using CommunityToolkit.Maui.Storage;

namespace AbioticEditor.App.Services;

/// <summary>
/// The one place that lets the user point the editor at their Abiotic Factor install: pick a
/// folder, validate it resolves to the game's paks, and persist it. Shared by Settings &gt; Game
/// Data, the in-editor "game data not detected" banner, and the first-run prompt, so all three
/// behave identically. Reloading the catalogs and refreshing the UI is the caller's job
/// (<see cref="MainViewModel.ReloadGameDataAsync"/>), since only the view-model owns the palette.
/// </summary>
public static class GameDataPrompt
{
    /// <summary>
    /// Opens a folder picker and, if the chosen folder resolves to the game's paks (install root,
    /// the inner AbioticFactor folder, or the Paks folder itself), persists it as the custom
    /// install path. Returns true when a valid folder was saved (the caller should reload game
    /// data), false when the picker was dismissed or the folder had no game data (an explanatory
    /// dialog is shown in the latter case).
    /// </summary>
    public static async Task<bool> PickAndSaveFolderAsync()
    {
        FolderPickerResult result;
        try
        {
            result = await FolderPicker.PickAsync(CancellationToken.None);
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            return false;
        }

        if (!result.IsSuccessful || result.Folder is null)
        {
            return false; // dismissed
        }

        var picked = result.Folder.Path;
        var paks = AfInstallLocator.ResolvePaksDirectory(picked);
        if (paks is null)
        {
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["GameDataPrompt_NoGameDataTitle"],
                LocalizationResourceManager.Instance.Format("GameDataPrompt_NoGameDataMessage", picked));
            return false;
        }

        GameDataServices.CustomInstallPath = picked;
        EditorLog.Info("GameData", $"Game install folder set to {picked} (paks: {paks})");
        return true;
    }

    /// <summary>Picker filter for usmap import (see MainViewModel.ImportMappingsCommand for the rationale).</summary>
    private static readonly FilePickerFileType UsmapFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".usmap"],
        [DevicePlatform.Android] = ["application/octet-stream", "*/*"],
        [DevicePlatform.iOS] = ["public.data", "public.item"],
        [DevicePlatform.MacCatalyst] = ["usmap", "public.data"],
    });

    /// <summary>
    /// Opens a file picker for a <c>Mappings.usmap</c> and installs it as the user override.
    /// Returns true when one was installed (the caller should reload), false when dismissed or
    /// the file was rejected (validation shows its own dialog).
    /// </summary>
    public static async Task<bool> PickAndImportUsmapAsync()
    {
        FileResult? pick;
        try
        {
            pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = LocalizationResourceManager.Instance["GameDataPrompt_PickUsmapTitle"],
                FileTypes = UsmapFileType,
            });
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            return false;
        }
        if (pick is null) return false;

        try
        {
            var installed = GameAssetProvider.InstallUserMappings(pick.FullPath);
            EditorLog.Info("GameData", $"Imported mappings: {pick.FullPath} -> {installed}");
            return true;
        }
        catch (Exception ex)
        {
            await DialogViewModel.Current.AlertAsync(
                LocalizationResourceManager.Instance["GameDataPrompt_ImportFailedTitle"], ex.Message);
            return false;
        }
    }

    /// <summary>
    /// The right fix for the current <see cref="GameDataServices.Status"/>: importing a usmap when
    /// the game was found but its data file is missing, otherwise locating the install folder.
    /// Returns true when something changed (the caller should reload).
    /// </summary>
    public static Task<bool> FixAsync()
        => GameDataServices.Status == GameDataStatus.MappingsMissing
            ? PickAndImportUsmapAsync()
            : PickAndSaveFolderAsync();

    /// <summary>
    /// The toolkit reports a dismissed folder dialog as a failure carrying an exception; closing
    /// the picker without choosing is a normal outcome, not an error.
    /// </summary>
    private static bool IsCancellation(Exception ex)
        => ex is OperationCanceledException
            || ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);
}
