using SimLab.App.Cameras;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class FpvRigTests
{
    static readonly Vec3 Cg = new(10, 20, 30);
    static readonly FpvCameraSpec Mount = new(new Vec3(-0.5, 0, 0.1), 20, 110);

    static CameraPose Pose(double rollDeg, double pitchDeg, double headingDeg) =>
        new FpvRig(Mount).Update(0.016, new CameraContext(Cg,
            Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg)), 1.5));

    static Vec3 Look(CameraPose p) => (p.LookAt - p.Position).Normalized();

    [Fact]
    public void Level_heading_north_looks_north_tilted_up_by_the_uptilt()
    {
        var pose = Pose(0, 0, 0);
        var look = Look(pose);
        Assert.Equal(0, look.X, 9);
        Assert.Equal(Math.Cos(Angle.Rad(20)), look.Y, 9);
        Assert.Equal(Math.Sin(Angle.Rad(20)), look.Z, 9);
        Assert.Equal(0, Vec3.Dot(look, pose.Up), 9);
        Assert.True(pose.Up.Z > 0.9);
    }

    [Fact]
    public void Mount_is_carried_around_the_cg()
    {
        // Heading east: the body forward axis (−x) points east, so the mount 0.5 m forward is 0.5 m east of the CG.
        var pose = Pose(0, 0, 90);
        Assert.Equal(Cg.X + 0.5, pose.Position.X, 9);
        Assert.Equal(Cg.Y, pose.Position.Y, 9);
        Assert.Equal(Cg.Z + 0.1, pose.Position.Z, 9);
    }

    [Fact]
    public void Up_rolls_with_the_aircraft()
    {
        // 90° right bank heading north: body up points east (world +x), and the uptilt leans it back (south).
        var up = Pose(90, 0, 0).Up;
        Assert.Equal(Math.Cos(Angle.Rad(20)), up.X, 9);
        Assert.Equal(-Math.Sin(Angle.Rad(20)), up.Y, 9);
        Assert.Equal(0, up.Z, 9);
    }

    [Fact]
    public void Horizontal_fov_is_turned_into_the_vertical_one_for_the_screen()
    {
        double expected = Angle.Deg(2 * Math.Atan(Math.Tan(Angle.Rad(55)) / (16.0 / 9.0)));
        Assert.Equal(expected, FpvRig.VerticalFov(110, 16.0 / 9.0), 9);
        Assert.Equal(expected, Pose(0, 0, 0).VerticalFovDeg, 9);
    }
}
