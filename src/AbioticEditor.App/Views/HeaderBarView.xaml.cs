namespace AbioticEditor.App.Views;

/// <summary>
/// Branded top bar: logo, active-folder breadcrumb, HOME and the OPEN FOLDER action.
/// The save-list / slot-editor pane toggles live on edge rails next to each pane (see
/// MainPage + ResponsivePaneController), not here.
/// </summary>
public partial class HeaderBarView : ContentView
{
    public HeaderBarView()
    {
        InitializeComponent();

        // Pin the header version tag to the build's real version (Directory.Build.props <Version>
        // -> ApplicationDisplayVersion, which the release workflow rewrites per release).
#if !NEXUSMODS
        VersionLabel.Text = Services.LocalizationResourceManager.Instance.Format("HeaderBar_VersionLabel", Services.UpdateService.CurrentVersion);
#else
        VersionLabel.Text = Services.LocalizationResourceManager.Instance.Format("HeaderBar_VersionLabel", AppInfo.Current.VersionString);
#endif
    }

    /// <summary>Drops the breadcrumb + version tag when horizontal space is scarce.</summary>
    public void SetCompact(bool compact)
    {
        FolderBreadcrumb.IsVisible = !compact;
        VersionLabel.IsVisible = !compact;
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { } vm)
        {
            await vm.GoHomeAsync();
        }
    }

    private async void OnOpenFolderTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.Vm(this) is { } vm)
        {
            await FolderPicking.PickAndLoadAsync(this, vm);
        }
    }
}
