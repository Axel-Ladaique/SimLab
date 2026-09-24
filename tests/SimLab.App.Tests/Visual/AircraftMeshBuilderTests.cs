using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Visual;

public class AircraftMeshBuilderTests
{
    static IReadOnlyList<MeshPart> Parts(string id)
    {
        var def = TestData.Aircraft(id);
        return AircraftMeshBuilder.Build(def, new Aircraft(def).Aero.Segments);
    }

    static Vec3 Centroid(IReadOnlyList<Vec3> v) => v.Aggregate(Vec3.Zero, (a, b) => a + b) / v.Count;

    static Vec3 RotateAboutHinge(MeshPart part, Vec3 p, double angle) =>
        part.HingePoint + Quat.FromAxisAngle(part.HingeAxis, angle).Rotate(p - part.HingePoint);

    [Theory]
    [InlineData("trainer", new[] { "airframe", "aileronRight", "aileronLeft", "elevator", "rudder", "fuselage", "gear", "propeller" })]
    [InlineData("wing", new[] { "airframe", "elevonRight", "elevonLeft", "fuselage", "propeller" })]
    public void Builds_one_part_per_control_plus_fixed_parts(string id, string[] names)
    {
        var parts = Parts(id);
        Assert.Equal(names.OrderBy(n => n), parts.Select(p => p.Name).OrderBy(n => n));
        Assert.All(parts, p =>
        {
            Assert.True(p.Triangles.Count > 0 && p.Triangles.Count % 3 == 0, p.Name);
            Assert.Equal(1.0, p.HingeAxis.Length, 9);
        });
    }

    [Fact]
    public void Trailing_edge_down_deflection_lowers_the_right_aileron()
    {
        var aileron = Parts("trainer").Single(p => p.Name == "aileronRight");
        var c = Centroid(aileron.Triangles);
        Assert.True(RotateAboutHinge(aileron, c, 0.3).Z < c.Z);
    }

    [Fact]
    public void Positive_rudder_deflection_moves_its_trailing_edge_right()
    {
        var rudder = Parts("trainer").Single(p => p.Name == "rudder");
        var c = Centroid(rudder.Triangles);
        Assert.True(RotateAboutHinge(rudder, c, 0.3).Y > c.Y);
    }

    [Fact]
    public void Control_parts_sit_aft_of_their_hinge()
    {
        var elevator = Parts("trainer").Single(p => p.Name == "elevator");
        Assert.True(Centroid(elevator.Triangles).X > elevator.HingePoint.X);
    }

    [Fact]
    public void Fuselage_spans_the_hull_points()
    {
        var def = TestData.Aircraft("trainer");
        var fuselage = Parts("trainer").Single(p => p.Name == "fuselage");
        Assert.Equal(def.Hull.Max(h => h.Position.X), fuselage.Triangles.Max(v => v.X), 6);
        Assert.Equal(def.Hull.Min(h => h.Position.X), fuselage.Triangles.Min(v => v.X), 6);
    }
}
