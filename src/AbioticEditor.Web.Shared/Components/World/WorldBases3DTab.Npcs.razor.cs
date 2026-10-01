using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Story characters, traders and pets of the open area as diamond markers in the 3D view. A click opens a
/// card naming the character with a link to the tab that edits it (the NPCs tab keeps a story character's
/// removal read-only, so the card does too). Holograms have no place in the world and are left out.
/// </summary>
public partial class WorldBases3DTab
{
    [Inject] private ItemCatalogService Items { get; set; } = null!;

    /// <summary>Opens another tab of the world editor by its id ("npcs", "pets").</summary>
    [Parameter] public EventCallback<string> OnOpenTab { get; set; }

    private bool _npcsOn = true;
    private bool _npcsDirty = true;
    private int _npcsToken;
    private string? _selectedNpcId;

    private WorldNpc? SelectedNpc => _selectedNpcId is null ? null
        : Session.Npcs.FirstOrDefault(n => string.Equals(n.Id, _selectedNpcId, StringComparison.Ordinal));

    internal static bool IsHologram(WorldNpc npc) => string.Equals(
        NpcIdentityCatalog.MatchedHint(npc.Id, npc.ActorName), "Human_Hologram", StringComparison.Ordinal);

    /// <summary>Marker colour: pets cyan, story characters violet, ones the story has removed dark red.</summary>
    internal static int NpcColor(WorldNpc npc) => npc.IsPet ? 0x40d8e0 : npc.IsDead ? 0x8a1c1c : 0xb57bff;

    private string NpcName(WorldNpc npc) => npc.IsPet ? npc.ActorName
        : Items.GetNarrativeNpcDisplayName(npc, Session.Npcs, (name, area) => L.Resource("WorldNpcs_NameWithAreaFormat", name, area)) ?? npc.FriendlyLabel;

    /// <summary>
    /// Sends the area's characters to the view: the saved position when there is one; for a story
    /// character placed in the level and never moved, where the level puts it.
    /// </summary>
    private async Task PushNpcsAsync()
    {
        if (_view is null) return;
        var token = ++_npcsToken;
        var markers = new List<object>();
        foreach (var npc in Session.Npcs)
        {
            if (IsHologram(npc)) continue;
            PlacedVector at;
            if (npc.X != 0 || npc.Y != 0 || npc.Z != 0) at = new PlacedVector(npc.X, npc.Y, npc.Z);
            else if (!npc.IsPet && npc.Id.StartsWith("/Game/", StringComparison.Ordinal)
                     && await Art.TryGetActorWorldTransformAsync(npc.Id) is { } level) at = new PlacedVector(level.X, level.Y, level.Z);
            else continue;
            if (token != _npcsToken || _disposed) return;
            var v = PlacedSceneSpace.ToViewer(at);
            markers.Add(new { id = npc.Id, p = new[] { v.X, v.Y, v.Z }, color = NpcColor(npc) });
        }
        if (token != _npcsToken || _disposed) return;
        try
        {
            await _view.InvokeVoidAsync("setNpcs", markers);
            await _view.InvokeVoidAsync("setNpcSelection", _selectedNpcId);
        }
        catch (JSDisconnectedException) { }
    }

    private async Task SetNpcsVisibleAsync(bool on)
    {
        _npcsOn = on;
        if (!on) _selectedNpcId = null;
        if (_view is null) return;
        await _view.InvokeVoidAsync("setNpcsVisible", on);
        await _view.InvokeVoidAsync("setNpcSelection", _selectedNpcId);
    }

    /// <summary>A click on a character's marker opens its card.</summary>
    [JSInvokable]
    public async Task OnNpcPicked(string id)
    {
        _selectedNpcId = id;
        ShowInspector();
        if (_view is not null) await _view.InvokeVoidAsync("setNpcSelection", id);
        StateHasChanged();
    }

    private async Task CloseNpcCardAsync()
    {
        _selectedNpcId = null;
        if (_view is not null) await _view.InvokeVoidAsync("setNpcSelection", (string?)null);
    }
}
