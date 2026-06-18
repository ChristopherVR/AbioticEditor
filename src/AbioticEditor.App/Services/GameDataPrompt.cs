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
            await DialogViewModel.Current.AlertAsync("No game data there",
                $"Couldn't find Abiotic Factor's pak files under:\n{picked}\n\n"
                    + "Pick the game's install folder (the one containing the AbioticFactor folder), "
                    + "its AbioticFactor subfolder, or the Content\\Paks folder.");
            return false;
        }

        GameDataServices.CustomInstallPath = picked;
        EditorLog.Info("GameData", $"Game install folder set to {picked} (paks: {paks})");
        return true;
    }

    /// <summary>
    /// The toolkit reports a dismissed folder dialog as a failure carrying an exception; closing
    /// the picker without choosing is a normal outcome, not an error.
    /// </summary>
    private static bool IsCancellation(Exception ex)
        => ex is OperationCanceledException
            || ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);
}
