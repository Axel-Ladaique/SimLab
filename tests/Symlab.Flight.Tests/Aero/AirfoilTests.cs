using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

public class AirfoilTests
{
    static Airfoil Sample() => new("sample",
    [
        new AirfoilTable(100_000, [-10, 0, 10, 14], [-0.8, 0.2, 1.1, 1.2], [0.02, 0.01, 0.02, 0.05], [-0.05, -0.05, -0.05, -0.06]),
        new AirfoilTable(300_000, [-10, 0, 10, 14], [-0.9, 0.2, 1.2, 1.3], [0.015, 0.008, 0.015, 0.04], [-0.05, -0.05, -0.05, -0.06]),
    ]);

    [Fact]
    public void Returns_table_values_at_nodes()
    {
        var c = Sample().Evaluate(Angle.Rad(10), 100_000);
        Assert.Equal(1.1, c.Cl, 9);
        Assert.Equal(0.02, c.Cd, 9);
    }

    [Fact]
    public void Interpolates_between_alpha_nodes() => Assert.Equal(0.65, Sample().Evaluate(Angle.Rad(5), 100_000).Cl, 9);

    [Fact]
    public void Interpolates_between_reynolds_tables() => Assert.Equal(1.15, Sample().Evaluate(Angle.Rad(10), 200_000).Cl, 9);

    [Fact]
    public void Clamps_reynolds_outside_the_data() => Assert.Equal(1.2, Sample().Evaluate(Angle.Rad(10), 5_000_000).Cl, 9);

    [Fact]
    public void At_ninety_degrees_behaves_like_a_flat_plate()
    {
        var c = Sample().Evaluate(Math.PI / 2, 200_000);
        Assert.True(Math.Abs(c.Cl) < 0.05, $"Cl {c.Cl}");
        Assert.InRange(c.Cd, 1.9, 2.1);
        Assert.True(c.Cm < 0);
    }

    [Fact]
    public void Coefficients_are_continuous_over_the_full_circle()
    {
        var airfoil = Sample();
        var previous = airfoil.Evaluate(Angle.Rad(-180), 200_000);
        for (double a = -179.9; a <= 180.0; a += 0.1)
        {
            var c = airfoil.Evaluate(Angle.Rad(a), 200_000);
            Assert.True(Math.Abs(c.Cl - previous.Cl) < 0.05, $"Cl jump at {a:F1} deg");
            Assert.True(Math.Abs(c.Cd - previous.Cd) < 0.05, $"Cd jump at {a:F1} deg");
            previous = c;
        }
    }

    [Fact]
    public void Lift_drops_after_the_last_data_point()
    {
        var airfoil = Sample();
        Assert.True(airfoil.Evaluate(Angle.Rad(24), 100_000).Cl < airfoil.Evaluate(Angle.Rad(14), 100_000).Cl);
    }

    [Fact]
    public void Rejects_mismatched_arrays()
        => Assert.Throws<ArgumentException>(() => new Airfoil("bad", [new AirfoilTable(1e5, [0, 1], [0], [0, 0], [0, 0])]));
}
