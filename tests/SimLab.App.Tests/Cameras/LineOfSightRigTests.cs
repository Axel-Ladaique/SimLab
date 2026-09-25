using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class LineOfSightRigTests
{
    static readonly Vec3 Eye = new(0, -25, 1.7);

    static CameraContext At(Vec3 p) => new(p, Quat.Identity, 1.5);

    static Vec3 LookDirection(CameraPose pose) => (pose.LookAt - pose.Position).Normalized();

    [Fact]
    public void Camera_sits_at_the_eye_and_converges_on_the_aircraft()
    {
        var rig = new LineOfSightRig(Eye, 50, autoZoom: false);
        var target = new Vec3(40, 30, 20);
        rig.Reset(At(target));
        CameraPose pose = default;
        for (int i = 0; i < 60; i++) pose = rig.Update(1.0 / 60, At(target));
        Assert.Equal(Eye, pose.Position);
        var expected = (target - Eye).Normalized();
        Assert.True(Vec3.Dot(LookDirection(pose), expected) > 0.9999);
        Assert.Equal(50, pose.VerticalFovDeg);
        Assert.Equal(Vec3.UnitZ, pose.Up);
    }

    [Fact]
    public void Head_lags_behind_a_sudden_jump()
    {
        var rig = new LineOfSightRig(Eye, 50, autoZoom: false);
        rig.Reset(At(new Vec3(0, 50, 20)));
        var pose = rig.Update(0.016, At(new Vec3(80, -25, 20)));
        var toNew = (new Vec3(80, -25, 20) - Eye).Normalized();
        double dot = Vec3.Dot(LookDirection(pose), toNew);
        Assert.True(dot < 0.99, $"no lag: {dot}");
    }

    [Fact]
    public void Auto_zoom_narrows_the_view_for_far_aircraft_but_only_three_times()
    {
        var rig = new LineOfSightRig(Eye, 60, autoZoom: true);
        Assert.Equal(60, rig.ZoomedFov(10, 1.5), 9);
        Assert.Equal(20, rig.ZoomedFov(400, 1.5), 9);
        double mid = rig.ZoomedFov(60, 1.5);
        Assert.InRange(mid, 20, 60);
    }

    [Fact]
    public void Auto_zoom_off_keeps_the_true_apparent_size()
    {
        var rig = new LineOfSightRig(Eye, 45, autoZoom: false);
        rig.Reset(At(new Vec3(0, 300, 50)));
        Assert.Equal(45, rig.Update(0.016, At(new Vec3(0, 300, 50))).VerticalFovDeg);
    }

    [Fact]
    public void Fov_matches_screen_geometry()
        => Assert.Equal(28.07, LineOfSightRig.FovForScreen(30, 60), 2);
}
