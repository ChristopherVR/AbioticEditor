using AbioticEditor.Core.WorldSaves;
using Microsoft.Maui.Graphics;

namespace AbioticEditor.App.ViewModels;

/// <summary>One detected base for the base-manager list.</summary>
public sealed class WorldBaseViewModel
{
    public WorldBaseViewModel(WorldBase source)
    {
        Source = source;
    }

    public WorldBase Source { get; }
    public string Name => Source.Name;

    public string Summary =>
        $"{Source.BenchCount} bench(es) · {Source.Deployables.Count} deployables · "
        + $"{Source.ContainerCount} container(s) holding {Source.StoredItemCount} item(s)";

    /// <summary>The crafting bench(es) anchoring this base, named explicitly.</summary>
    public string AnchorText
    {
        get
        {
            var benches = Source.Deployables.Where(d => d.IsCraftingBench).ToList();
            return benches.Count == 0
                ? "No crafting bench - leftover deployables outside any base."
                : "Anchored by: " + string.Join(", ", benches
                    .GroupBy(b => b.DisplayName)
                    .Select(g => g.Count() > 1 ? $"{g.Count()}× {g.Key}" : g.Key));
        }
    }

    /// <summary>
    /// Every crafting bench in this base, individually: its player-given name (or class),
    /// position, and whether a teleporter could target it.
    /// </summary>
    public IReadOnlyList<BenchRow> Benches => Source.Deployables
        .Where(d => d.IsCraftingBench)
        .Select(d => new BenchRow(
            d.DisplayName,
            $"({d.X:F0}, {d.Y:F0}, {d.Z:F0}) · id {d.Id[..Math.Min(8, d.Id.Length)]}…"))
        .ToList();

    public bool HasBenches => Benches.Count > 0;

    /// <summary>Deployable classes grouped with counts - benches always listed first.</summary>
    public string Composition => string.Join("\n", Source.Deployables
        .GroupBy(d => d.DisplayName)
        .OrderByDescending(g => g.Any(d => d.IsCraftingBench))
        .ThenByDescending(g => g.Count())
        .Take(14)
        .Select(g => $"{g.Count()}× {g.Key}" + (g.Any(d => d.IsCraftingBench) ? "  [BENCH]" : "")));

    /// <summary>Container ids in this base (used to jump into the containers tab).</summary>
    public IReadOnlyList<string> ContainerIds => Source.Deployables
        .Where(d => d.HasInventory)
        .Select(d => d.Id)
        .ToList();
}

/// <summary>One crafting bench row in the base detail.</summary>
public sealed record BenchRow(string Name, string Detail);

/// <summary>
/// Top-down scatter map of every deployable in the world. Crafting benches draw
/// hazard-orange, containers phosphor-green, everything else muted; the selected base's
/// cluster radius is outlined.
/// </summary>
public sealed class BaseMapDrawable : IDrawable
{
    private readonly IReadOnlyList<WorldDeployable> _deployables;
    private readonly WorldBase? _selected;

    public BaseMapDrawable(IReadOnlyList<WorldDeployable> deployables, WorldBase? selected)
    {
        _deployables = deployables;
        _selected = selected;
    }

    // Category palette - keep in sync with the legend below.
    private static readonly Color BenchColor = Color.FromArgb("#F08418");
    private static readonly Color ContainerColor = Color.FromArgb("#8CCB58");
    private static readonly Color BedColor = Color.FromArgb("#B07CE8");
    private static readonly Color DefenseColor = Color.FromArgb("#D14A30");
    private static readonly Color PowerColor = Color.FromArgb("#56C4E8");
    private static readonly Color FarmColor = Color.FromArgb("#4FA84F");
    private static readonly Color OtherColor = Color.FromArgb("#5E5440");

    private static (Color Color, char Shape) Classify(WorldDeployable d)
    {
        var c = d.ClassName ?? string.Empty;
        bool Has(params string[] hints) => hints.Any(h => c.Contains(h, StringComparison.OrdinalIgnoreCase));

        if (d.IsCraftingBench) return (BenchColor, 'S');                       // square
        if (d.IsBed) return (BedColor, 'D');                                   // diamond
        if (Has("Turret", "Trap", "Barricade", "Tripwire", "Spikes", "Fence")) return (DefenseColor, 'T'); // triangle
        if (Has("Light", "Lamp", "Generator", "Power", "Charging", "Battery", "Solar", "Teleporter")) return (PowerColor, 'T');
        if (Has("GardenPlot", "Plant", "Sprinkler", "Compost")) return (FarmColor, 'C');
        if (d.HasInventory) return (ContainerColor, 'S');
        return (OtherColor, 'C');                                              // small circle
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#101810");
        canvas.FillRectangle(dirtyRect);
        if (_deployables.Count == 0) return;

        var minX = _deployables.Min(d => d.X);
        var maxX = _deployables.Max(d => d.X);
        var minY = _deployables.Min(d => d.Y);
        var maxY = _deployables.Max(d => d.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min((dirtyRect.Width - 36) / spanX, (dirtyRect.Height - 36) / spanY);

        float Px(double x) => (float)(18 + (x - minX) * scale);
        float Py(double y) => (float)(18 + (y - minY) * scale);

        // Faint blueprint grid for spatial reference.
        canvas.StrokeColor = Color.FromArgb("#1C271C");
        canvas.StrokeSize = 1;
        for (var gx = 0f; gx < dirtyRect.Width; gx += 48) canvas.DrawLine(gx, 0, gx, dirtyRect.Height);
        for (var gy = 0f; gy < dirtyRect.Height; gy += 48) canvas.DrawLine(0, gy, dirtyRect.Width, gy);

        // Selected base: cluster ring + soft fill so the active base pops.
        if (_selected is not null && _selected.Deployables.Count > 0)
        {
            var r = Math.Max(14, (float)(BaseDetector.ClusterRadius * scale));
            var cx = Px(_selected.CenterX);
            var cy = Py(_selected.CenterY);
            canvas.FillColor = Color.FromArgb("#22F5C518");
            canvas.FillCircle(cx, cy, r);
            canvas.StrokeColor = Color.FromArgb("#F5C518");
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(cx, cy, r);
        }

        // Draw non-selected dimmed first, selected base members on top at full strength.
        foreach (var pass in new[] { false, true })
        {
            foreach (var d in _deployables)
            {
                var inSelected = _selected is null || _selected.Deployables.Contains(d);
                if (inSelected != pass && _selected is not null) continue;
                if (_selected is null && pass) continue;

                var (color, shape) = Classify(d);
                var alpha = _selected is null || inSelected ? 1f : 0.28f;
                var x = Px(d.X);
                var y = Py(d.Y);
                canvas.FillColor = color.WithAlpha(alpha);

                switch (shape)
                {
                    case 'S':
                        var s = d.IsCraftingBench ? 5f : 3.5f + Math.Min(2.5f, d.StoredItemCount / 12f);
                        canvas.FillRectangle(x - s, y - s, s * 2, s * 2);
                        break;
                    case 'D':
                        var path = new PathF();
                        path.MoveTo(x, y - 5); path.LineTo(x + 5, y); path.LineTo(x, y + 5); path.LineTo(x - 5, y);
                        path.Close();
                        canvas.FillPath(path);
                        break;
                    case 'T':
                        var tri = new PathF();
                        tri.MoveTo(x, y - 4.5f); tri.LineTo(x + 4f, y + 3.5f); tri.LineTo(x - 4f, y + 3.5f);
                        tri.Close();
                        canvas.FillPath(tri);
                        break;
                    default:
                        canvas.FillCircle(x, y, 2f);
                        break;
                }

                // Benches get their name so the map reads like a base plan.
                if (d.IsCraftingBench && (inSelected || _selected is null))
                {
                    canvas.FontSize = 9;
                    canvas.FontColor = Color.FromArgb("#F0B068").WithAlpha(alpha);
                    var label = d.CustomName is { Length: > 0 } n ? n : d.FriendlyClass.Replace("CraftingBench ", "");
                    canvas.DrawString(label, x + 7, y - 10, 150, 14,
                        HorizontalAlignment.Left, VerticalAlignment.Top);
                }
            }
        }

        // Legend (bottom-left).
        var legend = new (Color C, string T)[]
        {
            (BenchColor, "bench"), (ContainerColor, "container"), (BedColor, "bed"),
            (DefenseColor, "defense"), (PowerColor, "power/tele"), (FarmColor, "farm"), (OtherColor, "other"),
        };
        var lx = 10f;
        var ly = dirtyRect.Height - 16;
        canvas.FontSize = 9;
        foreach (var (c, t) in legend)
        {
            canvas.FillColor = c;
            canvas.FillRectangle(lx, ly, 7, 7);
            canvas.FontColor = Color.FromArgb("#A89B7F");
            canvas.DrawString(t, lx + 10, ly - 2, 70, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
            lx += 14 + t.Length * 5.4f;
        }
    }
}
