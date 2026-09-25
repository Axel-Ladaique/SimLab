using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class CameraDirectorTests
{
    sealed class FakeRig(double fov) : ICameraRig
    {
        public int Resets;
        public void Reset(in CameraContext ctx) => Resets++;
        public CameraPose Update(double dt, in CameraContext ctx) => new(Vec3.Zero, Vec3.UnitY, fov, Vec3.UnitZ);
    }

    static readonly CameraContext Ctx = new(Vec3.Zero, Quat.Identity, 1.5);

    [Fact]
    public void Next_cycles_ground_fpv_chase_and_resets_the_new_rig()
    {
        FakeRig ground = new(1), fpv = new(2), chase = new(3);
        var director = new CameraDirector(ground, fpv, chase, CameraView.Ground);
        Assert.Equal(1, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Next(Ctx);
        Assert.Equal(CameraView.Fpv, director.Current);
        Assert.Equal(1, fpv.Resets);
        Assert.Equal(2, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Next(Ctx);
        Assert.Equal(CameraView.Chase, director.Current);
        Assert.Equal(1, chase.Resets);
        director.Next(Ctx);
        Assert.Equal(CameraView.Ground, director.Current);
        Assert.Equal(1, ground.Resets);
    }

    [Fact]
    public void Select_jumps_to_a_view_and_reset_resets_only_the_current_one()
    {
        FakeRig ground = new(1), fpv = new(2), chase = new(3);
        var director = new CameraDirector(ground, fpv, chase, CameraView.Fpv);
        director.Select(CameraView.Chase, Ctx);
        Assert.Equal(3, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Reset(Ctx);
        Assert.Equal(2, chase.Resets);
        Assert.Equal(0, ground.Resets);
        Assert.Equal(0, fpv.Resets);
    }

    [Fact]
    public void Select_ignores_an_undefined_view_and_keeps_the_current_one()
    {
        FakeRig ground = new(1), fpv = new(2), chase = new(3);
        var director = new CameraDirector(ground, fpv, chase, CameraView.Fpv);
        director.Select((CameraView)7, Ctx);
        Assert.Equal(CameraView.Fpv, director.Current);
        Assert.Equal(0, fpv.Resets);
    }
}
