using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The view's layout: the side panel's tabs (inspect, objects, filters, display, edit) and the loading
/// readout drawn over the view while models, textures and the level arrive.
/// </summary>
public partial class WorldBases3DTab
{
    private static readonly string[] SideTabs = ["objects", "filters", "display"];
    private string _sideTab = "objects";
    private ElementReference _viewport;

    private string SideTabLabel(string tab) => tab switch
    {
        "objects" => L.Resource("World3D_TabObjectsFormat", _visible.Length),
        "filters" => L.Resource("World3D_TabFilters"),
        _ => L.Resource("World3D_TabDisplay"),
    };

    /// <summary>
    /// After something was picked. The inspector is always shown above the tabs now, so nothing
    /// switches; kept as the one place to hook "the inspector has news".
    /// </summary>
    private void ShowInspector() { }

    /// <summary>One line of the loading readout: what is loading, and how far along (null when unknown).</summary>
    private sealed record LoadingLine(string Text, double? Fraction);

    private (int Done, int Total) _modelCounts;
    private (int Done, int Total) _textureCounts;
    private bool _levelBusy;

    /// <summary>What the view is still waiting for; empty once everything has arrived.</summary>
    private List<LoadingLine> LoadingLines()
    {
        var lines = new List<LoadingLine>();
        if (!_viewReady) lines.Add(new LoadingLine(L.Resource("World3D_Loading"), null));
        if (_modelsOn && _modelCounts.Total > 0 && _modelCounts.Done < _modelCounts.Total)
            lines.Add(new LoadingLine(L.Resource("World3D_ModelsProgressFormat", _modelCounts.Done, _modelCounts.Total), (double)_modelCounts.Done / _modelCounts.Total));
        if (_textureCounts.Total > 0 && _textureCounts.Done < _textureCounts.Total)
            lines.Add(new LoadingLine(L.Resource("World3D_TexturesProgressFormat", _textureCounts.Done, _textureCounts.Total), (double)_textureCounts.Done / _textureCounts.Total));
        if (_levelOn && _levelBusy && _levelProgress is not null) lines.Add(new LoadingLine(_levelProgress, null));
        return lines;
    }
}
