using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>The F-16 80 mm EDF against published data for its class (E-flite F-16 Falcon 80mm, 80 mm 12-blade fan on 6S).</summary>
public class JetTests
{
    [Fact]
    public void Static_thrust_and_current_match_80mm_6s_fan_bench_figures()
    {
        // Bench data for 80 mm 12-blade 2100 kV fans on 6S: about 3.2-3.4 kg for 95-100 A.
        var def = Fleet.Load("jet");
        var full = new PowerPlant(def.Power!).SteadyState(1.0, 0, Isa.SeaLevelDensity);
        Assert.InRange(full.Thrust / Aircraft.Gravity, 2.9, 3.5);
        Assert.InRange(full.Current, 85, 100);
        Assert.InRange(full.Rpm, 42000, 50000);
    }

    [Fact]
    public void Full_throttle_empties_a_5000_mah_pack_in_about_three_and_a_half_minutes()
    {
        // Reviews of the E-flite F-16 80mm: about 3.5 min at most on a 6S 5000 mAh pack.
        var def = Fleet.Load("jet");
        var plant = new PowerPlant(def.Power!);
        double minutes = def.Power!.Battery!.CapacityAh / plant.SteadyState(1.0, 35, Isa.SeaLevelDensity).Current * 60;
        Assert.InRange(minutes, 2.8, 4.5);
    }

    [Fact]
    public void Full_power_climb_does_not_roll_away_with_the_fan_torque()
    {
        var sim = Fleet.InFlight("jet", 150, 30);
        double maxBank = 0;
        Fleet.Fly(sim, 3, _ => new ControlInputs(1, 0, 0, 0) { GearUp = true }, s => maxBank = Math.Max(maxBank, Math.Abs(Fleet.Roll(s))));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(maxBank < Angle.Rad(20), $"max bank {Angle.Deg(maxBank):F0} deg");
    }

    [Fact]
    public void Cruise_is_jet_fast()
    {
        var sim = Fleet.InFlight("jet", 150, Fleet.Cruise("jet").Airspeed);
        Fleet.Fly(sim, 10, _ => Fleet.CruiseInputs("jet"));
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 25, 40);
    }
}
