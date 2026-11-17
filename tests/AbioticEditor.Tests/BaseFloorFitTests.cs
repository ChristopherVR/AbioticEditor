using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

/// <summary>
/// Standing a moved base on the floor at its new place (BaseFloorFit). Found in the game: a base put
/// down by the Reactor terminal stood partly over a drop and pieces were put on the bottom of it, out
/// of reach. The floor is a fake here: a function of (x, y), looked up from above.
/// </summary>
public class BaseFloorFitTests
{
    private static Func<PlacedVector, Task<double?>> Floor(Func<double, double, double?> heightAt)
        => at => Task.FromResult(heightAt(at.X, at.Y) is { } h && h <= at.Z ? h : (double?)null);

    private static BaseFloorPiece Piece(string key, double x, double y, double z) => new(key, new PlacedVector(x, y, z));

    [Fact]
    public async Task A_base_on_a_flat_floor_stands_on_it_and_keeps_its_shape()
    {
        // Floor at 500; the base arrived 300 too high, with a lamp 200 up a wall.
        var pieces = new[] { Piece("bench", 0, 0, 800), Piece("crate", 100, 0, 800), Piece("lamp", 0, 100, 1000), Piece("bed", 100, 100, 800) };
        var fit = await BaseFloorFit.FitAsync(pieces, Floor((_, _) => 500));

        Assert.Equal(500, fit.Floor);
        Assert.Empty(fit.OverDrop);
        Assert.Equal(4, fit.Stood);
        Assert.Equal(500, fit.Heights["bench"]);
        Assert.Equal(500, fit.Heights["crate"]);
        Assert.Equal(700, fit.Heights["lamp"]); // stays on the wall, 200 above the floor
    }

    [Fact]
    public async Task Pieces_over_a_drop_are_reported_and_not_put_on_the_bottom()
    {
        // A walkway at 1000 for x < 300, a pit floor at -2000 beyond it.
        var pieces = new[] { Piece("a", 0, 0, 1000), Piece("b", 100, 0, 1000), Piece("c", 200, 0, 1000), Piece("over", 400, 0, 1000) };
        var fit = await BaseFloorFit.FitAsync(pieces, Floor((x, _) => x < 300 ? 1000 : -2000));

        Assert.Equal(1000, fit.Floor);
        Assert.Equal(["over"], fit.OverDrop);
        Assert.Equal(1000, fit.Heights["over"]); // level with the rest, never down in the pit
    }

    [Fact]
    public async Task The_floor_most_pieces_share_wins_over_a_hole_under_the_middle()
    {
        // A small hole right where the middle piece stands.
        var pieces = new[] { Piece("w", -200, 0, 0), Piece("mid", 0, 0, 0), Piece("e", 200, 0, 0), Piece("n", 0, 200, 0) };
        var fit = await BaseFloorFit.FitAsync(pieces, Floor((x, y) => x == 0 && y == 0 ? -1500 : 0));

        Assert.Equal(0, fit.Floor);
        Assert.Equal(["mid"], fit.OverDrop);
    }

    [Fact]
    public async Task A_small_step_in_the_floor_is_followed()
    {
        var pieces = new[] { Piece("low", 0, 0, 0), Piece("low2", 100, 0, 0), Piece("step", 200, 0, 0) };
        var fit = await BaseFloorFit.FitAsync(pieces, Floor((x, _) => x >= 200 ? 30 : 0));

        Assert.Empty(fit.OverDrop);
        Assert.Equal(30, fit.Heights["step"]);
    }

    [Fact]
    public async Task No_level_loaded_changes_nothing()
    {
        var fit = await BaseFloorFit.FitAsync([Piece("a", 0, 0, 0)], _ => Task.FromResult<double?>(null));

        Assert.True(fit.NoLevel);
        Assert.Empty(fit.Heights);
    }
}
