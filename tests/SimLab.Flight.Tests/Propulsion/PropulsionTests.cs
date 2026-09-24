using SimLab.Flight.Geometry;
using SimLab.Flight.Propulsion;

namespace SimLab.Flight.Tests.Propulsion;

public class PropulsionTests
{
    internal static PowerPlantSpec TrainerLike() => new(
        new MotorSpec(Kv: 650, ResistanceOhm: 0.04, NoLoadCurrentA: 1.2, MaxCurrentA: 60, RotorInertia: 3e-4),
        BatterySpec.Lipo(cells: 4, capacityAh: 3.0, internalResistanceOhm: 0.028),
        EscSpec.Linear(),
        PropellerSpec.Generic(diameterIn: 12, pitchIn: 6),
        Position: new Vec3(0.45, 0, 0),
        ThrustAxis: Vec3.UnitX,
        SpinDirection: 1,
        PFactor: 0.1);

    static PowerPlant SpunUp(double seconds = 3)
    {
        var plant = new PowerPlant(TrainerLike());
        for (int i = 0; i < (int)(seconds / 0.002); i++) plant.Step(0.002, 1.0, 0, 1.225);
        return plant;
    }

    [Fact]
    public void Generic_propeller_thrust_falls_with_advance_ratio_to_zero_at_J0()
    {
        var p = PropellerSpec.Generic(12, 6);
        Assert.True(p.Ct[0] > p.Ct[2] && p.Ct[2] > 0);
        Assert.Equal(0, p.Ct[4], 12);
        Assert.True(p.Ct[5] < 0);
    }

    [Fact]
    public void Static_thrust_follows_the_thrust_coefficient()
    {
        var p = PropellerSpec.Generic(12, 6);
        var load = PropellerAero.Evaluate(p, 2 * Math.PI * 100, 0, 1.225);
        Assert.Equal(p.Ct[0] * 1.225 * 100 * 100 * Math.Pow(p.DiameterM, 4), load.Thrust, 9);
    }

    [Fact]
    public void Motor_spools_up_to_the_steady_state()
    {
        var plant = SpunUp();
        var steady = plant.SteadyState(1.0, 0, 1.225);
        Assert.InRange(plant.Omega, 0.97 * steady.Omega, 1.03 * steady.Omega);
        Assert.InRange(steady.Thrust, 15, 40);
    }

    [Fact]
    public void Battery_voltage_sags_under_load()
    {
        var plant = SpunUp();
        double open = plant.Spec.Battery.OpenCircuitVoltage(plant.StateOfCharge);
        Assert.True(plant.Telemetry.BatteryVoltage < open - 0.5, $"loaded {plant.Telemetry.BatteryVoltage} V, open {open} V");
    }

    [Fact]
    public void Idle_throttle_draws_no_current_and_the_prop_winds_down()
    {
        var plant = SpunUp(2);
        double before = plant.Omega;
        for (int i = 0; i < 1500; i++) plant.Step(0.002, 0, 0, 1.225);
        Assert.Equal(0, plant.Telemetry.MotorCurrent);
        Assert.True(plant.Omega < 0.2 * before);
    }

    [Fact]
    public void One_minute_at_full_throttle_uses_about_a_fifth_of_the_pack()
    {
        var plant = SpunUp(60);
        Assert.InRange(1 - plant.StateOfCharge, 0.12, 0.28);
    }

    [Fact]
    public void Loads_contain_thrust_and_a_roll_left_reaction_for_a_clockwise_prop()
    {
        var t = default(PowerTelemetry) with { Thrust = 20, MotorTorque = 0.5 };
        var load = PowerPlantLoads.Compute(TrainerLike(), t, 0, Vec3.Zero, Vec3.Zero);
        Assert.Equal(20, load.Force.X, 12);
        Assert.Equal(-0.5, load.Moment.X, 12);
        Assert.Equal(0, load.Moment.Y, 12);
    }

    [Fact]
    public void P_factor_yaws_left_at_positive_angle_of_attack()
    {
        var t = default(PowerTelemetry) with { Thrust = 20 };
        var level = PowerPlantLoads.Compute(TrainerLike(), t, 0, new Vec3(15, 0, 0), Vec3.Zero);
        var noseUp = PowerPlantLoads.Compute(TrainerLike(), t, 0, new Vec3(15, -3, 0), Vec3.Zero);
        Assert.True(noseUp.Moment.Y > level.Moment.Y);
    }

    [Fact]
    public void Pitching_up_with_a_clockwise_prop_yaws_right()
    {
        var load = PowerPlantLoads.Compute(TrainerLike(), default, 1000, Vec3.Zero, new Vec3(0, 0, 1));
        Assert.True(load.Moment.Y < 0, $"gyroscopic yaw moment {load.Moment.Y}");
    }
}
