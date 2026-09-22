using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Sim;
using Symlab.Flight.Tests.Airframe;

namespace Symlab.Flight.Tests.Sim;

public class SimulationTests
{
    static Simulation Glider(double altitude = 50, double speed = 12, double pitchDeg = 0)
    {
        var sim = new Simulation(new Aircraft(TestDefinitions.Glider()), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.InFlight(new Vec3(0, altitude, 0), headingDeg: 0, airspeed: speed, pitchDeg: pitchDeg));
        return sim;
    }

    [Fact]
    public void Glider_glides_forward_and_down()
    {
        var sim = Glider();
        for (int i = 0; i < 2500; i++) sim.StepOnce(ControlInputs.Neutral);
        var s = sim.Aircraft.State;
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(s.Position.Y, 30, 50);
        Assert.True(s.Position.Z < -30, $"north distance {-s.Position.Z}");
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 6, 25);
        Assert.Equal(5.0, sim.Time, 9);
    }

    [Fact]
    public void Runs_are_deterministic()
    {
        var a = Glider();
        var b = Glider();
        var input = new ControlInputs(0, 0, 0.2, 0);
        for (int i = 0; i < 1000; i++) { a.StepOnce(input); b.StepOnce(input); }
        Assert.Equal(a.Aircraft.State, b.Aircraft.State);
    }

    [Fact]
    public void Advance_runs_whole_fixed_steps_and_reports_the_remainder()
    {
        var sim = Glider();
        Assert.Equal(2, sim.Advance(0.005, ControlInputs.Neutral));
        Assert.Equal(0.5, sim.InterpolationAlpha, 6);
        Assert.Equal(125, Glider().Advance(1.0, ControlInputs.Neutral));
    }

    [Fact]
    public void Previous_state_is_kept_for_interpolation()
    {
        var sim = Glider();
        sim.StepOnce(ControlInputs.Neutral);
        var before = sim.Aircraft.State;
        sim.StepOnce(ControlInputs.Neutral);
        Assert.Equal(before, sim.Previous);
    }

    [Fact]
    public void Diving_into_the_ground_crashes_and_freezes_the_aircraft()
    {
        var sim = Glider(altitude: 1.5, speed: 15, pitchDeg: -45);
        for (int i = 0; i < 1000 && sim.Aircraft.Crash == CrashCause.None; i++) sim.StepOnce(ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, sim.Aircraft.Crash);
        var frozen = sim.Aircraft.State;
        sim.StepOnce(ControlInputs.Neutral);
        Assert.Equal(frozen, sim.Aircraft.State);
    }

    [Fact]
    public void Servo_deflection_follows_the_stick_with_rate_limit()
    {
        var sim = Glider();
        sim.StepOnce(new ControlInputs(0, 0, 1, 0));
        double first = sim.Aircraft.Deflections[0];
        Assert.True(first < 0 && first > -Angle.Rad(20));
        for (int i = 0; i < 200; i++) sim.StepOnce(new ControlInputs(0, 0, 1, 0));
        Assert.Equal(-Angle.Rad(20), sim.Aircraft.Deflections[0], 9);
    }

    [Fact]
    public void On_ground_start_puts_the_lowest_point_on_the_terrain()
    {
        var def = TestDefinitions.Glider();
        var s = InitialConditions.OnGround(def, new Symlab.Flight.Terrain.FlatTerrain(10), 0, 0, 90);
        Assert.Equal(10.051, s.Position.Y, 6);
        Assert.Equal(1.0, s.Orientation.Rotate(Vec3.UnitX).X, 9);
    }
}
