using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The Bases map's backdrop: the level under the bases seen straight from above, cut a few metres
/// over the floor of the base looked at, drawn by the 3D view's renderer (hidden) where the game's
/// files can be read. It lies exactly under the map's markers. Without the game files (or in the
/// browser build) the map keeps its plain grid. Pictures are kept for the session, one per map
/// framing and floor.
/// </summary>
public partial class WorldBasesTab
{
    [Inject] private IJSRuntime Js { get; set; } = null!;

    private IJSObjectReference? _viewModule;
    private ElementReference _backdropImage;
    private bool _backdropShown;
    private string? _backdropKey;
    private bool _backdropBusy;
    private bool _disposedBackdrop;

    /// <summary>The map wants a level picture: a save on this computer whose area has a level to draw.</summary>
    private bool BackdropWanted => !_show3D && Session is WorldSaveSession save && Base3DScene.RegionOf(save.Path) is not null;

    /// <summary>Above the floor, the level is cut away so the rooms the bases stand in show.</summary>
    private const double BackdropCutMetres = 3;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await EnsureBackdropAsync();
    }

    private async Task EnsureBackdropAsync()
    {
        // The map (and its picture element) is rebuilt after the 3D view or an open container: ask again then.
        if (!BackdropWanted || OpenContainer is not null) { _backdropKey = null; _backdropShown = false; return; }
        if (_backdropBusy || Session is not WorldSaveSession save || MapLayout() is not { } map) return;
        if (Base3DScene.RegionOf(save.Path) is not { } region) return;
        var floor = FloorHeight();
        var key = FormattableString.Invariant($"{save.Path}|{map.MinX:0}|{map.MinY:0}|{map.Scale:0.00000}|{floor:0}");
        if (key == _backdropKey) return;
        _backdropKey = key;
        _backdropBusy = true;
        if (_backdropShown)
        {
            _backdropShown = false; // the plain grid until the new framing's picture is in
            StateHasChanged();
        }
        var shown = false;
        var pending = false;
        try
        {
            // The map's whole rectangle in save space (Px/Py put the first marker 18 px in).
            var x0 = map.MinX - 18 / map.Scale;
            var y0 = map.MinY - 18 / map.Scale;
            var x1 = x0 + MapW / map.Scale;
            var y1 = y0 + MapH / map.Scale;
            var centre = PlacedSceneSpace.ToViewer(new PlacedVector((x0 + x1) / 2, (y0 + y1) / 2, floor));
            var right = Subtract(PlacedSceneSpace.ToViewer(new PlacedVector(x0 + 100, y0, floor)), PlacedSceneSpace.ToViewer(new PlacedVector(x0, y0, floor)));
            var down = Subtract(PlacedSceneSpace.ToViewer(new PlacedVector(x0, y0 + 100, floor)), PlacedSceneSpace.ToViewer(new PlacedVector(x0, y0, floor)));
            _viewModule ??= await Js.InvokeAsync<IJSObjectReference>("import", "./_content/AbioticEditor.Web.Shared/base3d.js");
            var answer = await _viewModule.InvokeAsync<string>("fillMapBackdrop", _backdropImage, key, new
            {
                region,
                center = new[] { centre.X, centre.Y, centre.Z },
                focus = FocusPoint(floor) is { } focusAt ? new[] { focusAt.X, focusAt.Y, focusAt.Z } : null,
                right,
                down,
                metresWide = (x1 - x0) / 100,
                metresHigh = (y1 - y0) / 100,
                floorY = centre.Y,
                cutAbove = BackdropCutMetres,
                width = (int)MapW * 2,
                height = (int)MapH * 2,
            });
            shown = answer == "shown";
            pending = answer == "pending";
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            shown = false;
        }
        finally
        {
            _backdropBusy = false;
        }
        if (shown != _backdropShown)
        {
            _backdropShown = shown;
            StateHasChanged();
        }
        if (pending)
        {
            // The level files are still being read: ask again in a few seconds.
            _backdropKey = null;
            await Task.Delay(5000);
            if (!_disposedBackdrop) await InvokeAsync(EnsureBackdropAsync);
            return;
        }
        // The base picked while this one was drawn gets its own picture now (its floor may differ).
        await EnsureBackdropAsync();
    }

    private static double[] Subtract(PlacedVector a, PlacedVector b) => [a.X - b.X, a.Y - b.Y, a.Z - b.Z];

    /// <summary>The base looked at (its centre, in the viewer's space): the picture is most detailed around it.</summary>
    private PlacedVector? FocusPoint(double floor)
        => FocusBase() is { } chosen ? PlacedSceneSpace.ToViewer(new PlacedVector(chosen.CenterX, chosen.CenterY, floor)) : null;

    /// <summary>The base looked at, else the biggest one: the picture is most detailed and its floor taken there.</summary>
    private WorldBase? FocusBase()
        => SelectedBase is { Deployables.Count: > 0 } chosen ? chosen : Bases.OrderByDescending(b => b.Deployables.Count).FirstOrDefault();

    /// <summary>The floor of the base looked at (else of every piece): the middle height of its pieces, in save centimetres.</summary>
    private double FloorHeight()
    {
        var pieces = (FocusBase()?.Deployables is { Count: > 0 } chosen ? chosen : Session.Deployables).Select(d => d.Z).OrderBy(z => z).ToList();
        return pieces.Count == 0 ? 0 : pieces[pieces.Count / 2];
    }
}
