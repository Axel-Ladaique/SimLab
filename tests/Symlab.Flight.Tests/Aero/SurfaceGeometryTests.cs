using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

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
        Assert.All(segments.Where(s => s.Side == Side.Right), s => Assert.True(s.Position.Z > 0));
        Assert.All(segments.Where(s => s.Side == Side.Left), s => Assert.True(s.Position.Z < 0));
    }

    [Fact]
    public void Dihedral_raises_the_tips_and_tilts_normals_inward()
    {
        var segments = SurfaceGeometry.Build(Wing(dihedral: 5), TestAirfoils.Linear());
        var rightTip = segments.Where(s => s.Side == Side.Right).MaxBy(s => s.Position.Z)!;
        var leftTip = segments.Where(s => s.Side == Side.Left).MinBy(s => s.Position.Z)!;
        Assert.True(rightTip.Position.Y > 0);
        Assert.True(rightTip.NormalAxis.Z < 0);
        Assert.True(leftTip.NormalAxis.Z > 0);
        Assert.True(rightTip.PitchAxis.Z > 0.99 && leftTip.PitchAxis.Z > 0.99, "both panels pitch up about +z");
    }

    [Fact]
    public void Vertical_fin_normal_points_left_and_span_points_up()
    {
        var fin = new SurfaceSpec("fin", SurfaceRole.VerticalTail, new Vec3(-0.8, 0, 0), 0.2, 0.2, 0.1,
            0, 90, 0, 0, "linear", 3, Mirror: false);
        var segments = SurfaceGeometry.Build(fin, TestAirfoils.Linear());
        Assert.All(segments, s => Approx(new Vec3(0, 0, -1), s.NormalAxis));
        Assert.True(segments[2].Position.Y > segments[0].Position.Y);
    }

    [Fact]
    public void Flap_effectiveness_matches_thin_airfoil_theory()
        => Assert.Equal(0.609, SurfaceGeometry.FlapEffectiveness(0.25), 3);

    [Fact]
    public void Flap_moment_coefficient_matches_thin_airfoil_theory()
        => Assert.Equal(-0.650, SurfaceGeometry.FlapMomentCoefficient(0.25), 3);

    static void Approx(Vec3 e, Vec3 a)
    {
        Assert.Equal(e.X, a.X, 6);
        Assert.Equal(e.Y, a.Y, 6);
        Assert.Equal(e.Z, a.Z, 6);
    }
}
