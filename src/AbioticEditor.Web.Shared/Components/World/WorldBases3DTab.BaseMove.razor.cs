using AbioticEditor.Web.Services;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Moving a base from the 3D view (GitHub issue 69): the selection can be moved to another world or
/// exported to a base file, and a base file can be imported here. The work happens in the shared
/// <see cref="BaseMovePanel"/>; this half opens it, hands it the selection and the view's "where I'm
/// looking" point, and lets the pieces of a staged copy (an imported base, or any copy) be dragged
/// and turned in the view as one group.
/// </summary>
public partial class WorldBases3DTab
{
    private string? _baseMoveMode;
    private IReadOnlyList<string> _baseMoveKeys = [];
    private BaseMovePanel? _baseMovePanel;

    /// <summary>The player-built pieces of the selection (what a move or export would carry).</summary>
    private int MovableSelectionCount
        => _selected.Count(k => Session.FindPlacedObject(k) is { DeployedByPlayer: true });

    [Inject] private BaseMoveWarning MoveWarning { get; set; } = null!;

    /// <summary>Opens the move, export or import tool once the player has read the experimental warning.</summary>
    private Task OpenBaseMoveAsync(string mode) => MoveWarning.ShowAsync(() => InvokeAsync(async () =>
    {
        await OpenBaseMoveNowAsync(mode);
        StateHasChanged();
    }));

    private async Task OpenBaseMoveNowAsync(string mode)
    {
        if (!_moveOptIn) await SetMoveOptInAsync(true);
        CloseOperationPanels();
        _placeOpen = false;
        _baseMoveKeys = mode == BaseMovePanel.ImportMode ? [] : _selected.ToList();
        _baseMoveMode = mode;
        ShowInspector();
    }

    private Task SwitchBaseMoveAsync(string mode)
    {
        _baseMoveMode = mode;
        return Task.CompletedTask;
    }

    private void CloseBaseMove()
    {
        _baseMoveMode = null;
        _baseMoveKeys = [];
    }

    private Task BaseMoveStagedAsync() => AfterEditAsync(PlacedTransformStageResult.Ok);

    /// <summary>The new pieces are picked, so dragging one (or pressing R) moves or turns them all.</summary>
    private async Task BaseImportedAsync(IReadOnlyList<string> newKeys)
    {
        if (newKeys.Count > 0) await SetSelectionAsync(newKeys, newKeys[0], frame: true);
    }

    /// <summary>The point under the middle of the view, in save space, for "where I'm looking".</summary>
    private async Task<PlacedVector?> ViewPointAsync()
    {
        if (_view is null) return null;
        try
        {
            var p = await _view.InvokeAsync<double[]>("placementPoint");
            return p is { Length: 3 } ? PlacedSceneSpace.FromViewer(new PlacedVector(p[0], p[1], p[2])) : null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            return null;
        }
    }

    /// <summary>Selects every piece of the base chosen in the Bases tab (the pieces a move would usually carry).</summary>
    private Task SelectWholeBaseAsync()
    {
        if (FocusBase is null) return Task.CompletedTask;
        // The base's main floor: pieces on a level far above or below are not part of what moves.
        var keys = Session.MovableKeysOf(FocusBase).ToList();
        return SetSelectionAsync(keys, keys.FirstOrDefault(), frame: true);
    }

    // ---- dragging and turning a staged copy -----------------------------------------------------

    /// <summary>The staged copy the whole selection belongs to, when every selected key is one of its new pieces.</summary>
    private StagedDuplication? SelectedStagedCopy => Session.StagedCopyOfAll(_selected);

    /// <summary>Moves a staged copy by a step (cm) and tells an open move panel where it went.</summary>
    private async Task MoveCopyAsync(StagedDuplication copy, double dx, double dy, double dz)
    {
        Session.MoveStagedCopy(copy.Id, dx, dy, dz);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
        if (_baseMovePanel is not null) await _baseMovePanel.SyncFromStagedAsync();
        await AfterPlacingMovedAsync(copy);
    }

    private async Task TurnCopyAsync(StagedDuplication copy, double degrees)
    {
        Session.TurnStagedCopy(copy.Id, degrees);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
        if (_baseMovePanel is not null) await _baseMovePanel.SyncFromStagedAsync();
        await AfterPlacingMovedAsync(copy);
    }
}
