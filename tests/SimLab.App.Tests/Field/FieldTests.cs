using SimLab.App.Field;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Field;

public class FieldTests
{
    [Fact]
    public void Sun_direction_follows_azimuth_and_elevation()
    {
        var south = SunMath.Direction(180, 0);
        Assert.Equal(0, south.X, 9);
        Assert.Equal(-1, south.Y, 9);
        Assert.Equal(1, SunMath.Direction(0, 90).Z, 9);
        Assert.Equal(1, SunMath.Direction(90, 0).X, 9);
    }

    [Fact]
    public void Windsock_points_downwind_and_hangs_in_calm_air()
    {
        var fromWest = Windsock.Pose(new Vec3(8, 0, 0));
        Assert.Equal(90, fromWest.HeadingDeg, 6);
        Assert.Equal(0, fromWest.DroopDeg, 6);
        Assert.Equal(90, Windsock.Pose(Vec3.Zero).DroopDeg, 6);
        Assert.InRange(Windsock.Pose(new Vec3(0, -3, 0)).DroopDeg, 40, 70);
        Assert.Equal(180, Windsock.Pose(new Vec3(0, -3, 0)).HeadingDeg, 6);
    }
}
