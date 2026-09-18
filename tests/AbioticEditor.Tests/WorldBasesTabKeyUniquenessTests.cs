namespace AbioticEditor.Tests;

/// <summary>
/// Round 118: a live report showed upgrading a bench crash the whole app so hard that no button
/// worked afterwards. The editor log pinned it to <c>RenderTreeDiffBuilder</c>: "More than one
/// sibling of element 'div' has the same key value" for a <c>Deployed_CraftingBench_Default_C</c>
/// actor's own id.
/// </summary>
/// <remarks>
/// The actual cause had nothing to do with duplicated live-agent data (see
/// <see cref="LiveBasesSessionTests"/> for that belt-and-braces fix instead): WorldBasesTab
/// renders two separate <c>@foreach</c> loops - "Crafting Benches" and, a little further down, the
/// same base's "Painted Objects" - as direct siblings under one shared <c>wt-card</c> parent, each
/// producing a <c>&lt;div class="bench-row"&gt;</c>. Both loops used to key their row on the bare
/// deployable id. <c>Deployed_CraftingBench_Default_C</c> is both a crafting bench
/// (<c>WorldDeployable.IsCraftingBench</c>) and paintable
/// (<c>DeployablePaintCatalog</c>), so it rendered in both lists with the identical <c>@key</c> -
/// Blazor's diff algorithm requires unique keys among the siblings a single render pass
/// produces, even across two unrelated <c>@foreach</c> blocks, once they land in the same parent
/// element. That is an <see cref="InvalidOperationException"/> raised by the renderer's own diff
/// pass while applying a render batch, not a normal component render exception, so it can slip
/// straight past an <c>ErrorBoundary</c> wrapping the page (see the reconnection-UI tests this
/// round added for what a lost circuit now shows instead) - the fix has to be the keys themselves.
///
/// Asserted against the source text, matching the rest of this suite's UI-parity/contract tests:
/// there is no rendered-DOM harness for Blazor Server components here, and the source text is
/// exactly what was wrong.
/// </remarks>
public sealed class WorldBasesTabKeyUniquenessTests
{
    [Fact]
    public void CraftingBench_and_painted_object_rows_key_on_different_namespaces()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldBasesTab.razor");

        // Each loop's own "bench-row" div prefixes the shared deployable id with which list
        // produced it (so the two lists can never collapse onto the same key even for a bench
        // that is also paintable), and RenderKeys.With then makes repeats within one list unique.
        Assert.Contains("RenderKeys.With(CraftingBenches(worldBaseDetail), b => \"bench:\" + b.Id)", source, StringComparison.Ordinal);
        Assert.Contains("<div class=\"bench-row\" @key=\"benchRenderKey\">", source, StringComparison.Ordinal);
        Assert.Contains("RenderKeys.With(paintable, d => \"paint:\" + d.Id)", source, StringComparison.Ordinal);
        Assert.Contains("<div class=\"bench-row\" @key=\"paintRenderKey\">", source, StringComparison.Ordinal);

        // The exact regression: either loop reverting to the bare id would collide again the next
        // time a crafting bench is also painted.
        Assert.DoesNotContain("@key=\"bench.Id\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@key=\"deployable.Id\"", source, StringComparison.Ordinal);
    }
}
