using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class ChaseRigTests
{
    const double Span = 1.5;
    static readonly Vec3 Cg = new(0, 0, 50);

    static CameraContext At(double rollDeg, double pitchDeg, double headingDeg, Func<double, double, double>? ground = null) =>
        new(Cg, Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg)), Span, ground);

    /// <summary>Compass heading (deg, clockwise from north) the camera looks along, horizontally.</summary>
    static double CameraHeading(CameraPose p)
    {
        var d = p.LookAt - p.Position;
        double h = Angle.Deg(Math.Atan2(d.X, d.Y));
        return h < 0 ? h + 360 : h;
    }

    [Fact]
    public void Sits_behind_and_above_with_a_level_horizon_whatever_the_bank()
    {
        var rig = new ChaseRig();
        rig.Reset(At(60, 0, 0));
        var pose = rig.Update(0.016, At(60, 0, 0));
        Assert.Equal(Vec3.UnitZ, pose.Up);
        Assert.Equal(0, pose.Position.X, 9);
        Assert.Equal(Cg.Y - ChaseRig.DistanceSpans * Span, pose.Position.Y, 9);
        Assert.Equal(Cg.Z + ChaseRig.HeightSpans * Span, pose.Position.Z, 9);
        Assert.Equal(Cg, pose.LookAt);
        Assert.Equal(ChaseRig.FovDeg, pose.VerticalFovDeg);
    }

    [Fact]
    public void Follows_a_heading_change_with_a_lag()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 0));
        CameraPose pose = default;
        for (int i = 0; i < 6; i++) pose = rig.Update(1.0 / 60, At(0, 0, 90));
        Assert.InRange(CameraHeading(pose), 1, 45);
        for (int i = 0; i < 114; i++) pose = rig.Update(1.0 / 60, At(0, 0, 90));
        Assert.InRange(CameraHeading(pose), 89, 90);
    }

    [Fact]
    public void Turns_the_short_way_across_north()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 350));
        var pose = rig.Update(0.2, At(0, 0, 10));
        double h = CameraHeading(pose);
        Assert.True(h > 350 || h < 10, $"went the long way: {h}");
    }

    [Fact]
    public void Holds_the_last_heading_in_a_vertical_climb()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 90));
        CameraPose pose = default;
        for (int i = 0; i < 120; i++) pose = rig.Update(1.0 / 60, At(0, 89, 180));
        Assert.Equal(90, CameraHeading(pose), 6);
    }

    [Fact]
    public void Never_goes_below_the_ground()
    {
        var rig = new ChaseRig();
        var ctx = At(0, 0, 0, (_, _) => 100);
        rig.Reset(ctx);
        Assert.Equal(100 + ChaseRig.GroundClearance, rig.Update(0.016, ctx).Position.Z, 9);
    }

    [Fact]
    public void Reset_snaps_to_the_heading()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 0));
        rig.Reset(At(0, 0, 200));
        Assert.Equal(200, CameraHeading(rig.Update(0.016, At(0, 0, 200))), 6);
    }
}
