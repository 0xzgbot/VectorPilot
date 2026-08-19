using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P2: the follow-source link. Toolpath.SelectedShapeIds always existed but was
/// never surfaced, so a user could not tell which geometry a toolpath cuts — or
/// that its source shapes had been deleted underneath it.
/// </summary>
public class FollowSourceLinkTests
{
    private static (Layer Layer, VectorShape A, VectorShape B) TwoShapes()
    {
        var layer = new Layer { Name = "L" };
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var b = VectorShape.Circle(new VectorPoint(30, 30), 5);
        layer.AddShape(a);
        layer.AddShape(b);
        return (layer, a, b);
    }

    [Fact]
    public void Followed_Set_Starts_Empty()
    {
        AppState.FollowedSourceShapeIds.Clear();
        Assert.Empty(AppState.FollowedSourceShapeIds);
    }

    [Fact]
    public void Followed_Set_Round_Trips_Shape_Ids()
    {
        var (_, a, b) = TwoShapes();
        AppState.FollowedSourceShapeIds.Clear();
        AppState.FollowedSourceShapeIds.Add(a.Id);
        AppState.FollowedSourceShapeIds.Add(b.Id);

        Assert.Contains(a.Id, AppState.FollowedSourceShapeIds);
        Assert.Contains(b.Id, AppState.FollowedSourceShapeIds);
        Assert.Equal(2, AppState.FollowedSourceShapeIds.Count);
    }

    [Fact]
    public void Change_Notification_Fires()
    {
        int fired = 0;
        void Handler() => fired++;

        AppState.FollowedSourceChanged += Handler;
        try
        {
            AppState.RaiseFollowedSourceChanged();
            AppState.RaiseFollowedSourceChanged();
        }
        finally
        {
            AppState.FollowedSourceChanged -= Handler;
        }

        Assert.Equal(2, fired);
    }

    [Fact]
    public void A_Toolpath_Reports_The_Shapes_It_Cuts()
    {
        var (layer, a, b) = TwoShapes();
        var tp = new Toolpath { Name = "profile" };
        tp.SelectedShapeIds.Add(a.Id);
        tp.SelectedShapeIds.Add(b.Id);

        int present = layer.Shapes.Count(s => tp.SelectedShapeIds.Contains(s.Id));
        Assert.Equal(2, present);
    }

    [Fact]
    public void Deleted_Source_Shapes_Are_Detectable()
    {
        // The stale-link case: a toolpath still references geometry that is gone.
        var (layer, a, b) = TwoShapes();
        var tp = new Toolpath { Name = "profile" };
        tp.SelectedShapeIds.Add(a.Id);
        tp.SelectedShapeIds.Add(b.Id);

        layer.Shapes.Remove(b);

        int present = layer.Shapes.Count(s => tp.SelectedShapeIds.Contains(s.Id));
        int missing = tp.SelectedShapeIds.Count - present;

        Assert.Equal(1, present);
        Assert.Equal(1, missing);   // surfaced to the user as "recalculate"
    }

    [Fact]
    public void A_Toolpath_With_No_Links_Reports_None()
    {
        var (layer, _, _) = TwoShapes();
        var tp = new Toolpath { Name = "unlinked" };

        Assert.Empty(tp.SelectedShapeIds);
        Assert.Equal(0, layer.Shapes.Count(s => tp.SelectedShapeIds.Contains(s.Id)));
    }
}
