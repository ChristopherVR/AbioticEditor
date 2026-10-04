using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// "Show in 3D" from the other world tabs: the view frames what was asked for. A placed object is
/// selected and framed; a door or a character has its marker selected and framed; a level actor (a
/// button, a breakable wall) or a saved position (a dropped item, a vehicle) gets a pin.
/// </summary>
public partial class WorldBases3DTab
{
    /// <summary>The latest "Show in 3D" request; a new record is a new request.</summary>
    [Parameter] public WorldLocateTarget? Locate { get; set; }

    private WorldLocateTarget? _seenLocate;
    private WorldLocateTarget? _pendingLocate;
    private string? _locateNote;

    private const double LocateDistanceM = 9;

    /// <summary>
    /// "Show in 3D" covers the view until the models, textures and level around the target are in,
    /// so it never shows the place half-built (at most 12 seconds, then it shows whatever is there).
    /// </summary>
    private bool _curtain;

    private async Task LiftCurtainWhenLoadedAsync()
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        await Task.Delay(400); // the level query for the new spot starts a moment after the camera moves
        while (DateTime.UtcNow < until && !_disposed && LoadingLines().Count > 0) await Task.Delay(250);
        _curtain = false;
        await InvokeAsync(StateHasChanged);
    }

    private void TakeLocateRequest()
    {
        if (Locate is null || ReferenceEquals(Locate, _seenLocate)) return;
        _seenLocate = Locate;
        _pendingLocate = Locate;
        _curtain = true; // the view stays covered until what it shows has loaded
    }

    /// <summary>Runs a waiting request once the view has its scene.</summary>
    private async Task ProcessLocateAsync()
    {
        if (_pendingLocate is not { } target || _view is null || !_framedOnce) return;
        _pendingLocate = null;
        try
        {
            await _view.InvokeVoidAsync("reveal");
            _locateNote = await LocateAsync(target) ? L.Resource("World3D_LocatedFormat", target.Label) : _locateNote;
            // The level around the target starts loading now, not when the camera settles, so the
            // cover below waits for it.
            if (_levelOn) await _view.InvokeAsync<bool>("followLevel");
            _ = LiftCurtainWhenLoadedAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException) { return; }
        StateHasChanged();
    }

    /// <summary>Frames the target. False (with a note saying why) when the view cannot place it.</summary>
    private async Task<bool> LocateAsync(WorldLocateTarget target)
    {
        await _view!.InvokeVoidAsync("clearPin");
        switch (target.Kind)
        {
            case WorldLocateKind.PlacedObject:
                if (await LocatePlacedAsync(target.Id)) return true;
                if (_scene.Unresolved.Any(u => string.Equals(u.Key, target.Id, StringComparison.Ordinal)))
                {
                    ShowInspector();
                    SelectUnresolved(target.Id);
                    _locateNote = L.Resource("World3D_LocateNoPlaceFormat", target.Label);
                    return false;
                }
                return await LocatePointAsync(target, Session.PositionAfterStaging(target.Id) ?? target.At);

            case WorldLocateKind.Door:
                await LevelForContextAsync();
                if (!_doorsOn) await SetDoorsVisibleAsync(true);
                if (!await FocusMarkerAsync("door", target.Id))
                {
                    await PushDoorsAsync();
                    if (!await FocusMarkerAsync("door", target.Id)) return NotPlaced(target);
                }
                await OnDoorPicked(target.Id);
                return true;

            case WorldLocateKind.Character:
                await LevelForContextAsync();
                if (!_npcsOn) await SetNpcsVisibleAsync(true);
                if (!await FocusMarkerAsync("npc", target.Id))
                {
                    await PushNpcsAsync();
                    if (!await FocusMarkerAsync("npc", target.Id)) return await LocatePointAsync(target, target.At);
                }
                await OnNpcPicked(target.Id);
                return true;

            case WorldLocateKind.LevelActor:
                if (InBrowser)
                {
                    _locateNote = L.Resource("World3D_LocateNoLevelFormat", target.Label);
                    return false;
                }
                var at = target.At;
                if (at is null && await Art.TryGetActorWorldTransformAsync(target.Id) is { } t) at = new(t.X, t.Y, t.Z);
                var shown = await LocatePointAsync(target, at);
                // A breakable wall, button, resource node... opens its card too, so what can be done
                // with it is right there (not only a pin).
                var (actorMap, actorName) = DoorIdParser.Parse(target.Id);
                if (actorMap.Length > 0)
                    await PickLevelActorAsync($"{actorMap[(actorMap.LastIndexOf('/') + 1)..]}:{actorName[(actorName.LastIndexOf('.') + 1)..]}");
                return shown;

            default:
                if (Session.Vehicles.Any(v => v.Id == target.Id) && await LocatePlacedAsync(target.Id)) return true;
                return await LocatePointAsync(target, target.At);
        }
    }

    /// <summary>Selects and frames a drawn object, clearing the filters first when they hide it.</summary>
    private async Task<bool> LocatePlacedAsync(string key)
    {
        var index = -1;
        for (var i = 0; i < _scene.Objects.Count; i++)
        {
            if (string.Equals(_scene.Objects[i].Key, key, StringComparison.Ordinal)) { index = i; break; }
        }
        if (index < 0) return false;
        if (Array.IndexOf(_visible, index) < 0)
        {
            ResetFilters();
            await PushSceneAsync();
        }
        await SelectAsync(key, frame: true);
        return true;
    }

    private async Task<bool> FocusMarkerAsync(string kind, string id)
        => await _view!.InvokeAsync<bool>("focusMarker", kind, id, LocateDistanceM);

    /// <summary>Flies to a save-space point and pins it; the level is turned on for context when it can be.</summary>
    private async Task<bool> LocatePointAsync(WorldLocateTarget target, PlacedVector? at)
    {
        if (at is not { } point) return NotPlaced(target);
        await LevelForContextAsync();
        var v = PlacedSceneSpace.ToViewer(point);
        await _view!.InvokeAsync<bool>("focusPoint", new[] { v.X, v.Y, v.Z }, LocateDistanceM);
        return true;
    }

    /// <summary>Things in the level (doors, characters, buttons) mean little on an empty grid: the level is turned on when it can be.</summary>
    private async Task LevelForContextAsync()
    {
        if (!_levelOn && _modelStatus is { Available: true } && LevelRegion is not null) await SetLevelAsync(true);
    }

    private bool NotPlaced(WorldLocateTarget target)
    {
        _locateNote = L.Resource("World3D_LocateNoPlaceFormat", target.Label);
        return false;
    }
}
