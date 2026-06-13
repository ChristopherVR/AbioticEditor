using System.ComponentModel;
using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Owns the page's responsive behavior: header compaction, auto-collapse of the inline
/// side panes on narrow desktops, and the phone drawer mode where both panes re-home
/// from the inline columns into overlay hosts and slide in over the editor behind a
/// tap-to-close scrim. Extracted from MainPage so the page stays declarative.
/// </summary>
public sealed class ResponsivePaneController
{
    /// <summary>Below this width the side panes become slide-in overlay drawers (phone layout).</summary>
    private const double DrawerWidthThreshold = 800;

    /// <summary>Below this width the inline side panes auto-collapse (narrow desktop).</summary>
    private const double CompactWidthThreshold = 1150;

    /// <summary>Below this width the header drops the folder breadcrumb + version tag.</summary>
    private const double CompactHeaderThreshold = 900;

    private const double FilePaneInlineWidth = 340;
    private const double SlotPaneInlineWidth = 400;

    // User-resizable inline pane widths (drag the splitters), clamped to these bounds.
    private const double FilePaneMinWidth = 220;
    private const double FilePaneMaxWidth = 600;
    private const double SlotPaneMinWidth = 260;
    private const double SlotPaneMaxWidth = 680;

    private readonly Page _page;
    private readonly MainViewModel _vm;
    private readonly HeaderBarView _header;
    private readonly View _filePane;
    private readonly View _slotPane;
    private readonly ContentView _fileInlineHost;
    private readonly ContentView _slotInlineHost;
    private readonly ContentView _fileOverlayHost;
    private readonly ContentView _slotOverlayHost;
    private readonly View _scrim;
    private readonly Button _fileRailButton;
    private readonly Button _slotRailButton;

    // Material Symbols chevrons used on the edge rails.
    private const string ChevronLeft = "\uE5CB";
    private const string ChevronRight = "\uE5CC";

    private bool? _wasCompact;
    private bool _drawerMode;
    private bool _filePaneUserHidden;
    private bool _slotPaneUserHidden;
    private bool _fileDrawerOpen;
    private bool _slotDrawerOpen;

    // Current (user-adjustable) inline widths + the width captured at a drag's start.
    private double _fileWidth = FilePaneInlineWidth;
    private double _slotWidth = SlotPaneInlineWidth;
    private double _fileResizeStart;
    private double _slotResizeStart;

    public ResponsivePaneController(
        Page page,
        MainViewModel vm,
        HeaderBarView header,
        View filePane,
        View slotPane,
        ContentView fileInlineHost,
        ContentView slotInlineHost,
        ContentView fileOverlayHost,
        ContentView slotOverlayHost,
        View scrim,
        Button fileRailButton,
        Button slotRailButton)
    {
        _page = page;
        _vm = vm;
        _header = header;
        _filePane = filePane;
        _slotPane = slotPane;
        _fileInlineHost = fileInlineHost;
        _slotInlineHost = slotInlineHost;
        _fileOverlayHost = fileOverlayHost;
        _slotOverlayHost = slotOverlayHost;
        _scrim = scrim;
        _fileRailButton = fileRailButton;
        _slotRailButton = slotRailButton;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateRailGlyphs();
    }

    /// <summary>
    /// Points each rail's chevron the right way: toward the pane when it's open (a
    /// "collapse" hint), toward the screen edge when it's hidden (an "expand" hint). Uses
    /// the drawer-open state in drawer mode, the inline visibility otherwise.
    /// </summary>
    private void UpdateRailGlyphs()
    {
        var fileShown = _drawerMode ? _fileDrawerOpen : _filePane.IsVisible;
        var slotShown = _drawerMode ? _slotDrawerOpen : _slotPane.IsVisible;
        _fileRailButton.Text = fileShown ? ChevronLeft : ChevronRight;
        _slotRailButton.Text = slotShown ? ChevronRight : ChevronLeft;
    }

    // ---------- splitter drag-resize (inline desktop panes) ----------

    /// <summary>Captures the file pane's width at the start of a splitter drag.</summary>
    public void BeginFileResize() => _fileResizeStart = _fileWidth;

    /// <summary>
    /// Resizes the file pane during a splitter drag. <paramref name="totalX"/> is the
    /// cumulative horizontal movement since the drag began; dragging right widens it.
    /// </summary>
    public void UpdateFileResize(double totalX)
    {
        _fileWidth = Math.Clamp(_fileResizeStart + totalX, FilePaneMinWidth, FilePaneMaxWidth);
        if (!_drawerMode) _filePane.WidthRequest = _fileWidth;
    }

    /// <summary>Captures the slot pane's width at the start of a splitter drag.</summary>
    public void BeginSlotResize() => _slotResizeStart = _slotWidth;

    /// <summary>
    /// Resizes the slot pane during a splitter drag. The pane sits on the right, so dragging
    /// left (negative <paramref name="totalX"/>) widens it.
    /// </summary>
    public void UpdateSlotResize(double totalX)
    {
        _slotWidth = Math.Clamp(_slotResizeStart - totalX, SlotPaneMinWidth, SlotPaneMaxWidth);
        if (!_drawerMode) _slotPane.WidthRequest = _slotWidth;
    }

    /// <summary>
    /// Drops the view-model subscription. The view-model now outlives the page (it is
    /// shared so a theme rebuild keeps the loaded save), so a discarded page must let go
    /// or it stays alive on the VM's event list across every theme switch.
    /// </summary>
    public void Detach() => _vm.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Picking a save from the phone drawer should reveal the editor behind it.
            case nameof(MainViewModel.SelectedSave) when _fileDrawerOpen:
                _ = CloseDrawersAsync();
                break;

            // Selecting a slot surfaces the slot editor wherever it currently lives:
            // as a drawer on phones, or by un-collapsing the inline pane on desktops.
            case nameof(MainViewModel.ActiveSlot) when _vm.ActiveSlot is not null:
                if (_drawerMode)
                {
                    if (!_slotDrawerOpen) _ = OpenDrawerAsync(file: false);
                }
                else if (!_slotPane.IsVisible && !_slotPaneUserHidden)
                {
                    _slotPane.IsVisible = true;
                    UpdateRailGlyphs();
                }
                break;
        }
    }

    public void HandleSizeAllocated(double width)
    {
        if (width <= 0) return;

        _header.SetCompact(width < CompactHeaderThreshold);

        var drawer = width < DrawerWidthThreshold;
        if (drawer != _drawerMode)
        {
            _drawerMode = drawer;
            if (drawer) EnterDrawerMode();
            else ExitDrawerMode();
        }

        var compact = width < CompactWidthThreshold;
        if (_wasCompact == compact) return;
        _wasCompact = compact;

        if (!_drawerMode)
        {
            // Auto-collapse in compact mode; restore on widen unless the user hid a pane.
            _filePane.IsVisible = !compact && !_filePaneUserHidden;
            _slotPane.IsVisible = !compact && !_slotPaneUserHidden;
        }
        UpdateRailGlyphs();
    }

    public void ToggleFilePane()
    {
        if (_drawerMode)
        {
            _ = _fileDrawerOpen ? CloseDrawersAsync() : OpenDrawerAsync(file: true);
            return;
        }
        _filePane.IsVisible = !_filePane.IsVisible;
        _filePaneUserHidden = !_filePane.IsVisible;
        UpdateRailGlyphs();
    }

    public void ToggleSlotPane()
    {
        if (_drawerMode)
        {
            _ = _slotDrawerOpen ? CloseDrawersAsync() : OpenDrawerAsync(file: false);
            return;
        }
        _slotPane.IsVisible = !_slotPane.IsVisible;
        _slotPaneUserHidden = !_slotPane.IsVisible;
        UpdateRailGlyphs();
    }

    /// <summary>Re-homes both side panes from the inline columns into the overlay hosts.</summary>
    private void EnterDrawerMode()
    {
        _fileInlineHost.Content = null;
        _slotInlineHost.Content = null;
        _fileOverlayHost.Content = _filePane;
        _slotOverlayHost.Content = _slotPane;

        // Drawer visibility is gated by the hosts; the panes themselves fill them.
        _filePane.IsVisible = true;
        _slotPane.IsVisible = true;
        _filePane.WidthRequest = -1;
        _slotPane.WidthRequest = -1;

        _fileOverlayHost.IsVisible = false;
        _slotOverlayHost.IsVisible = false;
        _scrim.IsVisible = false;
        _scrim.Opacity = 0;
        _fileDrawerOpen = false;
        _slotDrawerOpen = false;
        UpdateRailGlyphs();
    }

    /// <summary>Puts the side panes back into the inline desktop columns.</summary>
    private void ExitDrawerMode()
    {
        _fileOverlayHost.Content = null;
        _slotOverlayHost.Content = null;
        _fileOverlayHost.IsVisible = false;
        _slotOverlayHost.IsVisible = false;
        _scrim.IsVisible = false;
        _scrim.Opacity = 0;
        _fileDrawerOpen = false;
        _slotDrawerOpen = false;

        _filePane.WidthRequest = _fileWidth;
        _slotPane.WidthRequest = _slotWidth;
        _filePane.TranslationX = 0;
        _slotPane.TranslationX = 0;
        _fileInlineHost.Content = _filePane;
        _slotInlineHost.Content = _slotPane;

        var compact = _wasCompact == true;
        _filePane.IsVisible = !compact && !_filePaneUserHidden;
        _slotPane.IsVisible = !compact && !_slotPaneUserHidden;
        UpdateRailGlyphs();
    }

    private async Task OpenDrawerAsync(bool file)
    {
        await CloseDrawersAsync(); // one drawer at a time keeps the editor visible behind the scrim

        var host = file ? _fileOverlayHost : _slotOverlayHost;
        var width = Math.Min(file ? 360 : 420, _page.Width * 0.88);
        host.WidthRequest = width;
        host.TranslationX = file ? -width : width;
        host.IsVisible = true;
        _scrim.IsVisible = true;
        if (file) _fileDrawerOpen = true;
        else _slotDrawerOpen = true;

        UpdateRailGlyphs();
        _ = _scrim.FadeToAsync(0.45, 160);
        await host.TranslateToAsync(0, 0, 220, Easing.CubicOut);
    }

    public async Task CloseDrawersAsync()
    {
        var slides = new List<Task>();
        if (_fileDrawerOpen) slides.Add(SlideDrawerOutAsync(_fileOverlayHost, -1));
        if (_slotDrawerOpen) slides.Add(SlideDrawerOutAsync(_slotOverlayHost, +1));
        _fileDrawerOpen = false;
        _slotDrawerOpen = false;
        UpdateRailGlyphs();
        if (slides.Count == 0) return;

        _ = _scrim.FadeToAsync(0, 160);
        await Task.WhenAll(slides);
        _scrim.IsVisible = false;
    }

    private static async Task SlideDrawerOutAsync(View host, int direction)
    {
        await host.TranslateToAsync(direction * Math.Max(host.Width, 1), 0, 180, Easing.CubicIn);
        host.IsVisible = false;
    }
}
