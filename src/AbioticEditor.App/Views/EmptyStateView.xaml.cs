using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;


/// <summary>Empty state: onboarding copy + the discovered-worlds quick-load list.</summary>
public partial class EmptyStateView : ContentView
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    private async void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { } vm)
        {
            await FolderPicking.PickAndLoadAsync(this, vm);
        }
    }

    private async void OnCreateWorldClicked(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        var page = ViewUtils.ParentPage(this);
        if (page is not null)
        {
            await page.Navigation.PushModalAsync(new CreateWorldPage(vm));
        }
    }

    private async void OnDiscoveredWorldClicked(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        var option = ViewUtils.FindBoundContext<DiscoveredWorldOption>(sender);
        if (option is null) return;
        await vm.OpenDiscoveredWorldAsync(option.World);
    }

    private void OnDiscoveredWorldTapped(object? sender, TappedEventArgs e)
        => OnDiscoveredWorldClicked(sender, e);

    private async void OnRemoveDiscoveredWorldClicked(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        if ((sender as MenuFlyoutItem)?.BindingContext is not DiscoveredWorldOption option) return;
        var loc = LocalizationResourceManager.Instance;
        var confirmed = await ViewUtils.ConfirmAsync(
            this,
            loc["Empty_RemoveConfirmTitle"],
            loc.Format("Empty_RemoveConfirmMessage", option.Name),
            loc["Common_Ok"],
            loc["Common_Cancel"]);
        if (confirmed) vm.HideDiscoveredWorld(option);
    }
}
