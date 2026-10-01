using System.Globalization;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Placing a new object (under the Experimental opt-in): the player picks a kind of object already built
/// in this save and a spot (the point the view is looking at, or typed coordinates), and a copy of one of
/// those objects is staged there. It is the duplicate operation with one source and an offset to the
/// chosen spot, so the copy carries every member the game wrote for that kind (nothing is made up), gets
/// fresh ids, starts empty and is unplugged. Nothing here writes a save.
/// </summary>
public partial class WorldBases3DTab
{
    private bool _placeOpen;
    private string? _placeClass;
    private string _placeX = "", _placeY = "", _placeZ = "", _placeYaw = "0";
    private string? _placeError;
    private int? _placeLastId;

    private IReadOnlyList<Base3DPlaceKind> PlaceKinds
        => Base3DScene.PlaceKinds(Session.PlacedObjects, Session.StagedPlacedDeletions.Keys.ToHashSet(StringComparer.Ordinal));

    private async Task OpenPlaceAsync()
    {
        _placeOpen = true;
        _placeError = null;
        _placeLastId = null;
        var kinds = PlaceKinds;
        _placeClass ??= kinds.Count > 0 ? kinds[0].ClassPath : null;
        await UseViewCentreAsync();
    }

    private void ClosePlace()
    {
        _placeOpen = false;
        _placeError = null;
    }

    /// <summary>Fills the spot with the point under the middle of the view (the floor or object looked at).</summary>
    private async Task UseViewCentreAsync()
    {
        if (_view is null) return;
        try
        {
            var p = await _view.InvokeAsync<double[]>("placementPoint");
            if (p is not { Length: 3 }) return;
            var save = PlacedSceneSpace.FromViewer(new PlacedVector(p[0], p[1], p[2]));
            _placeX = Num(save.X);
            _placeY = Num(save.Y);
            _placeZ = Num(save.Z);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException) { }
    }

    private async Task ConfirmPlaceAsync()
    {
        _placeError = null;
        var kind = PlaceKinds.FirstOrDefault(k => string.Equals(k.ClassPath, _placeClass, StringComparison.OrdinalIgnoreCase));
        if (kind is null)
        {
            _placeError = L.Resource("World3D_PlaceNoKind");
            return;
        }
        if (!TryNumber(_placeX, out var x) || !TryNumber(_placeY, out var y) || !TryNumber(_placeZ, out var z) || !TryNumber(_placeYaw, out var yaw))
        {
            _placeError = L.Resource("World3D_InvalidNumber");
            return;
        }
        if (Session.StagePlacedNew(kind.DonorKey, new PlacedVector(x, y, z), yaw) is not { } staged)
        {
            _placeError = L.Resource("World3D_PlaceRefused");
            return;
        }
        _placeLastId = staged.Id;
        await AfterEditAsync(PlacedTransformStageResult.Ok);
        if (staged.NewKeys.TryGetValue(kind.DonorKey, out var newKey)) await SetSelectionAsync([newKey], newKey, frame: false);
    }

    /// <summary>The findings for the object just placed (empty until one is placed).</summary>
    private IReadOnlyList<BaseEditIssue> LastPlaceIssues
        => _placeLastId is { } id && _basePreview?.Duplications.FirstOrDefault(r => r.DuplicationId == id) is { } row ? row.Issues : [];
}
