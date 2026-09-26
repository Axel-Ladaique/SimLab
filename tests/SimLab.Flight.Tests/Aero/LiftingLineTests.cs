using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

public class LiftingLineTests
{
    static readonly SurfaceSpec Rectangular = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.25, 0.25, 0, 0, 0, 0, "linear", 12, true);
    static readonly SurfaceSpec Stab = new("stab", SurfaceRole.HorizontalTail, new Vec3(0.8, 0, 0.1), 0.25, 0.15, 0.15, 0, 0, 0, 0, "linear", 4, true);
    static readonly SurfaceSpec Fin = new("fin", SurfaceRole.VerticalTail, new Vec3(0.8, 0, 0.05), 0.2, 0.18, 0.12, 20, 90, 0, 0, "linear", 6, false);

    static List<SurfaceSegment> Strips(params SurfaceSpec[] specs) =>
        specs.SelectMany(s => SurfaceGeometry.Build(s, TestAirfoils.Linear())).ToList();

    static StripState[] Uniform(int count, double speed, double alphaDeg, double betaDeg = 0)
    {
        double a = Angle.Rad(alphaDeg), b = Angle.Rad(betaDeg);
        var air = new Vec3(-speed * Math.Cos(a) * Math.Cos(b), speed * Math.Sin(b), -speed * Math.Sin(a) * Math.Cos(b));
        return Enumerable.Repeat(new StripState(air, 0, 1), count).ToArray();
    }

    [Fact]
    public void Bound_vortices_run_so_that_the_flow_lifts_along_each_strip_normal()
    {
        var strips = Strips(Rectangular, Fin);
        var line = new LiftingLine(strips);
        for (int i = 0; i < strips.Count; i++)
            Assert.True(Vec3.Dot(Vec3.Cross(Vec3.UnitX, line.BoundVector(i)), strips[i].NormalAxis) > 0, $"strip {i}");
    }

    [Fact]
    public void Very_long_wing_tends_to_the_two_dimensional_circulation()
    {
        var longWing = Rectangular with { Span = 50, Segments = 40 };
        var strips = Strips(longWing);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 3), 1.225);
        // Mid-span strip (index 0 is the right root strip): Γ = ½ V c a α with a = 2π.
        double expected = 0.5 * 15 * 0.25 * 2 * Math.PI * Math.Sin(Angle.Rad(3));
        Assert.InRange(line.Circulation(0), 0.97 * expected, 1.01 * expected);
    }

    [Fact]
    public void Linear_polar_converges_in_a_few_newton_steps()
    {
        var strips = Strips(Rectangular, Stab, Fin);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 2), 1.225);
        Assert.True(line.LastIterations <= 3, $"iterations {line.LastIterations}");
        Assert.Equal(0, line.NonConvergedSolves);
    }

    [Fact]
    public void Symmetric_flight_gives_symmetric_circulation()
    {
        var strips = Strips(Rectangular, Stab);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 4), 1.225);
        // SurfaceGeometry emits each right strip followed by its left mirror.
        for (int i = 0; i < strips.Count; i += 2)
            Assert.Equal(line.Circulation(i), line.Circulation(i + 1), 9);
    }

    [Fact]
    public void Tail_feels_the_wing_downwash_after_the_lag()
    {
        var strips = Strips(Rectangular, Stab);
        var line = new LiftingLine(strips);
        var states = Uniform(strips.Count, 15, 5);
        int tail = strips.FindIndex(s => s.Role == SurfaceRole.HorizontalTail);
        // Lag time constant: stab mean quarter-chord x (0.8) minus the wing's (0), over the airspeed.
        double tau = 0.8 / 15;

        line.Solve(states, 1.225);
        double before = line.Circulation(tail);
        line.Advance(1000, 15);
        line.Solve(states, 1.225);
        double settled = line.Circulation(tail);
        Assert.True(settled < before, "the wing's downwash lowers the tail's circulation");

        line.Reset();
        line.Solve(states, 1.225);
        for (int k = 0; k < 100; k++)
        {
            line.Advance(tau / 100, 15);
            line.Solve(states, 1.225);
        }
        double fraction = (line.Circulation(tail) - before) / (settled - before);
        Assert.InRange(fraction, 0.57, 0.70);
    }

    [Theory]
    [InlineData(15, 40)]
    [InlineData(15, 180)]
    [InlineData(0.05, 5)]
    public void Stays_finite_far_outside_the_linear_range(double speed, double alphaDeg)
    {
        var strips = Strips(Rectangular, Stab, Fin);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, speed, alphaDeg, 10), 1.225);
        Assert.True(line.LastIterations <= LiftingLine.MaxIterations);
        for (int i = 0; i < strips.Count; i++)
        {
            Assert.True(double.IsFinite(line.Circulation(i)), $"Γ {i}");
            Assert.True(double.IsFinite(line.InducedAtBoundVortex(i).Length), $"induced {i}");
        }
    }

    [Fact]
    public void Circulation_stays_bounded_when_the_induced_flow_exceeds_the_onset_flow()
    {
        // A static prop hang in miniature: a fast band at the wing root, near-still air elsewhere. With Γ = ½ v cₙ cl and
        // v taken from the flow including the induced velocity, v ∝ |w| ∝ Γ feeds back and diverges; with the onset speed,
        // Γ is bounded by ½ v_onset cₙ max|cl|. A 0.4 rad flap term on every strip (a deflected control, as in the full-aileron
        // hover where the loop was found) supplies the lift that the feedback needs.
        var strips = Strips(Rectangular, Stab);
        var line = new LiftingLine(strips);
        var states = new StripState[strips.Count];
        double a = Angle.Rad(10);
        for (int i = 0; i < strips.Count; i++)
        {
            double speed = strips[i].Role == SurfaceRole.Wing && strips[i].SpanFraction < 0.3 ? 20 : 0.3;
            states[i] = new StripState(new Vec3(-speed * Math.Cos(a), 0, -speed * Math.Sin(a)), 0.4, 1);
        }
        for (int k = 0; k < 500; k++) line.Solve(states, 1.225);
        for (int i = 0; i < strips.Count; i++)
        {
            double speed = states[i].Velocity.Length;
            double bound = 0.5 * speed * line.NormalChord(i) * 2.5;
            double gamma = line.Circulation(i);
            Assert.True(double.IsFinite(gamma), $"Γ {i} not finite");
            Assert.True(Math.Abs(gamma) <= bound, $"strip {i}: |Γ| {Math.Abs(gamma):G4} > {bound:G4}");
        }
    }

    [Fact]
    public void Reset_clears_the_circulation()
    {
        var strips = Strips(Rectangular);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 4), 1.225);
        Assert.NotEqual(0, line.Circulation(0));
        line.Reset();
        Assert.Equal(0, line.Circulation(0));
        Assert.Equal(Vec3.Zero, line.InducedAtBoundVortex(0));
    }
}
