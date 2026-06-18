using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;

namespace AbioticEditor.App;

/// <summary>
/// Host page: wires the view-model, the responsive pane controller and page-level
/// services (settings modal, folder drag-and-drop). All editor UI lives in the
/// Views/ ContentViews; all slot/save logic lives in the view-models.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly ResponsivePaneController _panes;

    public MainPage()
    {
        InitializeComponent();
        // Shared instance: a theme switch rebuilds this page on the same VM, so the
        // loaded save survives the swap. Startup work only runs the first time it loads.
        var firstLoad = !App.SharedViewModel.HasStartedUp;
        _vm = App.SharedViewModel;
        BindingContext = _vm;

        _panes = new ResponsivePaneController(
            this, _vm, HeaderBar,
            FileSidebar, SlotSidebar,
            FileSidebarInlineHost, SlotSidebarInlineHost,
            FileSidebarOverlayHost, SlotSidebarOverlayHost,
            DrawerScrim,
            FileToggleButton, SlotToggleButton);
        StatusBar.SettingsRequested += OnOpenSettingsRequested;
        StatusBar.CompareRequested += OnCompareRequested;

        FolderDropHandler.Attach(this, _vm);
        BuildPluginMenu();
        // A replaced page must release its VM subscription (the VM outlives it now).
        Unloaded += (_, _) => _panes.Detach();
        if (firstLoad) _ = StartupAsync();
    }

    /// <summary>
    /// Adds a "Plugins" menu bar item with one entry per plugin-registered menu action, so
    /// plugins can contribute real menu commands. No-op when no menu actions are loaded.
    /// </summary>
    private void BuildPluginMenu()
    {
        var actions = Services.PluginService.MenuActions;
        if (actions.Count == 0)
        {
            return;
        }

        var menu = new MenuBarItem { Text = "Plugins" };
        foreach (var capability in actions)
        {
            var item = new MenuFlyoutItem { Text = capability.Value.Title };
            var cap = capability;
            item.Clicked += async (_, _) =>
            {
                try
                {
                    var context = Services.PluginService.CreateMenuActionContext(
                        cap, _vm.SelectedSave?.FullPath,
                        message => DisplayAlertAsync(cap.Value.Title, message, "OK"));
                    await cap.Value.InvokeAsync(context);
                }
                catch (Exception ex)
                {
                    cap.Plugin.Host?.Log.Error("menu action failed", ex);
                    await DisplayAlertAsync(cap.Value.Title, $"The action failed: {ex.Message}", "OK");
                }
            };
            menu.Add(item);
        }
        MenuBarItems.Add(menu);
    }

    private async Task StartupAsync()
    {
        _vm.HasStartedUp = true;
        await _vm.LoadLogoAsync();
        // Honour the testing/automation folder override only; otherwise the app stays on the
        // landing page and lets the user pick from the worlds discovered below (no auto-open).
        await _vm.ApplyStartupFolderOverrideAsync();
        await _vm.DiscoverWorldsAsync();

        // First run: no language chosen yet (the app is already showing the OS default) - let the
        // user confirm or change it.
        if (!Services.LocalizationService.HasChosenLanguage)
        {
            await Navigation.PushModalAsync(new LanguagePage());
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        _panes.HandleSizeAllocated(width);
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => _ = _panes.CloseDrawersAsync();

    private void OnToggleFilePane(object? sender, EventArgs e) => _panes.ToggleFilePane();

    private void OnToggleSlotPane(object? sender, EventArgs e) => _panes.ToggleSlotPane();

    private void OnFileSplitterPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started: _panes.BeginFileResize(); break;
            case GestureStatus.Running: _panes.UpdateFileResize(e.TotalX); break;
        }
    }

    private void OnSlotSplitterPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started: _panes.BeginSlotResize(); break;
            // The slot pane is on the right, so dragging left (negative X) widens it.
            case GestureStatus.Running: _panes.UpdateSlotResize(e.TotalX); break;
        }
    }

    // ---------- settings ----------

    private async void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new SettingsPage(_vm, RebuildForThemeAsync));
    }

    private async void OnCompareRequested(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new ComparePage(_vm));
    }

    /// <summary>
    /// The shared styles re-color live (DynamicResource), but inline StaticResource
    /// references and converter output only re-resolve on a fresh page tree. The new
    /// page reuses the shared view-model, so the loaded save and any staged edits carry
    /// over the swap - no leave-gate needed for a cosmetic change.
    /// </summary>
    private Task RebuildForThemeAsync()
    {
        if (Application.Current?.Windows is { Count: > 0 } windows)
        {
            windows[0].Page = new AppShell();
        }
        return Task.CompletedTask;
    }
}
