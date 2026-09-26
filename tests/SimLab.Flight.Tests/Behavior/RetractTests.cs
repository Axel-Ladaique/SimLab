using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Ground;

namespace SimLab.Flight.Tests.Behavior;

public class RetractTests
{
    static readonly ControlInputs GearUp = new(0.5, 0, 0, 0) { GearUp = true };
    static readonly ControlInputs GearDown = new(0.5, 0, 0, 0);

    [Fact]
    public void Gear_travels_up_over_its_transit_time_and_back_down()
    {
        var aircraft = new Aircraft(Fleet.Load("jet"));
        double seconds = aircraft.Definition.GearRetract!.Seconds;
        Assert.Equal(0, aircraft.GearPosition);
        Assert.True(aircraft.Ground.WheelsExtended);

        for (int i = 0; i < (int)(seconds / 2 / 0.002); i++) aircraft.StepControls(0.002, GearUp);
        Assert.Equal(0.5, aircraft.GearPosition, 2);
        Assert.False(aircraft.Ground.WheelsExtended);

        for (int i = 0; i < (int)(seconds / 0.002); i++) aircraft.StepControls(0.002, GearUp);
        Assert.Equal(1, aircraft.GearPosition);

        for (int i = 0; i < (int)(seconds * 1.01 / 0.002); i++) aircraft.StepControls(0.002, GearDown);
        Assert.Equal(0, aircraft.GearPosition);
        Assert.True(aircraft.Ground.WheelsExtended);
    }

    [Fact]
    public void Fixed_gear_ignores_the_gear_command()
    {
        var aircraft = new Aircraft(Fleet.Load("trainer"));
        for (int i = 0; i < 2000; i++) aircraft.StepControls(0.002, GearUp);
        Assert.Equal(0, aircraft.GearPosition);
        Assert.True(aircraft.Ground.WheelsExtended);
    }

    [Fact]
    public void Extended_gear_costs_airspeed()
    {
        double SpeedAfter(ControlInputs input)
        {
            var sim = Fleet.InFlight("jet", 150, 30);
            for (int i = 0; i < 5000; i++) sim.Aircraft.StepControls(0.002, input);
            Fleet.Fly(sim, 0.5, _ => input);
            return sim.Aircraft.AirData.Airspeed;
        }
        // Frontal cdA 0.004 m² at 30 m/s: about 2.2 N on 2.73 kg, 0.4 m/s in 0.5 s.
        Assert.InRange(SpeedAfter(GearUp) - SpeedAfter(GearDown), 0.25, 0.6);
    }

    [Fact]
    public void Raising_the_gear_on_the_runway_drops_the_jet_on_its_belly_without_a_crash()
    {
        var sim = Fleet.OnGround("jet");
        var idle = GearUp with { Throttle = 0 };
        Fleet.Fly(sim, 6, _ => idle);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.Equal(0, sim.Aircraft.Ground.WheelsInContact(sim.Aircraft.State, sim.Environment.Terrain));
        Assert.Contains(sim.Aircraft.Ground.Contacts(sim.Aircraft.State, sim.Environment.Terrain), c => c.Tag == "belly" && c.Depth > 0);
    }
}
