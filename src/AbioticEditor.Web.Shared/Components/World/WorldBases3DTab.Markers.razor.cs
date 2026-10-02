using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Services;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Web.Models;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Clickable markers for what else is in the area: items lying on the ground (from the save) and
/// things of the level from the world lists (buttons, breakable walls, resource nodes, elevators,
/// NPC spawns, teleporters and trams, placed where the level puts them). A click opens a card with a
/// link to the tab that edits it.
/// </summary>
public partial class WorldBases3DTab
{
    /// <summary>World lists keyed by level actors, which the view can place from the game files.</summary>
    private static readonly string[] ThingFeatures = ["buttons", "destructibles", "resource-nodes", "elevators", "npc-spawns", "portals", "trams", "power-sockets"];

    private const int MaxThings = 4000;
    private const int ThingBatch = 400;
    private const int ItemColor = 0xffd23f;

    private sealed record ThingInfo(string FeatureId, string FeatureName, string Key, string Label, PlacedVector At);

    private bool _itemsOn; // off at first: hundreds of them crowd the overview; one click away in Show
    private bool _thingsOn = true;
    private bool _markersDirty = true;
    private int _itemStamp = -1;
    private int _markersToken;
    private Dictionary<string, ThingInfo> _things = new(StringComparer.Ordinal);
    private (string Kind, string Id)? _pickedMarker;

    private WorldDroppedItem? PickedItem => _pickedMarker is ("item", var id)
        ? Session.DroppedItems.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal)) : null;

    private ThingInfo? PickedThing => _pickedMarker is ("thing", var id) ? _things.GetValueOrDefault(id) : null;

    private string ItemName(WorldDroppedItem item) => Items.Find(item.Slot.ItemId)?.DisplayName ?? item.Slot.ItemId ?? "?";

    /// <summary>Sends the ground items now and the level things as their places are found.</summary>
    private async Task PushMarkersAsync()
    {
        if (_view is null) return;
        var token = ++_markersToken;
        var items = Session.DroppedItems
            .Where(i => i.X != 0 || i.Y != 0 || i.Z != 0)
            .Select(i =>
            {
                var v = PlacedSceneSpace.ToViewer(new PlacedVector(i.X, i.Y, i.Z));
                return new { id = i.Id, p = new[] { v.X, v.Y, v.Z }, color = ItemColor };
            })
            .ToList();
        try
        {
            await _view.InvokeVoidAsync("setMarkers", "item", items);
            await _view.InvokeVoidAsync("setMarkersVisible", "item", _itemsOn);
        }
        catch (JSDisconnectedException) { return; }

        // Level things come from the game files, which the browser build does not have.
        if (InBrowser) return;
        // Every kind takes turns (a few buttons, a few spawn points, ...), so each kind shows up early
        // even in an area with hundreds of one kind, and markers are sent in batches as their places
        // are found instead of all at the end. (A cap of 600 used to leave the spawn points out.)
        var perFeature = ThingFeatures
            .Select(id => Session.MapFeature(id))
            .Where(f => f is not null)
            .Select(f => new Queue<(WorldMapFeatureSnapshot Feature, WorldMapEntry Entry)>(
                f!.Entries.Where(e => e.Key.StartsWith("/Game/", StringComparison.Ordinal)).Select(e => (f!, e))))
            .ToList();
        var wanted = new List<(WorldMapFeatureSnapshot Feature, WorldMapEntry Entry)>();
        while (perFeature.Any(q => q.Count > 0) && wanted.Count < MaxThings)
        {
            foreach (var queue in perFeature)
            {
                if (queue.Count > 0) wanted.Add(queue.Dequeue());
            }
        }
        var things = new Dictionary<string, ThingInfo>(StringComparer.Ordinal);
        var markers = new List<object>();
        for (var start = 0; start < wanted.Count; start += ThingBatch)
        {
            var batch = wanted.Skip(start).Take(ThingBatch).ToList();
            var places = await Art.TryGetActorWorldTransformsAsync(batch.Select(w => w.Entry.Key));
            var found = batch.Select(w => (w.Feature, w.Entry, At: places.GetValueOrDefault(w.Entry.Key))).ToList();
            if (token != _markersToken || _disposed || _view is null) return;
            foreach (var (feature, entry, at) in found)
            {
                if (at is not { } t) continue;
                things[entry.Key] = new ThingInfo(feature.Id, feature.DisplayName, entry.Key, entry.Label, new PlacedVector(t.X, t.Y, t.Z));
                var v = PlacedSceneSpace.ToViewer(new PlacedVector(t.X, t.Y, t.Z));
                markers.Add(new { id = entry.Key, p = new[] { v.X, v.Y, v.Z }, color = ThingColor(feature.Id) });
            }
            _things = new Dictionary<string, ThingInfo>(things, StringComparer.Ordinal);
            try
            {
                await _view.InvokeVoidAsync("setMarkers", "thing", markers);
                if (start == 0) await _view.InvokeVoidAsync("setMarkersVisible", "thing", _thingsOn);
            }
            catch (JSDisconnectedException) { return; }
        }
    }

    private static int ThingColor(string featureId) => featureId switch
    {
        "buttons" => 0x4fc3f7,
        "power-sockets" => 0xffd23f,
        "destructibles" => 0xff7043,
        "resource-nodes" => 0x81c784,
        "elevators" or "trams" or "portals" => 0xba68c8,
        "npc-spawns" => 0xe53935,
        _ => 0xb0bec5,
    };

    private async Task SetItemsVisibleAsync(bool on)
    {
        _itemsOn = on;
        if (!on && _pickedMarker is ("item", _)) _pickedMarker = null;
        if (_view is not null) await _view.InvokeVoidAsync("setMarkersVisible", "item", on);
    }

    private async Task SetThingsVisibleAsync(bool on)
    {
        _thingsOn = on;
        if (!on && _pickedMarker is ("thing", _)) _pickedMarker = null;
        if (_view is not null) await _view.InvokeVoidAsync("setMarkersVisible", "thing", on);
    }

    /// <summary>A click on a ground item or level thing marker opens its card.</summary>
    [JSInvokable]
    public async Task OnMarkerPicked(string kind, string id)
    {
        if (_pickedMarker is { } old && _view is not null && old.Kind != kind) await _view.InvokeVoidAsync("setMarkerSelection", old.Kind, (string?)null);
        _pickedMarker = (kind, id);
        ShowInspector();
        if (_view is not null) await _view.InvokeVoidAsync("setMarkerSelection", kind, id);
        StateHasChanged();
    }

    private async Task CloseMarkerCardAsync()
    {
        if (_pickedMarker is { } old && _view is not null) await _view.InvokeVoidAsync("setMarkerSelection", old.Kind, (string?)null);
        _pickedMarker = null;
    }

    /// <summary>The picture of a level thing's kind (a button, an elevator...), or null.</summary>
    private static string? ThingPicture(ThingInfo thing)
        => WorldThumbnails.KindOfFeature(thing.FeatureId) is { } kind ? WorldThumbnails.For(kind, thing.Key) : null;


    /// <summary>A level thing's setting changed in its card: its marker and the scene are refreshed.</summary>
    private Task ThingChangedAsync()
    {
        _markersDirty = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens the card of the listed thing a clicked level actor ("Map:Actor") is: a door, or an entry
    /// of one of the world lists (a wall socket, a button...). False when it is none of those.
    /// </summary>
    private async Task<bool> PickLevelActorAsync(string mapActor)
    {
        var colon = mapActor.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return false;
        var map = mapActor[..colon];
        var actor = mapActor[(colon + 1)..];
        bool Same(string key)
        {
            var (keyMap, keyActor) = DoorIdParser.Parse(key);
            return string.Equals(keyActor[(keyActor.LastIndexOf('.') + 1)..], actor, StringComparison.OrdinalIgnoreCase)
                   && (string.IsNullOrEmpty(keyMap) ? map.Equals("Facility", StringComparison.OrdinalIgnoreCase)
                       : keyMap[(keyMap.LastIndexOf('/') + 1)..].Equals(map, StringComparison.OrdinalIgnoreCase));
        }
        if (Session.Doors.FirstOrDefault(d => Same(d.Id)) is { } door)
        {
            await OnDoorPicked(door.Id);
            return true;
        }
        foreach (var featureId in ThingFeatures)
        {
            if (Session.MapFeature(featureId) is not { } feature) continue;
            if (feature.Entries.FirstOrDefault(e => e.Key.StartsWith("/Game/", StringComparison.Ordinal) && Same(e.Key)) is not { } entry) continue;
            if (!_things.ContainsKey(entry.Key))
            {
                var at = await Art.TryGetActorWorldTransformAsync(entry.Key);
                _things = new Dictionary<string, ThingInfo>(_things, StringComparer.Ordinal)
                {
                    [entry.Key] = new ThingInfo(feature.Id, feature.DisplayName, entry.Key, entry.Label, at is { } t ? new PlacedVector(t.X, t.Y, t.Z) : new PlacedVector(0, 0, 0)),
                };
            }
            await OnMarkerPicked("thing", entry.Key);
            return true;
        }
        // A wall socket nobody has used yet has no entry in any list, but it can still be plugged
        // into: it opens as a power socket (the save writes its entry when something is plugged in).
        if (actor.StartsWith("PowerSocket", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"/Game/Maps/{map}.{map}:PersistentLevel.{actor}";
            var at = await Art.TryGetActorWorldTransformAsync(key);
            _things = new Dictionary<string, ThingInfo>(_things, StringComparer.Ordinal)
            {
                [key] = new ThingInfo("power-sockets", L.Resource("World3D_WallSocket"), key, L.Resource("World3D_WallSocket"),
                    at is { } t ? new PlacedVector(t.X, t.Y, t.Z) : new PlacedVector(0, 0, 0)),
            };
            await OnMarkerPicked("thing", key);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The level a listed thing stands in, when that is not this save's own area: the game keeps the
    /// thing's state (a socket's plugs) in that area's save, so it is changed there.
    /// </summary>
    private string? OtherAreaOf(ThingInfo thing)
    {
        var (map, _) = DoorIdParser.Parse(thing.Key);
        map = map[(map.LastIndexOf('/') + 1)..];
        return map.Length > 0 && LevelRegion is { } region && !string.Equals(map, region, StringComparison.OrdinalIgnoreCase)
               && HasOwnSave(map) ? map : null;
    }

    /// <summary>True when the workspace has a region save for this level.</summary>
    private bool HasOwnSave(string level)
        => System.IO.Path.GetDirectoryName(Session.Path) is { } dir && System.IO.File.Exists(System.IO.Path.Combine(dir, $"WorldSave_{level}.sav"));
}
