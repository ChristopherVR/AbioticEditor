using AbioticEditor.App.ViewModels;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using CommunityToolkit.Maui.Alerts;

namespace AbioticEditor.App.Services;

/// <summary>
/// The GUI implementation of <see cref="IHostUi"/> - the bridge plugins (including JavaScript
/// ones via <c>abiotic.ui</c>) use to drive the app: dialogs, toasts, running save operations,
/// reloading, and navigation. Every call is marshalled onto the UI thread, so plugins can
/// invoke it from any thread. Installed once at startup via
/// <see cref="PluginHostEnvironment.HostUi"/>.
/// </summary>
internal sealed class AppHostUi : IHostUi
{
    private readonly MainViewModel _vm;
    private readonly Func<Task> _rebuildHost;

    public AppHostUi(MainViewModel vm, Func<Task> rebuildHost)
    {
        _vm = vm;
        _rebuildHost = rebuildHost;
    }

    public string? OpenSavePath => _vm.SelectedSave?.FullPath;

    public Task ShowAlertAsync(string title, string message)
        => OnUiAsync(async () =>
        {
            if (CurrentPage is { } page)
            {
                await page.DisplayAlertAsync(title, message, "OK");
            }
        });

    public Task<bool> ConfirmAsync(string title, string message)
        => OnUiAsync(async () => CurrentPage is { } page && await page.DisplayAlertAsync(title, message, "Yes", "No"));

    public Task ToastAsync(string message)
        => OnUiAsync(async () => await Toast.Make(message).Show());

    public Task<bool> RunSaveOperationAsync(string operationId, IReadOnlyDictionary<string, string>? parameters = null)
        => OnUiAsync(async () =>
        {
            var path = _vm.SelectedSave?.FullPath;
            if (path is null)
            {
                return false;
            }
            var match = PluginService.SaveOperations
                .FirstOrDefault(c => string.Equals(c.Value.Id, operationId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                EditorLog.Warn("Plugins", $"abiotic.ui.runSaveOperation: no operation '{operationId}'.");
                return false;
            }

            var outcome = await PluginService.RunOperationAsync(match, path, parameters, dryRun: false);
            if (outcome.Wrote)
            {
                await _vm.ReloadSelectedSaveAsync();
            }
            return outcome.Wrote;
        });

    public Task ReloadOpenSaveAsync() => OnUiAsync(() => _vm.ReloadSelectedSaveAsync());

    public Task OpenSettingsAsync()
        => OnUiAsync(async () =>
        {
            if (CurrentPage is { } page)
            {
                await page.Navigation.PushModalAsync(new SettingsPage(_vm, _rebuildHost));
            }
        });

    public Task OpenPluginsPanelAsync()
        => OnUiAsync(async () =>
        {
            if (CurrentPage is { } page)
            {
                await page.Navigation.PushModalAsync(new PluginsPage(_vm, _rebuildHost));
            }
        });

    private static Page? CurrentPage =>
        Application.Current?.Windows is { Count: > 0 } windows ? windows[0].Page : null;

    private static Task OnUiAsync(Func<Task> action)
        => MainThread.InvokeOnMainThreadAsync(action);

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action)
        => MainThread.InvokeOnMainThreadAsync(action);
}
