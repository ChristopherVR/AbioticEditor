using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// EXPORT is a browser-only action, and the screens that offer it are shared with the desktop.
/// </summary>
/// <remarks>
/// <para>In a browser an edited save exists nowhere but in the tab, so a zip of the world (or one
/// save on its own) is the only way the player's work ever reaches the game. On the desktop the
/// editor writes into the game's own save folder, so the same buttons only ever produced a second
/// copy of files the player already had, in a different place, and left them wondering which copy
/// the game was reading.</para>
///
/// <para>The screens are one set of files, so the difference lives entirely in what each host's
/// exporter answers. Nothing else fails if that answer is wrong: the desktop simply grows a button
/// it was not meant to have, or the browser loses the only way out of the editor.</para>
/// </remarks>
public sealed class SaveExportHostGateTests
{
    [Fact]
    public void The_desktop_does_not_offer_export()
    {
        var exporter = new DesktopSaveExporter(new SilentNavigation());

        Assert.False(exporter.OffersSaveExport);
    }

    [Fact]
    public void The_browser_does_offer_export()
    {
        var source = File.ReadAllText(Path.Combine(
            UiSource.RepositoryRoot, "src", "AbioticEditor.Web.Wasm", "Services", "BrowserSaveExporter.cs"));

        Assert.Contains("OffersSaveExport => true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_asks_the_host_before_drawing_any_export_action()
    {
        var shell = UiSource.ReadAllText("Components/Shared/WorkspaceShell.razor");

        // The whole-world zip, the single-save context-menu item, and the "your work leaves
        // through EXPORT" reminder after a save.
        var gates = shell.Split("Export.OffersSaveExport", StringSplitOptions.None).Length - 1;
        Assert.True(
            gates >= 3,
            "The shell must gate each of its export actions on Export.OffersSaveExport: the "
            + "sidebar EXPORT button, the 'export this save' item on a save's right-click menu, "
            + $"and the reminder shown after a save. Found {gates} gate(s).");

        Assert.DoesNotContain("Export.CanExport", shell, StringComparison.Ordinal);
    }

    /// <summary>
    /// Handing back one file still works on both hosts, which is what the raw-data view and the
    /// appearance editor use to download what they are holding.
    /// </summary>
    [Theory]
    [InlineData("Components/World/WorldRawTab.razor")]
    [InlineData("Components/Player/PlayerRawDataTab.razor")]
    [InlineData("Components/Player/PlayerAppearanceEditor.razor")]
    public void Single_file_downloads_are_not_gated_on_the_export_action(string page)
    {
        var source = UiSource.ReadAllText(page);

        Assert.Contains("Exporter.ExportAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OffersSaveExport", source, StringComparison.Ordinal);
    }

    private sealed class SilentNavigation : AbioticEditor.Ui.IExternalNavigationService
    {
        public Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevealPathAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
