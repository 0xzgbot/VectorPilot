using System.Windows.Media.Media3D;
using VectorPilot.App.Controls;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Animated camera for the 3D preview (Aspire OSG camera row).
///
/// The preview shipped with a manual Rotate(degrees) only — a static orbit the user had
/// to nudge by hand, which is why the parity doc read "no camera animation". These tests
/// pin the viewpoint maths, which is the part that can be verified without a WPF
/// dispatcher pumping timer ticks.
/// </summary>
public class CameraAnimationTests
{
    private static PerspectiveCamera Cam(double x = 0, double y = -100, double z = 60)
        => new()
        {
            Position = new Point3D(x, y, z),
            LookDirection = new Vector3D(-x, -y, -z),
            FieldOfView = 45
        };

    private static double Dist(Point3D p)
        => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);

    [Fact]
    public void Top_View_Looks_Straight_Down()
    {
        var (pos, look) = ThreeDPreview.ViewpointVectors(CameraViewpoint.Top, Cam());

        Assert.Equal(0, pos.X, 6);
        Assert.Equal(0, pos.Y, 6);
        Assert.True(pos.Z > 0, "top view must sit above the model");
        Assert.Equal(-1, look.Z, 6);
    }

    [Fact]
    public void Front_View_Looks_Along_Y()
    {
        var (pos, look) = ThreeDPreview.ViewpointVectors(CameraViewpoint.Front, Cam());

        Assert.True(pos.Y < 0, "front view sits in front of the model");
        Assert.Equal(0, pos.Z, 6);
        Assert.Equal(1, look.Y, 6);
    }

    [Fact]
    public void Right_View_Looks_Along_X()
    {
        var (pos, look) = ThreeDPreview.ViewpointVectors(CameraViewpoint.Right, Cam());

        Assert.True(pos.X > 0);
        Assert.Equal(0, pos.Y, 6);
        Assert.Equal(-1, look.X, 6);
    }

    [Fact]
    public void Isometric_View_Is_Equidistant_On_All_Three_Axes()
    {
        var (pos, _) = ThreeDPreview.ViewpointVectors(CameraViewpoint.Isometric, Cam());

        Assert.Equal(Math.Abs(pos.X), Math.Abs(pos.Y), 3);
        Assert.Equal(Math.Abs(pos.Y), Math.Abs(pos.Z), 3);
    }

    [Fact]
    public void Changing_View_Preserves_The_Camera_Distance()
    {
        // A view change must not silently zoom.
        var cam = Cam(0, -120, 90);
        double before = Dist(cam.Position);

        foreach (var v in new[] { CameraViewpoint.Top, CameraViewpoint.Front,
                                  CameraViewpoint.Right, CameraViewpoint.Isometric })
        {
            var (pos, _) = ThreeDPreview.ViewpointVectors(v, cam);
            Assert.Equal(before, Dist(pos), 1);
        }
    }

    [Fact]
    public void Every_Viewpoint_Faces_The_Origin()
    {
        var cam = Cam();
        foreach (var v in new[] { CameraViewpoint.Top, CameraViewpoint.Front,
                                  CameraViewpoint.Right, CameraViewpoint.Isometric })
        {
            var (pos, look) = ThreeDPreview.ViewpointVectors(v, cam);

            // The look direction must point back toward the origin, i.e. oppose the
            // position vector.
            double dot = pos.X * look.X + pos.Y * look.Y + pos.Z * look.Z;
            Assert.True(dot < 0, $"{v} look direction points away from the model (dot={dot:F3})");
        }
    }

    [Fact]
    public void Look_Directions_Are_Unit_Length()
    {
        var cam = Cam();
        foreach (var v in new[] { CameraViewpoint.Top, CameraViewpoint.Front,
                                  CameraViewpoint.Right, CameraViewpoint.Isometric })
        {
            var (_, look) = ThreeDPreview.ViewpointVectors(v, cam);
            double len = Math.Sqrt(look.X * look.X + look.Y * look.Y + look.Z * look.Z);
            Assert.Equal(1.0, len, 2);
        }
    }

    [Fact]
    public void A_Camera_At_The_Origin_Does_Not_Divide_By_Zero()
    {
        var degenerate = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, 0),
            LookDirection = new Vector3D(0, 1, 0)
        };

        var (pos, _) = ThreeDPreview.ViewpointVectors(CameraViewpoint.Top, degenerate);
        Assert.True(Dist(pos) >= 1.0, "distance must be clamped away from zero");
    }

    [Fact]
    public void The_Four_Viewpoints_Are_Distinct()
    {
        var cam = Cam();
        var positions = new[] { CameraViewpoint.Top, CameraViewpoint.Front,
                                CameraViewpoint.Right, CameraViewpoint.Isometric }
            .Select(v => ThreeDPreview.ViewpointVectors(v, cam).Position)
            .ToList();

        for (int i = 0; i < positions.Count; i++)
            for (int j = i + 1; j < positions.Count; j++)
                Assert.True(Dist(new Point3D(
                        positions[i].X - positions[j].X,
                        positions[i].Y - positions[j].Y,
                        positions[i].Z - positions[j].Z)) > 1.0,
                    "two viewpoints resolve to the same camera position");
    }
}
