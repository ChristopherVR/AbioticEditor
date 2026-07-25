using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="DoorMiniMapLayout"/>: the pure projection math behind the Blazor door
/// mini-map (a port of the retired native app's <c>DoorMiniMapDrawable</c>). No game install or
/// save fixture needed since this is just geometry over caller-supplied points.
/// </summary>
public sealed class DoorMiniMapLayoutTests
{
    [Fact]
    public void Empty_input_yields_empty_output()
    {
        var result = DoorMiniMapLayout.Layout(
            Array.Empty<(string Actor, double X, double Y)>(), "AnyActor", 200, 120);
        Assert.Empty(result);
    }

    [Fact]
    public void Points_fit_within_canvas_bounds_and_selected_flag_is_set()
    {
        var doors = new (string Actor, double X, double Y)[]
        {
            ("DoorA", -500, 1000),
            ("DoorB", 1500, -200),
            ("DoorC", 0, 0),
        };

        var result = DoorMiniMapLayout.Layout(doors, "DoorB", width: 200, height: 120, margin: 14);

        Assert.Equal(3, result.Count);
        foreach (var point in result)
        {
            Assert.InRange(point.X, 0, 200);
            Assert.InRange(point.Y, 0, 120);
        }

        var selected = Assert.Single(result, p => p.IsSelected);
        Assert.Equal("DoorB", selected.Actor);
        Assert.DoesNotContain(result, p => p.Actor != "DoorB" && p.IsSelected);
    }

    [Fact]
    public void Selected_actor_match_is_case_insensitive()
    {
        var doors = new (string Actor, double X, double Y)[] { ("SimpleDoor_ParentBP_C_12", 10, 10) };
        var result = DoorMiniMapLayout.Layout(doors, "simpledoor_parentbp_c_12", 200, 120);
        Assert.True(Assert.Single(result).IsSelected);
    }

    [Fact]
    public void Single_door_or_degenerate_span_does_not_throw_or_produce_nan()
    {
        // Every door sharing the same X (a vertical corridor of doors, say) must not divide by
        // zero on that axis: DoorMiniMapLayout.Layout clamps each span to at least 1.
        var doors = new (string Actor, double X, double Y)[]
        {
            ("DoorA", 100, 100),
            ("DoorB", 100, 200),
            ("DoorC", 100, 300),
        };

        var result = DoorMiniMapLayout.Layout(doors, "DoorA", 200, 120);

        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y)));
    }

    [Fact]
    public void No_selected_actor_leaves_every_point_unselected()
    {
        var doors = new (string Actor, double X, double Y)[] { ("DoorA", 0, 0), ("DoorB", 10, 10) };
        var result = DoorMiniMapLayout.Layout(doors, selectedActor: "DoorZ", 200, 120);
        Assert.DoesNotContain(result, p => p.IsSelected);
    }
}
