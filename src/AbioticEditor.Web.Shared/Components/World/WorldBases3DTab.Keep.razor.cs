using System.Runtime.CompilerServices;
using AbioticEditor.Web.Models;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Leaving the 3D view (another tab, the map) and coming back. The viewer keeps its drawing in
/// memory under the save's path (base3d.js <c>park</c>), and the settings chosen here are kept per
/// open save, so the view comes back as it was left: same camera, level, filters and panel tab,
/// with nothing read or uploaded again.
/// </summary>
public partial class WorldBases3DTab
{
    private sealed record KeptView(
        bool ModelsOn, bool LevelOn, int LevelRadius, int LevelCut, bool LampsOn,
        bool Labels, bool DoorsOn, bool NpcsOn, string SideTab, Base3DFilter Filter);

    private static readonly ConditionalWeakTable<WorldSaveSession, KeptView> Kept = new();

    /// <summary>Which kept drawing belongs to this view: one per save file.</summary>
    private string ParkKey => Session.Path;

    private bool? _keptModelsOn;

    private void RestoreKept()
    {
        if (!Kept.TryGetValue(Session, out var k)) return;
        _keptModelsOn = k.ModelsOn;
        (_levelOn, _levelRadius, _levelCut, _lampsOn) = (k.LevelOn, k.LevelRadius, k.LevelCut, k.LampsOn);
        (_labels, _doorsOn, _npcsOn, _filter) = (k.Labels, k.DoorsOn, k.NpcsOn, k.Filter);
        if (Array.IndexOf(SideTabs, k.SideTab) >= 0) _sideTab = k.SideTab;
    }

    private void Keep() => Kept.AddOrUpdate(Session,
        new KeptView(_modelsOn, _levelOn, _levelRadius, _levelCut, _lampsOn, _labels, _doorsOn, _npcsOn, _sideTab, _filter));

    /// <summary>
    /// After the view is created: a kept drawing keeps its camera (no reframing); a new drawing with
    /// kept settings is told the ones that differ from its defaults.
    /// </summary>
    private async Task AfterViewCreatedAsync()
    {
        if (_view is null) return;
        if (await _view.InvokeAsync<bool>("isReattached"))
        {
            _framedOnce = true;
            _framedBase = FocusBase?.Name;
            return;
        }
        if (!_doorsOn) await _view.InvokeVoidAsync("setDoorsVisible", false);
        if (!_npcsOn) await _view.InvokeVoidAsync("setNpcsVisible", false);
        if (!_lampsOn) await _view.InvokeVoidAsync("setLampsVisible", false);
        if (_levelOn) _regionChanged = true;
    }

    /// <summary>Keeps the drawing for the next visit instead of throwing it away.</summary>
    private async Task ParkViewAsync()
    {
        if (_view is null) return;
        Keep();
        try { await _view.InvokeVoidAsync("park", ParkKey); }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or OperationCanceledException) { }
    }
}
