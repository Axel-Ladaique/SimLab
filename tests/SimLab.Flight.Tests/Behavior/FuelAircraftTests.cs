using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>The P-51 (Hangar 9 60cc class) and the F-18 (Skymaster 1:6.25 class) against published data.</summary>
public class FuelAircraftTests
{
    [Fact]
    public void P51_static_rpm_matches_the_evolution_62gxi_benchmark()
    {
        // Evolution 62GXi benchmark: Falcon 23x9 at 7 300 rpm; about 15-18 kg static thrust for a 60 cc single.
        var full = new PowerPlant(Fleet.Load("p51").Power!).SteadyState(1, 0, Isa.SeaLevelDensity);
        Assert.InRange(full.Rpm, 7100, 7500);
        Assert.InRange(full.Thrust / Aircraft.Gravity, 14, 18);
    }

    [Fact]
    public void P51_tank_lasts_a_normal_gas_flight()
    {
        // 700 ml: about 9-10 min at full power, 15+ min of mixed flying.
        var spec = Fleet.Load("p51").Power!.Piston!;
        Assert.InRange(spec.TankMl / spec.FuelFlowMaxMlMin, 8, 11);
    }

    [Fact]
    public void F18_turbine_needs_its_spool_time_to_reach_full_thrust()
    {
        var plant = new PowerPlant(Fleet.Load("f18").Power!);
        double time = 0;
        while (plant.Telemetry.Thrust < 0.95 * 220 && time < 10)
        {
            plant.Step(0.002, 1, 0, Isa.SeaLevelDensity);
            time += 0.002;
        }
        Assert.InRange(time, 3.5, 5.5);
    }

    [Fact]
    public void F18_tank_lasts_a_normal_jet_flight()
    {
        // 4.5 L: 6 min at full power, about 10-12 min of real flying with the throttle mostly back.
        var spec = Fleet.Load("f18").Power!.Turbine!;
        Assert.InRange(spec.TankMl / spec.FuelFlowMaxMlMin, 5, 7);
        Assert.InRange(spec.TankMl / (0.5 * (spec.FuelFlowMaxMlMin + spec.FuelFlowIdleMlMin)), 9, 12);
    }

    [Theory]
    [InlineData("p51", 10, 70)]
    [InlineData("f18", 8, 110)]
    public void Takes_off_on_the_club_runway_holding_the_heading_with_rudder(string id, double pitchDeg, double maxRoll)
    {
        var sim = Fleet.OnGround(id);
        Fleet.Fly(sim, 1, _ => ControlInputs.Neutral);
        var start = sim.Aircraft.State.Position;
        double heading0 = Attitude.FromOrientation(sim.Aircraft.State.Orientation).Heading;
        double? liftOff = null;
        ControlInputs Pilot(double t)
        {
            var st = sim.Aircraft.State;
            var attitude = Attitude.FromOrientation(st.Orientation);
            double headingError = Math.IEEERemainder(attitude.Heading - heading0, 2 * Math.PI);
            double rudder = Math.Clamp(-3 * headingError + 0.3 * WorldYawLeftRate(sim), -1, 1);
            double airspeed = sim.Aircraft.AirData.Airspeed;
            // Stick neutral for the ground run, then rotate to the climb attitude and hold it, wings level.
            double elevator = airspeed < 0.6 * Fleet.Cruise(id).Airspeed ? 0
                : Math.Clamp(2 * (Angle.Rad(pitchDeg) - attitude.Pitch) - 0.3 * st.AngularVelocity.Y, -1, 1);
            double aileron = Math.Clamp(-2 * attitude.Roll - 0.2 * -st.AngularVelocity.X, -1, 1);
            return new ControlInputs(1, aileron, elevator, rudder) { GearUp = liftOff is not null && t > 8 };
        }
        Fleet.Fly(sim, 16, Pilot, s =>
        {
            if (liftOff is null && s.Aircraft.Ground.WheelsInContact(s.Aircraft.State, s.Environment.Terrain) == 0)
                liftOff = (s.Aircraft.State.Position - start).Length;
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.NotNull(liftOff);
        Assert.InRange(liftOff!.Value, 15, maxRoll);
        Assert.True(sim.Aircraft.State.Position.Z > 15, $"altitude {sim.Aircraft.State.Position.Z:F1} m");
    }

    static double WorldYawLeftRate(Simulation sim) => -Fleet.WorldYawRightRate(sim);
}
