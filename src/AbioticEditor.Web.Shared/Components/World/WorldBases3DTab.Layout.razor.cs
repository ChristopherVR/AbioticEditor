using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The view's layout: the side panel's tabs (inspect, objects, filters, display, edit) and the loading
/// readout drawn over the view while models, textures and the level arrive.
/// </summary>
public partial class WorldBases3DTab
{
    // The side panel no longer has tabs (what is drawn moved into the view's Show menu); the names are
    // kept so a view kept from before still restores.
    private static readonly string[] SideTabs = ["objects"];
    private string _sideTab = "objects";

    /// <summary>The changes waiting for SAVE are listed in the side panel only when asked for.</summary>
    private bool _showStaged;

    /// <summary>How many base changes wait for SAVE (moves, removals, copies and new pieces, power changes).</summary>
    private int StagedCount => _basePreview is { } bp ? bp.Transforms.Count + bp.Deletions.Count + bp.Duplications.Count + bp.PowerLinks.Count : 0;
    private ElementReference _viewport;


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
    private double? _levelFraction;

    /// <summary>What the view is still waiting for; empty once everything has arrived.</summary>
    private List<LoadingLine> LoadingLines()
    {
        var lines = new List<LoadingLine>();
        if (!_viewReady) lines.Add(new LoadingLine(L.Resource("World3D_Loading"), null));
        if (_modelsOn && _modelCounts.Total > 0 && _modelCounts.Done < _modelCounts.Total)
            lines.Add(new LoadingLine(L.Resource("World3D_ModelsProgressFormat", _modelCounts.Done, _modelCounts.Total), (double)_modelCounts.Done / _modelCounts.Total));
        if (_textureCounts.Total > 0 && _textureCounts.Done < _textureCounts.Total)
            lines.Add(new LoadingLine(L.Resource("World3D_TexturesProgressFormat", _textureCounts.Done, _textureCounts.Total), (double)_textureCounts.Done / _textureCounts.Total));
        if (_levelOn && _levelBusy && _levelProgress is not null) lines.Add(new LoadingLine(_levelProgress, _levelFraction));
        return lines;
    }
}
