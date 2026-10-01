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
    private string? _placeLastKey;
    private bool _placeOnFloor = true;

    private bool _placeScanning;
    private const string OtherWorldPrefix = "world|";

    /// <summary>Kinds built only in the player's other worlds (one entry per kind, the first world that has it).</summary>
    private List<WorldSaveSession.OtherWorldKind> OtherKinds
    {
        get
        {
            // Asked several times per render; computed once per state of the save and the edits.
            var stamp = (Session.PlacedObjects, Session.PlacedTransformsRevision, Session.OtherWorldKinds);
            if (_otherKindsCache is { } cached && cached.Stamp.Equals(stamp)) return cached.Kinds;
            var here = PlaceKinds.Select(k => k.ClassPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kinds = Session.OtherWorldKinds
                .Where(k => !here.Contains(k.ClassPath))
                .GroupBy(k => k.ClassPath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(k => k.Count).First())
                .OrderBy(k => Base3DScene.FriendlyClass(k.ClassName), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _otherKindsCache = (stamp, kinds);
            return kinds;
        }
    }

    private ((IReadOnlyList<PlacedObjectSummary>, int, IReadOnlyList<WorldSaveSession.OtherWorldKind>) Stamp, List<WorldSaveSession.OtherWorldKind> Kinds)? _otherKindsCache;
    private ((IReadOnlyList<PlacedObjectSummary>, int) Stamp, IReadOnlyList<Base3DPlaceKind> Kinds)? _placeKindsCache;

    private static string OtherKindValue(WorldSaveSession.OtherWorldKind k) => OtherWorldPrefix + k.World + "|" + k.ClassPath;

    private async Task LoadOtherWorldsAsync()
    {
        _placeScanning = true;
        StateHasChanged();
        await Session.LoadOtherWorldKindsAsync();
        _placeScanning = false;
        if (_placeClass is null && OtherKinds.Count > 0) _placeClass = OtherKindValue(OtherKinds[0]);
    }

    private IReadOnlyList<Base3DPlaceKind> PlaceKinds
    {
        get
        {
            var stamp = (Session.PlacedObjects, Session.PlacedTransformsRevision);
            if (_placeKindsCache is { } cached && cached.Stamp.Equals(stamp)) return cached.Kinds;
            var kinds = Base3DScene.PlaceKinds(Session.PlacedObjects, Session.StagedPlacedDeletions.Keys.ToHashSet(StringComparer.Ordinal));
            _placeKindsCache = (stamp, kinds);
            return kinds;
        }
    }

    private async Task OpenPlaceAsync()
    {
        _sideTab = "edit";
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
        var other = _placeClass?.StartsWith(OtherWorldPrefix, StringComparison.Ordinal) == true
            ? OtherKinds.FirstOrDefault(k => string.Equals(OtherKindValue(k), _placeClass, StringComparison.Ordinal))
            : null;
        var kind = other is null ? PlaceKinds.FirstOrDefault(k => string.Equals(k.ClassPath, _placeClass, StringComparison.OrdinalIgnoreCase)) : null;
        if (kind is null && other is null)
        {
            _placeError = L.Resource("World3D_PlaceNoKind");
            return;
        }
        if (!TryNumber(_placeX, out var x) || !TryNumber(_placeY, out var y) || !TryNumber(_placeZ, out var z) || !TryNumber(_placeYaw, out var yaw))
        {
            _placeError = L.Resource("World3D_InvalidNumber");
            return;
        }
        // Stand it on the floor under the spot (the view's level), unless asked not to.
        if (_placeOnFloor && await FloorUnderAsync(new PlacedVector(x, y, z)) is { } floor) z = floor;
        var staged = other is not null
            ? await Session.StagePlacedImportAsync(other, new PlacedVector(x, y, z), yaw)
            : Session.StagePlacedNew(kind!.DonorKey, new PlacedVector(x, y, z), yaw);
        if (staged is null)
        {
            _placeError = L.Resource("World3D_PlaceRefused");
            return;
        }
        _placeLastId = staged.Id;
        await AfterEditAsync(PlacedTransformStageResult.Ok);
        _placeLastKey = staged.NewKeys.Values.FirstOrDefault();
        if (_placeLastKey is { } newKey) await SetSelectionAsync([newKey], newKey, frame: false);
    }

    /// <summary>The findings for the object just placed (empty until one is placed).</summary>
    private IReadOnlyList<BaseEditIssue> LastPlaceIssues
        => _placeLastId is { } id && _basePreview?.Duplications.FirstOrDefault(r => r.DuplicationId == id) is { } row ? row.Issues : [];
}
