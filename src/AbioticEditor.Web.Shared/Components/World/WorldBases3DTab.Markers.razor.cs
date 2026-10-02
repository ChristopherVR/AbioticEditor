using AbioticEditor.Core.WorldSaves;
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

    private const int MaxThings = 600;
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
        var wanted = ThingFeatures
            .Select(id => Session.MapFeature(id))
            .Where(f => f is not null)
            .SelectMany(f => f!.Entries
                .Where(e => e.Key.StartsWith("/Game/", StringComparison.Ordinal))
                .Select(e => (Feature: f!, Entry: e)))
            .Take(MaxThings)
            .ToList();
        using var gate = new SemaphoreSlim(8);
        var found = await Task.WhenAll(wanted.Select(async w =>
        {
            await gate.WaitAsync();
            try { return (w.Feature, w.Entry, At: await Art.TryGetActorWorldTransformAsync(w.Entry.Key)); }
            finally { gate.Release(); }
        }));
        if (token != _markersToken || _disposed || _view is null) return;
        var things = new Dictionary<string, ThingInfo>(StringComparer.Ordinal);
        var markers = new List<object>();
        foreach (var (feature, entry, at) in found)
        {
            if (at is not { } t) continue;
            things[entry.Key] = new ThingInfo(feature.Id, feature.DisplayName, entry.Key, entry.Label, new PlacedVector(t.X, t.Y, t.Z));
            var v = PlacedSceneSpace.ToViewer(new PlacedVector(t.X, t.Y, t.Z));
            markers.Add(new { id = entry.Key, p = new[] { v.X, v.Y, v.Z }, color = ThingColor(feature.Id) });
        }
        _things = things;
        try
        {
            await _view.InvokeVoidAsync("setMarkers", "thing", markers);
            await _view.InvokeVoidAsync("setMarkersVisible", "thing", _thingsOn);
        }
        catch (JSDisconnectedException) { }
    }

    private static int ThingColor(string featureId) => featureId switch
    {
        "buttons" => 0x4fc3f7,
        "power-sockets" => 0xffd23f,
        "destructibles" => 0xff7043,
        "resource-nodes" => 0x81c784,
        "elevators" or "trams" or "portals" => 0xba68c8,
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
}
