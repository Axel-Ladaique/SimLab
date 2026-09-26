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
    [InlineData("trainer", new[] { "airframe", "aileronRight", "aileronLeft", "elevator", "elevator", "rudder", "fuselage", "gear", "propeller" })]
    [InlineData("wing", new[] { "airframe", "elevonRight", "elevonLeft", "fuselage", "propeller" })]
    public void Builds_one_part_per_control_panel_plus_fixed_parts(string id, string[] names)
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
        Assert.All(Parts("trainer").Where(p => p.Name == "elevator"),
            elevator => Assert.True(Centroid(elevator.Triangles).X > elevator.HingePoint.X));
    }

    static double DistanceToHingeLine(MeshPart part, Vec3 p)
    {
        var offset = p - part.HingePoint;
        return (offset - part.HingeAxis * Vec3.Dot(offset, part.HingeAxis)).Length;
    }

    /// <summary>A control that spans both halves of a swept or dihedral surface (the F-16 and F-18 stabilators) must
    /// turn each half about its own hinge line, not about the mirror image of the other half's. Washout twists each
    /// strip's chord, so on twisted wings the strip hinges sit up to ~0.4 % of the control's span off the straight line
    /// (tolerance 1 %); the mirrored-axis bug put them 5 to 100 mm off.</summary>
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("3d")]
    [InlineData("wing")]
    [InlineData("p51")]
    [InlineData("jet")]
    [InlineData("f18")]
    public void Every_control_panel_turns_about_its_own_hinge_line(string id)
    {
        foreach (var part in Parts(id).Where(p => p.ControlIndex >= 0))
        {
            var along = part.Triangles.Select(v => Vec3.Dot(v - part.HingePoint, part.HingeAxis)).ToList();
            double tolerance = 0.01 * (along.Max() - along.Min());
            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                double onHinge = Enumerable.Range(t, 3).Min(i => DistanceToHingeLine(part, part.Triangles[i]));
                Assert.True(onHinge < tolerance, $"{id} {part.Name}: triangle {t / 3} is {onHinge:E2} m off its hinge line");
            }
        }
    }

    [Fact]
    public void A_control_on_both_halves_of_a_surface_gets_one_part_per_half()
    {
        var elevators = Parts("jet").Where(p => p.Name == "elevator").ToList();
        Assert.Equal(2, elevators.Count);
        Assert.True(elevators[0].HingePoint.Y * elevators[1].HingePoint.Y < 0);
    }

    [Fact]
    public void Fuselage_spans_the_hull_points()
    {
        var def = TestData.Aircraft("trainer");
        var fuselage = Parts("trainer").Single(p => p.Name == "fuselage");
        Assert.Equal(def.Hull.Max(h => h.Position.X), fuselage.Triangles.Max(v => v.X), 6);
        Assert.Equal(def.Hull.Min(h => h.Position.X), fuselage.Triangles.Min(v => v.X), 6);
    }
    [Fact]
    public void Swept_tapered_surfaces_are_drawn_as_their_trapezoid()
    {
        // Jet wing: leading edge at datum x 0.56 on the centreline, 40° leading-edge sweep, 0.47 m semi-span, tip
        // chord 0.115; CG at datum x 0.77. Strips drawn as rectangles would leave a staircase along the leading edge.
        var parts = Parts("jet").Where(p => p.Name is "airframe" or "aileronRight" or "aileronLeft").SelectMany(p => p.Triangles).ToList();
        var tip = parts.Where(v => v.Y > 0.47 - 1e-6).ToList();
        double tipLeading = 0.56 + 0.47 * Math.Tan(40 * Math.PI / 180) - 0.77;
        Assert.Equal(tipLeading, tip.Min(v => v.X), 2);
        Assert.Equal(tipLeading + 0.115, tip.Max(v => v.X), 2);
        var root = parts.Where(v => Math.Abs(v.Y) < 1e-6 && v.Z < 0.02).ToList(); // not the fin
        Assert.Equal(0.56 - 0.77, root.Min(v => v.X), 2);
        Assert.Equal(0.56 + 0.50 - 0.77, root.Max(v => v.X), 2);
    }

    [Fact]
    public void Retractable_gear_is_one_folding_leg_per_wheel()
    {
        var def = TestData.Aircraft("jet");
        var parts = Parts("jet");
        Assert.DoesNotContain(parts, p => p.Name == "gear");
        var legs = parts.Where(p => p.Retracts).ToList();
        Assert.Equal(def.Wheels.Select(w => "gear:" + w.Name).OrderBy(n => n), legs.Select(p => p.Name).OrderBy(n => n));
        foreach (var leg in legs)
        {
            var wheel = def.Wheels.Single(w => "gear:" + w.Name == leg.Name);
            // The leg hangs from the CG level above its wheel and folds forward and up about the body's right axis.
            Assert.Equal(new Vec3(wheel.Position.X, wheel.Position.Y, 0), leg.HingePoint);
            Assert.Equal(BodyAxes.Right, leg.HingeAxis);
            Assert.Equal(wheel.Position.Z, leg.Triangles.Min(v => v.Z), 2);
            var c = Centroid(leg.Triangles);
            var folded = RotateAboutHinge(leg, c, Math.PI / 2);
            Assert.True(folded.X < c.X - 0.05 && folded.Z > c.Z + 0.05, leg.Name);
        }
    }

    [Fact]
    public void Fixed_gear_stays_one_part()
    {
        var gear = Parts("trainer").Single(p => p.Name == "gear");
        Assert.False(gear.Retracts);
    }
}
