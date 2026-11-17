using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The Bases map's half of moving a base (GitHub issue 69): the chosen base's player-built pieces go
/// to the shared <see cref="BaseMovePanel"/>, which moves them to another world, exports them to a
/// file, or imports a base file into this world. The 3D view offers the same panel for any selection.
/// </summary>
public partial class WorldBasesTab
{
    private string? _baseMoveMode;
    private IReadOnlyList<string> _baseMoveKeys = [];
    private (string? Base, int Revision, List<string> Keys)? _movableCache;

    /// <summary>The selected base's pieces that a player built (the ones that can move).</summary>
    private List<string> MovableKeys(WorldSaveSession session)
    {
        var stamp = (_selectedBaseName, session.PlacedTransformsRevision);
        if (_movableCache is { } cached && cached.Base == stamp._selectedBaseName && cached.Revision == stamp.PlacedTransformsRevision) return cached.Keys;
        var keys = SelectedBase is { } b ? session.MovableKeysOf(b).ToList() : [];
        _movableCache = (stamp._selectedBaseName, stamp.PlacedTransformsRevision, keys);
        return keys;
    }

    [Inject] private BaseMoveWarning MoveWarning { get; set; } = null!;

    /// <summary>Opens the move, export or import tool once the player has read the experimental warning.</summary>
    private Task OpenBaseMove(string mode, IReadOnlyList<string> keys, bool live = false)
        => MoveWarning.ShowAsync(() => InvokeAsync(() =>
        {
            _baseMoveKeys = keys.ToList();
            _baseMoveMode = mode;
            StateHasChanged();
        }), live);

    private void CloseBaseMove()
    {
        _baseMoveMode = null;
        _baseMoveKeys = [];
    }
}
