using SimLab.Flight.Aero;

namespace SimLab.Flight.Tests.Aero;

public class InducedFlowTests
{
    [Fact]
    public void Finite_wing_lift_slope_matches_lifting_line_estimate()
    {
        const double aspectRatio = 6, oswald = 0.9;
        double k = 1 / (Math.PI * oswald * aspectRatio);
        var airfoil = TestAirfoils.Linear();
        double alpha = 0.05;
        double ai = InducedFlow.SolveInducedAngle(airfoil, 200_000, alpha, k);
        double cl = airfoil.Evaluate(alpha - ai, 200_000).Cl;
        double a0 = 2 * Math.PI;
        double expected = a0 / (1 + a0 * k) * alpha;
        Assert.Equal(expected, cl, 3);
    }

    [Fact]
    public void Ground_effect_vanishes_far_from_the_ground_and_is_strong_near_it()
    {
        Assert.True(InducedFlow.GroundEffectFactor(20, 1.5) > 0.99);
        Assert.True(InducedFlow.GroundEffectFactor(0.05, 1.5) < 0.5);
        Assert.Equal(1.0, InducedFlow.GroundEffectFactor(1, 0));
    }
}
