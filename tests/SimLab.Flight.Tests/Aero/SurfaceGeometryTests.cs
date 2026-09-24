using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

public class SurfaceGeometryTests
{
    static SurfaceSpec Wing(double dihedral = 0) => new("wing", SurfaceRole.Wing, new Vec3(0, 0, 0), 0.75, 0.3, 0.2,
        SweepDeg: 0, DihedralDeg: dihedral, IncidenceDeg: 0, TwistDeg: 0, Airfoil: "linear", Segments: 5, Mirror: true);

    [Fact]
    public void Mirrored_surface_has_twice_the_segments_and_the_full_area()
    {
        var segments = SurfaceGeometry.Build(Wing(), TestAirfoils.Linear());
        Assert.Equal(10, segments.Count);
        Assert.Equal(Wing().TotalArea, segments.Sum(s => s.Area), 12);
        Assert.All(segments.Where(s => s.Side == Side.Right), s => Assert.True(s.Position.Y > 0));
        Assert.All(segments.Where(s => s.Side == Side.Left), s => Assert.True(s.Position.Y < 0));
    }

    [Fact]
    public void Wing_axes_follow_the_body_frame()
    {
        var segments = SurfaceGeometry.Build(Wing(), TestAirfoils.Linear());
        Assert.All(segments, s =>
        {
            Approx(BodyAxes.Forward, s.ChordAxis);
            Approx(BodyAxes.Up, s.NormalAxis);
            Approx(new Vec3(0, 1, 0), s.PitchAxis);
        });
    }

    [Fact]
    public void Dihedral_raises_the_tips_and_tilts_normals_inward()
    {
        var segments = SurfaceGeometry.Build(Wing(dihedral: 5), TestAirfoils.Linear());
        var rightTip = segments.Where(s => s.Side == Side.Right).MaxBy(s => s.Position.Y)!;
        var leftTip = segments.Where(s => s.Side == Side.Left).MinBy(s => s.Position.Y)!;
        Assert.True(rightTip.Position.Z > 0);
        Assert.True(leftTip.Position.Z > 0);
        Assert.True(rightTip.NormalAxis.Y < 0);
        Assert.True(leftTip.NormalAxis.Y > 0);
        Assert.True(rightTip.PitchAxis.Y > 0.99 && leftTip.PitchAxis.Y > 0.99, "both panels pitch up about +y");
    }

    [Fact]
    public void Incidence_raises_the_leading_edge()
    {
        var spec = Wing() with { IncidenceDeg = 5 };
        var segment = SurfaceGeometry.Build(spec, TestAirfoils.Linear())[0];
        Assert.True(segment.ChordAxis.Z > 0, "leading edge up");
        Assert.True(segment.NormalAxis.X > 0, "normal tilted back");
    }

    [Fact]
    public void Sweep_moves_the_tips_back()
    {
        var spec = Wing() with { SweepDeg = 25 };
        var segments = SurfaceGeometry.Build(spec, TestAirfoils.Linear());
        var tip = segments.Where(s => s.Side == Side.Right).MaxBy(s => s.Position.Y)!;
        Assert.Equal(tip.Position.Y * Math.Tan(Angle.Rad(25)), tip.Position.X, 12);
    }

    [Fact]
    public void Vertical_fin_normal_points_left_and_span_points_up()
    {
        var fin = new SurfaceSpec("fin", SurfaceRole.VerticalTail, new Vec3(0.8, 0, 0), 0.2, 0.2, 0.1,
            0, 90, 0, 0, "linear", 3, Mirror: false);
        var segments = SurfaceGeometry.Build(fin, TestAirfoils.Linear());
        Assert.All(segments, s => Approx(new Vec3(0, -1, 0), s.NormalAxis));
        Assert.True(segments[2].Position.Z > segments[0].Position.Z);
    }

    [Fact]
    public void Flap_effectiveness_matches_thin_airfoil_theory()
        => Assert.Equal(0.609, SurfaceGeometry.FlapEffectiveness(0.25), 3);

    [Fact]
    public void Flap_moment_coefficient_matches_thin_airfoil_theory()
        => Assert.Equal(-0.650, SurfaceGeometry.FlapMomentCoefficient(0.25), 3);

    [Theory]
    [InlineData(0.0, 0.25)]
    [InlineData(5.0, 0.25)]
    [InlineData(10.0, 0.10)]
    [InlineData(10.0, 0.50)]
    public void Large_deflection_factor_is_one_for_small_deflections(double deg, double chordFraction)
        => Assert.Equal(1.0, SurfaceGeometry.LargeDeflectionFactor(Angle.Rad(deg), chordFraction), 12);

    [Theory]
    [InlineData(20.0, 0.25, 0.850)]
    [InlineData(30.0, 0.10, 0.722)]
    [InlineData(60.0, 0.50, 0.423)]
    [InlineData(25.0, 0.25, (0.740 + 0.677) / 2)]
    [InlineData(20.0, 0.35, (0.800 + 0.750) / 2)]
    [InlineData(80.0, 0.60, 0.423)]
    [InlineData(20.0, 0.05, 0.900)]
    public void Large_deflection_factor_interpolates_the_datcom_chart(double deg, double chordFraction, double expected)
        => Assert.Equal(expected, SurfaceGeometry.LargeDeflectionFactor(Angle.Rad(deg), chordFraction), 3);

    [Fact]
    public void Large_deflection_factor_is_even_and_continuous_in_deflection()
    {
        double previous = 1;
        for (double deg = 0; deg <= 70; deg += 0.25)
        {
            double k = SurfaceGeometry.LargeDeflectionFactor(Angle.Rad(deg), 0.28);
            Assert.Equal(k, SurfaceGeometry.LargeDeflectionFactor(Angle.Rad(-deg), 0.28), 12);
            Assert.True(k <= previous + 1e-12 && previous - k < 0.02, $"step at {deg} deg");
            previous = k;
        }
    }

    static void Approx(Vec3 e, Vec3 a)
    {
        Assert.Equal(e.X, a.X, 6);
        Assert.Equal(e.Y, a.Y, 6);
        Assert.Equal(e.Z, a.Z, 6);
    }
}
