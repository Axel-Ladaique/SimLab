using Symlab.Flight.Airframe;
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Behavior;

public class FleetDefinitionTests
{
    [Theory]
    [InlineData("trainer", 2.6, 0.525)]
    [InlineData("sport", 2.2, 0.336)]
    [InlineData("wing", 1.1, 0.2475)]
    public void Definition_loads_with_expected_mass_and_wing_area(string id, double mass, double wingArea)
    {
        var def = Fleet.Load(id);
        Assert.Equal(mass, def.Mass.Mass);
        Assert.Equal(wingArea, new Aircraft(def).Aero.WingArea, 3);
    }

    [Theory]
    [InlineData("trainer", 0.7)]
    [InlineData("sport", 1.0)]
    [InlineData("wing", 0.5)]
    public void Static_thrust_to_weight_matches_the_aircraft_type(string id, double minimum)
    {
        var def = Fleet.Load(id);
        var full = new PowerPlant(def.Power!).SteadyState(1.0, 0, Isa.SeaLevelDensity);
        double ratio = full.Thrust / (def.Mass.Mass * Aircraft.Gravity);
        Assert.True(ratio > minimum, $"{id}: thrust/weight {ratio:F2}");
    }

    [Fact]
    public void Trainer_half_throttle_static_endurance_is_plausible()
    {
        var def = Fleet.Load("trainer");
        var half = new PowerPlant(def.Power!).SteadyState(0.5, 0, Isa.SeaLevelDensity);
        double batteryCurrent = half.Current * def.Power!.Esc.Map(0.5);
        double minutes = def.Power.Battery.CapacityAh / batteryCurrent * 60;
        Assert.InRange(minutes, 10, 45);
    }
}
