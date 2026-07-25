namespace AbioticEditor.Web.Models;

/// <summary>A door's dot on the sub-level mini-map, already projected into canvas space.</summary>
public readonly record struct MiniMapPoint(string Actor, double X, double Y, bool IsSelected);

/// <summary>
/// Projects every door's raw (X, Y) world position in a sub-level onto a small canvas: a
/// faithful port of the retired native app's <c>DoorMiniMapDrawable</c> math (fit the door
/// cloud's bounding box to the canvas, uniform scale on both axes so shapes aren't skewed).
/// This is deliberately NOT real level geometry, just a scatter plot that shows roughly where
/// in the sub-level each door sits relative to the others. Kept pure/static (no Blazor or
/// rendering dependency) so the projection math is unit-testable on its own.
/// </summary>
public static class DoorMiniMapLayout
{
    /// <summary>
    /// Lays out <paramref name="doors"/> within a <paramref name="width"/> x
    /// <paramref name="height"/> canvas, leaving <paramref name="margin"/> px on every side.
    /// Empty input yields an empty result. A single door (or every door sharing exactly the
    /// same X or Y) still lays out sensibly: <c>Math.Max(1, span)</c> keeps the scale finite.
    /// </summary>
    public static IReadOnlyList<MiniMapPoint> Layout(
        IReadOnlyList<(string Actor, double X, double Y)> doors,
        string? selectedActor,
        double width,
        double height,
        double margin = 14)
    {
        if (doors.Count == 0) return Array.Empty<MiniMapPoint>();

        var minX = doors.Min(d => d.X);
        var maxX = doors.Max(d => d.X);
        var minY = doors.Min(d => d.Y);
        var maxY = doors.Max(d => d.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min((width - margin * 2) / spanX, (height - margin * 2) / spanY);

        var result = new List<MiniMapPoint>(doors.Count);
        foreach (var (actor, x, y) in doors)
        {
            result.Add(new MiniMapPoint(
                actor,
                margin + (x - minX) * scale,
                margin + (y - minY) * scale,
                selectedActor is not null && string.Equals(actor, selectedActor, StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }
}
