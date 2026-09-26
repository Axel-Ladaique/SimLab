using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

public class VortexMathTests
{
    [Fact]
    public void Long_segment_induces_the_infinite_line_velocity()
    {
        // Circulation along +x, point 0.5 m above: v = 1/(2πh), along x × z = −y.
        var v = VortexMath.Segment(new Vec3(0, 0, 0.5), new Vec3(-1000, 0, 0), new Vec3(1000, 0, 0), 0);
        Assert.Equal(0, v.X, 9);
        Assert.Equal(-1 / (2 * Math.PI * 0.5), v.Y, 6);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void Semi_infinite_line_induces_half_of_the_infinite_line_beside_its_start()
    {
        var v = VortexMath.SemiInfinite(new Vec3(0, 0, 0.5), Vec3.Zero, Vec3.UnitX, 0);
        Assert.Equal(-1 / (4 * Math.PI * 0.5), v.Y, 9);
        Assert.Equal(0, v.X, 12);
        Assert.Equal(0, v.Z, 12);
    }

    [Fact]
    public void Core_keeps_the_velocity_finite_on_and_near_the_line()
    {
        const double core = 0.1;
        var onLine = VortexMath.Segment(Vec3.Zero, new Vec3(-1, 0, 0), new Vec3(1, 0, 0), core);
        var near = VortexMath.Segment(new Vec3(0, 0, 1e-6), new Vec3(-1, 0, 0), new Vec3(1, 0, 0), core);
        var nearLeg = VortexMath.SemiInfinite(new Vec3(1, 0, 1e-6), Vec3.Zero, Vec3.UnitX, core);
        Assert.Equal(Vec3.Zero, onLine);
        Assert.True(near.Length < 1 / (2 * Math.PI * core), $"segment {near.Length}");
        Assert.True(nearLeg.Length < 1 / (2 * Math.PI * core), $"leg {nearLeg.Length}");
    }

    [Fact]
    public void Point_at_an_end_gives_zero_instead_of_nan()
    {
        Assert.Equal(Vec3.Zero, VortexMath.Segment(Vec3.Zero, Vec3.Zero, Vec3.UnitY, 0));
        Assert.Equal(Vec3.Zero, VortexMath.SemiInfinite(Vec3.Zero, Vec3.Zero, Vec3.UnitX, 0));
        Assert.Equal(Vec3.Zero, VortexMath.SemiInfinite(new Vec3(2, 0, 0), Vec3.Zero, Vec3.UnitX, 0));
    }
}
