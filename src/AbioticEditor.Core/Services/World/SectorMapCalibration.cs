namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// How one cooked sub-level lines up with the in-game sector map that depicts it: an affine
/// from world units to fractions of the map texture (0,0 = top-left, 1,1 = bottom-right).
/// </summary>
/// <param name="PamphletRow">DT_MapPamphlets row holding the drawing, e.g. "Map_Office1".</param>
/// <param name="Variant">
/// Orientation applied before scaling: bits 0-1 rotate the world (X,Y) by 0/90/180/270
/// degrees, bit 2 flips X first.
/// </param>
/// <param name="ScaleX">Texture widths per oriented world unit.</param>
/// <param name="ScaleY">Texture heights per oriented world unit.</param>
/// <param name="OffsetX">Texture fraction the oriented world origin lands on, horizontally.</param>
/// <param name="OffsetY">Texture fraction the oriented world origin lands on, vertically.</param>
public sealed record SectorMapFit(
    string PamphletRow, int Variant, double ScaleX, double ScaleY, double OffsetX, double OffsetY);

/// <summary>
/// Projects world coordinates onto the game's drawn sector maps, so a door can be pinned on
/// the map a player recognises from the pamphlet they picked up in-game.
///
/// The game stores NO world bounds for these drawings - DT_MapPamphlets holds only a sector,
/// a level handle, an image and a strip flag - and the game never draws a "you are here"
/// marker on them, so there is nothing to read. Each fit below was solved offline by
/// <c>SectorMapCalibrationProbe.Solve_Fits</c>, which rasterises the drawn floor plan and
/// searches orientation, scale and offset for the placement that lands the most of the
/// level's actor cloud on the plan, then checked by eye against labelled landmarks
/// (restrooms, lifts, vending machines) on the drawing.
///
/// A level absent from <see cref="Fits"/> has no usable sector map and callers must fall back
/// to a plain relative plot. That covers most of the game, for three separate reasons:
/// <list type="bullet">
/// <item>Only 11 pamphlets exist for 77 cooked sub-levels.</item>
/// <item>Three of those drawings are useless - Secure Area reads "SITE MAP UNAVAILABLE FOR
/// SECURITY PURPOSES" and has no floor plan at all, Residence is a washed-out blank (the
/// game's own asset is even named "Map_ResidenceTerribleMap"), and the game ships
/// Map_Containment pointing at the Office Level 1 artwork.</item>
/// <item>Office Level 2 and the Dam refused to settle: every orientation scored within a few
/// percent of every other, and none put those levels' restrooms and lifts where the drawing
/// labels them. A pin that is confidently wrong is worse than no pin, so they are left out.</item>
/// </list>
/// </summary>
public static class SectorMapCalibration
{
    private static readonly Dictionary<string, SectorMapFit> Fits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Facility_Office1"] = new("Map_Office1", 1, 0.000035901, 0.000071801, 0.534224014, 0.66161589),
        ["Facility_Office3"] = new("Map_Office3", 1, 0.000030253, 0.000060506, 0.798243347, 0.980778989),
        ["Facility_Labs"] = new("Map_Lab", 1, 0.000028641, 0.000057281, 0.413090643, 0.873053716),
        ["Facility_MFWest"] = new("Map_MF", 3, 0.000028463, 0.000056927, 0.310796819, 1.037518411),
        ["Facility_Pens"] = new("Map_Pens", 6, 0.000038357, 0.000076713, 0.445498103, 0.831665721),
        ["Facility_DarkFusion"] = new("Map_Reactors", 2, 0.000016275, 0.000032549, 0.466219017, 0.032963468),
    };

    /// <summary>The calibrated fit for a cooked level, or null when it has no usable map.</summary>
    public static SectorMapFit? FitFor(string? levelFileName)
        => levelFileName is not null && Fits.TryGetValue(levelFileName, out var fit) ? fit : null;

    /// <summary>Every level the editor can pin on a drawn sector map.</summary>
    public static IReadOnlyDictionary<string, SectorMapFit> CalibratedLevels => Fits;

    /// <summary>Applies an orientation variant to a world position (top-down X/Y).</summary>
    public static (double X, double Y) ApplyVariant(double x, double y, int variant)
    {
        if ((variant & 4) != 0) x = -x;
        return (variant & 3) switch
        {
            1 => (-y, x),
            2 => (-x, -y),
            3 => (y, -x),
            _ => (x, y),
        };
    }

    /// <summary>
    /// Where a world position lands on the drawing, as fractions of the texture. Values
    /// outside 0..1 mean the spot sits off the edge of what the pamphlet draws.
    /// </summary>
    public static (double X, double Y) Project(SectorMapFit fit, double worldX, double worldY)
    {
        var (x, y) = ApplyVariant(worldX, worldY, fit.Variant);
        return (x * fit.ScaleX + fit.OffsetX, y * fit.ScaleY + fit.OffsetY);
    }

    /// <summary>Convenience overload for a resolved actor position.</summary>
    public static (double X, double Y) Project(SectorMapFit fit, DoorWorldLocation location)
        => Project(fit, location.X, location.Y);

    /// <summary>
    /// Percentile-trimmed bounds of an oriented point cloud (5th-95th), so a handful of
    /// far-away actors (skybox, parked props) can't stretch a fit being solved.
    /// </summary>
    public static (double MinX, double MaxX, double MinY, double MaxY) CloudBounds(
        IReadOnlyList<(double X, double Y)> points)
    {
        var xs = points.Select(p => p.X).OrderBy(v => v).ToArray();
        var ys = points.Select(p => p.Y).OrderBy(v => v).ToArray();
        double Pct(double[] sorted, double pct)
            => sorted[Math.Clamp((int)(pct * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return (Pct(xs, 0.05), Pct(xs, 0.95), Pct(ys, 0.05), Pct(ys, 0.95));
    }
}
