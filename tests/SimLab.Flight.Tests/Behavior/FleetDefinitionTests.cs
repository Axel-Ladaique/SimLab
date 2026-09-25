using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Tests.Behavior;

public class FleetDefinitionTests
{
    [Theory]
    [InlineData("trainer", 2.6, 0.525)]
    [InlineData("sport", 2.2, 0.336)]
    [InlineData("wing", 1.1, 0.2475)]
    [InlineData("3d", 1.3, 0.336)]
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
    [InlineData("3d", 1.8)]
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

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Wingtip_hull_points_sit_at_the_wing_tip_height(string id)
    {
        var def = Fleet.Load(id);
        var wing = def.Surfaces.Single(s => s.Role == SurfaceRole.Wing);
        double tipZ = wing.Root.Z + wing.Span * Math.Sin(wing.DihedralDeg * Math.PI / 180);
        var tips = def.Hull.Where(h => h.Name.StartsWith("wingtip", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, tips.Count);
        foreach (var tip in tips) Assert.True(Math.Abs(tip.Position.Z - tipZ) < 0.005, $"{tip.Name} z {tip.Position.Z:F3}, wing tip {tipZ:F3}");
    }
}
