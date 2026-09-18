namespace AbioticEditor.Tests;

/// <summary>
/// Round 118: MainLayout already wraps the whole routed page in one <c>AppErrorBoundary</c>, but
/// a render exception from inside a single world/player tab used to take the entire editor surface
/// down with it - sidebar, tab strip and all - rather than just that one tab. Worse, a duplicate
/// <c>@key</c> (see <see cref="WorldBasesTabKeyUniquenessTests"/>) is raised by the renderer's own
/// diff pass while applying a batch, not as an ordinary per-component render exception, so it can
/// escape even a boundary wrapping the whole page. Neither surface can be proven safe against that
/// specific class of bug by a boundary alone - the real fix is the keys themselves - but an
/// ordinary exception from any other cause in one tab should no longer have to take the rest of the
/// shell with it, in either the file editor or the live one.
/// </summary>
/// <remarks>
/// Asserted against the source text, matching the rest of this suite's UI-parity/contract tests.
/// </remarks>
public sealed class PerTabErrorBoundaryTests
{
    [Theory]
    [InlineData("Components/Pages/SaveEditorSurface.razor", "world-tab-viewport", "_worldTab")]
    [InlineData("Components/Player/PlayerEditor.razor", "player-tab-viewport", "ActiveTab")]
    [InlineData("Components/Pages/LiveConnect.razor", null, "_worldTab")]
    public void TabViewport_wraps_its_active_tab_in_a_keyed_AppErrorBoundary(string relativePath, string? viewportClass, string tabField)
    {
        var source = UiSource.ReadAllText(relativePath.Split('/'));

        if (viewportClass is not null)
        {
            Assert.Contains($"class=\"{viewportClass}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains($"<AppErrorBoundary @key=\"{tabField}\">", source, StringComparison.Ordinal);
        Assert.Contains("</AppErrorBoundary>", source, StringComparison.Ordinal);
    }
}
