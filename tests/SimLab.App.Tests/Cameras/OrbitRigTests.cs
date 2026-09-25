using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class OrbitRigTests
{
    [Fact]
    public void Orbits_at_a_fixed_distance_and_height_looking_at_the_aircraft()
    {
        var rig = new OrbitRig(fovDeg: 50);
        var ctx = new CameraContext(new Vec3(10, 20, 1), Quat.Identity, 1.5);
        rig.Reset(ctx);
        var a = rig.Update(0, ctx);
        var b = rig.Update(2, ctx);
        foreach (var pose in new[] { a, b })
        {
            var d = pose.Position - ctx.AircraftPosition;
            Assert.Equal(OrbitRig.RadiusSpans * 1.5, Math.Sqrt(d.X * d.X + d.Y * d.Y), 6);
            Assert.Equal(OrbitRig.HeightSpans * 1.5, d.Z, 6);
            Assert.Equal(ctx.AircraftPosition, pose.LookAt);
            Assert.Equal(50, pose.VerticalFovDeg);
        }
        var da = a.Position - ctx.AircraftPosition;
        var db = b.Position - ctx.AircraftPosition;
        double turned = Math.Acos(Vec3.Dot(new Vec3(da.X, da.Y, 0).Normalized(), new Vec3(db.X, db.Y, 0).Normalized())) * 180 / Math.PI;
        Assert.Equal(2 * OrbitRig.DegreesPerSecond, turned, 3);
    }
}
