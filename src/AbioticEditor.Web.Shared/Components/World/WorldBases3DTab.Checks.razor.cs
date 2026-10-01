using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Placement checks for moved, copied and placed pieces. The game puts a piece exactly where the save
/// says and never checks that it fits, so the view looks for the three ways that goes wrong: cutting
/// into the level, overlapping another piece, and nothing to hold it up. The geometry lives in the
/// viewer (base3d.js <c>checkPlacement</c>), so the checks run there and are shown here.
/// </summary>
public partial class WorldBases3DTab
{
    /// <summary>What the viewer found for one piece (see base3d.js <c>checkPlacement</c>).</summary>
    public sealed record PlacementCheck(string Key, bool LevelLoaded, bool Inside, string[] Overlaps, bool? Supported, double? GapM)
    {
        /// <summary>True when something looks wrong.</summary>
        public bool HasProblem => Inside || Overlaps.Length > 0 || Supported == false;
    }

    private const int MaxChecked = 60;
    private Dictionary<string, PlacementCheck> _checks = new(StringComparer.Ordinal);
    private bool _checksQueued;

    /// <summary>The pieces worth checking: staged moves and staged new pieces (copies and placed objects).</summary>
    private List<string> CheckedKeys()
    {
        var keys = Session.StagedPlacedTransforms.Select(t => t.Key).ToList();
        if (_basePreview is not null) keys.AddRange(_basePreview.Duplications.Where(r => !r.Blocked).Select(r => r.NewKey));
        return keys.Distinct(StringComparer.Ordinal).Take(MaxChecked).ToList();
    }

    /// <summary>Re-runs the checks after the scene or the level changed (at most one run in flight).</summary>
    private async Task RunPlacementChecksAsync()
    {
        if (_view is null || _checksQueued) return;
        _checksQueued = true;
        try
        {
            var keys = CheckedKeys();
            var results = keys.Count == 0 ? [] : await _view.InvokeAsync<PlacementCheck[]>("checkPlacement", keys);
            _checks = results.ToDictionary(c => c.Key, StringComparer.Ordinal);
            StateHasChanged();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or System.Text.Json.JsonException) { }
        finally { _checksQueued = false; }
    }

    private PlacementCheck? CheckOf(string key) => _checks.GetValueOrDefault(key);

    /// <summary>The warnings for one piece, in plain words (nothing when it looks fine or was not checked).</summary>
    private RenderFragment PlacementWarnings(string key) => __builder =>
    {
        if (CheckOf(key) is not { } check) return;
        var lines = new List<string>();
        if (check.Inside) lines.Add(L.Resource("World3D_CheckInside"));
        if (check.Overlaps.Length > 0) lines.Add(L.Resource("World3D_CheckOverlapsFormat", string.Join(", ", check.Overlaps)));
        if (check.Supported == false)
        {
            lines.Add(check.GapM is { } gap
                ? L.Resource("World3D_CheckFloatingFormat", gap.ToString("F1", System.Globalization.CultureInfo.CurrentCulture))
                : L.Resource("World3D_CheckNothingBelow"));
        }
        if (!check.LevelLoaded) lines.Add(L.Resource("World3D_CheckNeedsLevel"));
        var seq = 0;
        foreach (var line in lines)
        {
            __builder.OpenElement(seq++, "p");
            __builder.AddAttribute(seq++, "class", check.HasProblem && line != L.Resource("World3D_CheckNeedsLevel") ? "error-text b3d-warning" : "wt-muted");
            __builder.AddAttribute(seq++, "data-b3d", "placement-check");
            __builder.AddContent(seq++, line);
            __builder.CloseElement();
        }
    };

    /// <summary>The floor height (save Z, cm) under a save-space point, from the level in the view, or null.</summary>
    private async Task<double?> FloorUnderAsync(PlacedVector at)
    {
        if (_view is null) return null;
        try
        {
            var v = PlacedSceneSpace.ToViewer(at);
            var y = await _view.InvokeAsync<double?>("floorAt", new[] { v.X, v.Y, v.Z });
            return y is { } floor ? floor * 100 : null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException) { return null; }
    }

    /// <summary>Moves a staged (moved) piece down or up so it stands on the floor under it.</summary>
    private async Task StandOnFloorAsync(string key)
    {
        if (Session.CurrentPlacedTransform(key)?.Translation is not { } at || await FloorUnderAsync(at) is not { } floor) return;
        var result = Session.StagePlacedTransform(key, at with { Z = floor }, null);
        await AfterEditAsync(result);
    }
}
