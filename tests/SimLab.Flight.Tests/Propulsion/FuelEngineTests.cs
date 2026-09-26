using SimLab.Flight.Geometry;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Tests.Propulsion;

public class FuelEngineTests
{
    const double Rho = 1.225;

    /// <summary>A 62 cc gas single on a 23×9: about 5.5 kW at 8 200 rpm, idling at 1 800 rpm.</summary>
    internal static PowerPlantSpec Gas62() => PowerPlantSpec.WithPiston(
        new PistonEngineSpec(MaxPowerW: 5500, PeakPowerRpm: 8200, IdleRpm: 1800, MaxRpm: 9000, RotorInertia: 0.012,
            TankMl: 700, FuelFlowMaxMlMin: 110, FuelFlowIdleMlMin: 10),
        PropellerSpec.Generic(23, 9), new Vec3(-1, 0, 0), BodyAxes.Forward, SpinDirection: 1, PFactor: 0.1);

    /// <summary>A 220 N micro-turbine: 33 000–117 000 rpm, idle to max in 4 s.</summary>
    internal static PowerPlantSpec Turbine220(double tankMl = 4000) => PowerPlantSpec.WithTurbine(
        new TurbineSpec(MaxThrustN: 220, IdleThrustN: 9, MaxRpm: 117000, IdleRpm: 33000, SpoolUpSeconds: 4, SpoolDownSeconds: 2.5,
            MassFlowKgS: 0.45, NozzleDiameterM: 0.09, RotorInertia: 6e-5, TankMl: tankMl, FuelFlowMaxMlMin: 750, FuelFlowIdleMlMin: 120),
        new Vec3(1, 0, 0), BodyAxes.Forward);

    static PowerPlant Run(PowerPlantSpec spec, double throttle, double seconds, double airspeed = 0)
    {
        var plant = new PowerPlant(spec);
        for (int i = 0; i < (int)(seconds / 0.002); i++) plant.Step(0.002, throttle, airspeed, Rho);
        return plant;
    }

    [Fact]
    public void Piston_starts_idling_and_holds_its_idle_rpm_with_the_throttle_closed()
    {
        Assert.Equal(1800, new PowerPlant(Gas62()).Omega * 60 / (2 * Math.PI), 6);
        var plant = Run(Gas62(), 0, 5);
        Assert.InRange(plant.Telemetry.Rpm, 1700, 1900);
        Assert.True(plant.Telemetry.Thrust > 0);
    }

    [Fact]
    public void Piston_full_throttle_static_rpm_sits_below_the_power_peak_and_matches_the_steady_state()
    {
        var spec = Gas62();
        var dynamic = Run(spec, 1, 5).Telemetry;
        var steady = new PowerPlant(spec).SteadyState(1, 0, Rho);
        Assert.InRange(dynamic.Rpm, 6500, 8200);
        Assert.Equal(steady.Rpm, dynamic.Rpm, tolerance: 5);
        Assert.Equal(steady.Thrust, dynamic.Thrust, 1);
    }

    [Fact]
    public void Piston_crank_torque_rolls_the_airframe_against_the_propeller()
    {
        var t = Run(Gas62(), 1, 5).Telemetry;
        Assert.Equal(t.PropTorque, t.ReactionTorque, 2);
        Assert.True(t.ReactionTorque > 1);
        Assert.Equal(0, t.BatteryCurrent);
    }

    [Fact]
    public void Piston_burns_fuel_with_power_and_stops_when_the_tank_is_empty()
    {
        var spec = Gas62();
        var plant = Run(spec, 1, 60);
        // About 100 ml/min near full power: one minute leaves about six sevenths of the 700 ml.
        Assert.InRange(plant.Telemetry.StateOfCharge, 0.83, 0.88);
        var dry = Run(spec with { Piston = spec.Piston! with { TankMl = 5 } }, 1, 20);
        Assert.Equal(0, dry.Telemetry.StateOfCharge);
        Assert.True(dry.Telemetry.Rpm < 50, $"rpm {dry.Telemetry.Rpm:F0}");
    }

    [Fact]
    public void Piston_propeller_unloads_with_airspeed()
    {
        var steady = new PowerPlant(Gas62());
        Assert.True(steady.SteadyState(1, 30, Rho).Thrust < steady.SteadyState(1, 0, Rho).Thrust);
        Assert.True(steady.SteadyState(1, 30, Rho).Rpm > steady.SteadyState(1, 0, Rho).Rpm);
    }

    [Fact]
    public void Turbine_starts_at_idle_and_spools_up_in_its_spool_time()
    {
        var plant = new PowerPlant(Turbine220());
        plant.Step(0.002, 0, 0, Rho);
        Assert.Equal(33000, plant.Telemetry.Rpm, tolerance: 100);
        Assert.Equal(9, plant.Telemetry.Thrust, 0);

        var half = Run(Turbine220(), 1, 2);
        Assert.InRange(half.Telemetry.Rpm, 60000, 90000);
        var full = Run(Turbine220(), 1, 5);
        Assert.Equal(117000, full.Telemetry.Rpm, tolerance: 1000);
        Assert.Equal(220, full.Telemetry.Thrust, 0);
        Assert.Equal(0, full.Telemetry.ReactionTorque);
    }

    [Fact]
    public void Turbine_spools_down_more_slowly_than_the_throttle_moves()
    {
        var plant = Run(Turbine220(), 1, 5);
        for (int i = 0; i < 500; i++) plant.Step(0.002, 0, 0, Rho);
        Assert.InRange(plant.Telemetry.Rpm, 60000, 110000);
    }

    [Fact]
    public void Turbine_half_throttle_gives_about_half_the_thrust_range()
    {
        var steady = new PowerPlant(Turbine220()).SteadyState(0.5, 0, Rho);
        Assert.Equal(9 + 0.5 * (220 - 9), steady.Thrust, 0);
    }

    [Fact]
    public void Turbine_loses_thrust_to_ram_drag_with_airspeed()
    {
        var plant = new PowerPlant(Turbine220());
        // 0.45 kg/s at 50 m/s: 22.5 N of ram drag.
        Assert.Equal(220 - 0.45 * 50, plant.SteadyState(1, 50, Rho).Thrust, 0);
    }

    [Fact]
    public void Turbine_flames_out_when_the_tank_runs_dry()
    {
        // 750 ml/min at full power: 50 ml lasts about 4 s.
        var plant = Run(Turbine220(tankMl: 50), 1, 12);
        Assert.Equal(0, plant.Telemetry.StateOfCharge);
        Assert.True(plant.Telemetry.Rpm < 1000, $"rpm {plant.Telemetry.Rpm:F0}");
        Assert.True(plant.Telemetry.Thrust < 1);
    }

    [Fact]
    public void Turbine_has_no_torque_roll_but_keeps_its_gyroscopic_moment()
    {
        var spec = Turbine220();
        var t = Run(spec, 1, 5).Telemetry;
        double omega = t.Rpm * 2 * Math.PI / 60;
        var load = PowerPlantLoads.Compute(spec, t, omega, new Vec3(-30, 0, 0), new Vec3(0, 1, 0), inducedVelocity: 0);
        Assert.Equal(0, Vec3.Dot(load.Moment, BodyAxes.Forward), 9);
        // Pitch rate about +y with the rotor spinning about the thrust axis gives a yaw moment ω × H.
        Assert.Equal(6e-5 * omega, Math.Abs(load.Moment.Z), 6);
    }
}
