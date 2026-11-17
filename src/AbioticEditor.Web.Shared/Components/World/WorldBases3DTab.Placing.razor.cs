using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Putting a moved base down (the "choose the spot" step of a base move): the view shows the world the
/// base is going to, with the level around it, and the moving base staged there as a copy. The player
/// drags it, turns it, or clicks where it should go; after every change it is stood on the floor there
/// (<see cref="BaseFloorFit"/>) and checked against the level, and anything wrong is said in the bar on
/// top. Nothing is written: the host reads the copy's place when the player is done.
/// </summary>
public partial class WorldBases3DTab
{
    /// <summary>The staged copy being put down; set by a host showing this view to choose a spot.</summary>
    [Parameter] public int? PlacingCopyId { get; set; }

    /// <summary>Where the base is going, in words (the bar says it).</summary>
    [Parameter] public string? PlacingWhere { get; set; }

    /// <summary>The player is done (true: put it here) or gave up (false).</summary>
    [Parameter] public EventCallback<bool> OnPlacingDone { get; set; }

    private bool _placingStarted;
    private bool _placingBusy;
    private bool _placingPick;
    private BaseFloorFitResult? _placingFit;

    private bool Placing => PlacingCopyId is not null;

    private StagedDuplication? PlacingCopy
        => PlacingCopyId is int id ? Session.StagedPlacedDuplications.FirstOrDefault(d => d.Id == id) : null;

    private List<string> PlacingKeys() => PlacingCopy?.NewKeys.Values.ToList() ?? [];

    /// <summary>Starts once the first scene is in: picks the base, frames it, waits for the level there and stands it on the floor.</summary>
    private void StartPlacingWhenReady()
    {
        if (_placingStarted || !Placing || _view is null || !_framedOnce || PlacingCopy is null) return;
        _placingStarted = true;
        _ = InvokeAsync(async () =>
        {
            try
            {
                if (!_moveOptIn) await SetMoveOptInAsync(true);
                var keys = PlacingKeys();
                await SetSelectionAsync(keys, keys.FirstOrDefault(), frame: true);
                await FitPlacingWhenLoadedAsync();
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException) { }
        });
    }

    /// <summary>Lets the level around the base load (at most 15 seconds), then stands the base on it.</summary>
    private async Task FitPlacingWhenLoadedAsync()
    {
        _placingBusy = true;
        StateHasChanged();
        try
        {
            if (_view is not null && _levelOn) await _view.InvokeAsync<bool>("followLevel");
            var until = DateTime.UtcNow.AddSeconds(15);
            await Task.Delay(400);
            while (DateTime.UtcNow < until && !_disposed && LoadingLines().Count > 0) await Task.Delay(250);
            await FitPlacingAsync();
        }
        finally
        {
            _placingBusy = false;
            StateHasChanged();
        }
    }

    private async Task FitPlacingAsync()
    {
        if (PlacingCopy is not { } copy) return;
        _placingFit = await Session.FitStagedCopyToFloorAsync(copy.Id, FloorUnderAsync);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }

    /// <summary>After the base was dragged or turned: stand it on the floor where it is now.</summary>
    private async Task AfterPlacingMovedAsync(StagedDuplication copy)
    {
        if (!Placing || copy.Id != PlacingCopyId) return;
        await FitPlacingWhenLoadedAsync();
    }

    private async Task TurnPlacingAsync(double degrees)
    {
        if (PlacingCopy is { } copy) await TurnCopyAsync(copy, degrees);
    }

    /// <summary>The next click on the view says where the middle of the base goes.</summary>
    private async Task ArmPlacingPickAsync()
    {
        if (_view is null) return;
        _placingPick = !_placingPick;
        try { await _view.InvokeVoidAsync("armPointPick", _placingPick); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException) { _placingPick = false; }
    }

    /// <summary>A click on the view while "click where it goes" was on.</summary>
    [JSInvokable]
    public async Task OnPointPicked(double[] point)
    {
        _placingPick = false;
        if (point is not { Length: 3 }) return;
        await PutPlacingAtAsync(PlacedSceneSpace.FromViewer(new PlacedVector(point[0], point[1], point[2])));
        StateHasChanged();
    }

    /// <summary>Puts the middle of the base where the view is looking.</summary>
    private async Task PlacingToViewAsync()
    {
        if (await ViewPointAsync() is { } at) await PutPlacingAtAsync(at);
    }

    /// <summary>Moves the base so its middle stands at <paramref name="at"/> (its floor at that point's height).</summary>
    private async Task PutPlacingAtAsync(PlacedVector at)
    {
        if (PlacingCopy is not { } copy) return;
        var now = (_basePreview?.Duplications ?? [])
            .Where(r => r.DuplicationId == copy.Id && r.After?.Translation is not null)
            .Select(r => r.After!.Translation!.Value).ToList();
        if (now.Count == 0) return;
        var zs = now.Select(p => p.Z).Order().ToList();
        var floor = zs[(zs.Count - 1) / 4];
        await MoveCopyAsync(copy, at.X - now.Average(p => p.X), at.Y - now.Average(p => p.Y), at.Z - floor);
    }

    /// <summary>What is wrong with the base where it is now, in plain words (nothing when it looks fine).</summary>
    private List<string> PlacingProblems()
    {
        var lines = new List<string>();
        if (_placingFit is { NoLevel: true }) lines.Add(L.Resource("BasePlace_NoLevel"));
        if (_placingFit is { OverDrop.Count: > 0 } fit) lines.Add(L.Resource("BaseMove_OverDropFormat", fit.OverDrop.Count));
        var keys = PlacingKeys();
        var checks = keys.Select(CheckOf).Where(c => c is not null).Select(c => c!).ToList();
        var inside = checks.Count(c => c.Inside);
        var floating = checks.Count(c => c.Supported == false);
        var overlapping = checks.Count(c => c.Overlaps.Length > 0);
        if (inside > 0) lines.Add(L.Resource("BasePlace_InsideFormat", inside));
        if (floating > 0) lines.Add(L.Resource("BasePlace_FloatingFormat", floating));
        if (overlapping > 0) lines.Add(L.Resource("BasePlace_OverlapFormat", overlapping));
        return lines;
    }
}
