using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Ui;

public class OsdDataTests
{
    static readonly Vec3 Pilot = new(0, -25, 0);

    static RigidBodyState State(Vec3 position, double rollDeg, double pitchDeg, double headingDeg, Vec3 velocity = default) =>
        new(position, velocity, Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg)), Vec3.Zero);

    static OsdData Osd(RigidBodyState state, Aircraft? aircraft = null, double throttle = 0.5, double flap = 0) =>
        OsdData.From(aircraft ?? new Aircraft(TestData.Aircraft("trainer")), state, 12.3, 75, throttle, Pilot, flap);

    [Fact]
    public void Attitude_vario_height_and_time_come_through_in_pilot_units()
    {
        var osd = Osd(State(new Vec3(0, 75, 20), 30, 10, 90, new Vec3(0, 0, 2.5)));
        Assert.Equal(90, osd.HeadingDeg, 6);
        Assert.Equal(30, osd.RollDeg, 6);
        Assert.Equal(10, osd.PitchDeg, 6);
        Assert.Equal(2.5, osd.VarioMs, 9);
        Assert.Equal(12.3, osd.HeightM, 9);
        Assert.Equal(75, osd.FlightTimeSeconds);
        Assert.Equal(0, osd.AirspeedKmh);
    }

    [Theory]
    [InlineData(0, 180)]    // flying north, pilot to the south: behind
    [InlineData(90, 90)]    // flying east: pilot on the right
    [InlineData(270, -90)]  // flying west: pilot on the left
    [InlineData(180, 0)]    // flying south: pilot ahead
    public void Home_arrow_points_at_the_pilot_relative_to_the_heading(double heading, double expected)
    {
        var osd = Osd(State(new Vec3(0, 75, 20), 0, 0, heading));
        Assert.Equal(100, osd.HomeDistanceM, 9);
        Assert.Equal(expected, osd.HomeRelativeBearingDeg, 6);
    }

    [Fact]
    public void Relative_bearing_wraps_across_north()
    {
        // 100 m east of the pilot flying 10°: the pilot is due west (270°), i.e. 100° to the left.
        var osd = Osd(State(new Vec3(100, -25, 20), 0, 0, 10));
        Assert.Equal(-100, osd.HomeRelativeBearingDeg, 6);
        Assert.Equal(180, OsdData.WrapDeg(-180));
        Assert.Equal(-170, OsdData.WrapDeg(190));
    }

    [Theory]
    [InlineData(-0.2, 0)]
    [InlineData(0.654, 65.4)]
    [InlineData(1.3, 100)]
    public void Throttle_is_clamped_to_percent(double throttle, double expected)
        => Assert.Equal(expected, Osd(State(new Vec3(0, 75, 20), 0, 0, 0), throttle: throttle).ThrottlePercent, 9);

    [Fact]
    public void Battery_starts_at_the_open_circuit_voltage_with_nothing_consumed()
    {
        var aircraft = new Aircraft(TestData.Aircraft("trainer"));
        var osd = Osd(State(new Vec3(0, 75, 20), 0, 0, 0), aircraft);
        Assert.Equal(aircraft.Power!.Spec.Battery!.OpenCircuitVoltage(1.0), osd.BatteryVolts!.Value, 9);
        Assert.Equal(0, osd.ConsumedMah!.Value, 9);
        Assert.Equal(0, osd.CurrentAmps!.Value, 9);
    }

    [Fact]
    public void Consumed_mah_follows_the_battery_current_and_resets()
    {
        var aircraft = new Aircraft(TestData.Aircraft("trainer"));
        var power = aircraft.Power!;
        double ampSeconds = 0;
        for (int i = 0; i < 500; i++)
        {
            var t = power.Step(0.01, 1.0, 0, 1.225);
            ampSeconds += t.BatteryCurrent * 0.01;
        }
        var osd = Osd(State(new Vec3(0, 75, 20), 0, 0, 0), aircraft);
        Assert.True(ampSeconds > 1, $"motor drew too little: {ampSeconds} A·s");
        Assert.Equal(ampSeconds / 3.6, osd.ConsumedMah!.Value, 6);
        Assert.Equal(power.Telemetry.BatteryCurrent, osd.CurrentAmps!.Value, 9);
        Assert.Equal(power.Telemetry.BatteryVoltage, osd.BatteryVolts!.Value, 9);
        power.Reset();
        Assert.Equal(0, Osd(State(new Vec3(0, 75, 20), 0, 0, 0), aircraft).ConsumedMah!.Value, 9);
    }

    [Fact]
    public void Without_a_power_plant_the_battery_fields_are_empty()
    {
        var glider = new Aircraft(TestData.Aircraft("trainer") with { Power = null });
        var osd = Osd(State(new Vec3(0, 75, 20), 0, 0, 0), glider);
        Assert.Null(osd.BatteryVolts);
        Assert.Null(osd.CurrentAmps);
        Assert.Null(osd.ConsumedMah);
    }

    [Fact]
    public void Gear_state_is_shown_only_for_retractable_gear()
    {
        var level = State(new Vec3(0, 0, 50), 0, 0, 0);
        Assert.Null(Osd(level).Gear);
        var jet = new Aircraft(TestData.Aircraft("jet"));
        Assert.Equal(GearIndicator.Down, Osd(level, jet).Gear);
        var up = new SimLab.Flight.Controls.ControlInputs(0, 0, 0, 0) { GearUp = true };
        jet.StepControls(1.0, up);
        Assert.Equal(GearIndicator.Moving, Osd(level, jet).Gear);
        jet.StepControls(10.0, up);
        Assert.Equal(GearIndicator.Up, Osd(level, jet).Gear);
    }

    [Fact]
    public void Fuel_engines_show_the_fuel_left_instead_of_the_battery()
    {
        var def = TestData.Aircraft("trainer");
        var gas = def with
        {
            Power = SimLab.Flight.Propulsion.PowerPlantSpec.WithPiston(
                new SimLab.Flight.Propulsion.PistonEngineSpec(5500, 8200, 1800, 9000, 0.012, TankMl: 700, FuelFlowMaxMlMin: 110, FuelFlowIdleMlMin: 10),
                SimLab.Flight.Propulsion.PropellerSpec.Generic(23, 9), new Vec3(-1, 0, 0), BodyAxes.Forward),
        };
        var aircraft = new Aircraft(gas);
        var level = State(new Vec3(0, 0, 50), 0, 0, 0);
        var full = Osd(level, aircraft);
        Assert.Null(full.BatteryVolts);
        Assert.Null(full.CurrentAmps);
        Assert.Equal(100, full.FuelPercent!.Value, 6);
        Assert.Equal(700, full.FuelMl!.Value, 6);
        Assert.Null(Osd(level).FuelPercent);
    }

    [Fact]
    public void Flap_setting_is_shown_only_for_aircraft_with_flaps()
    {
        var level = State(new Vec3(0, 0, 50), 0, 0, 0);
        Assert.Null(Osd(level).Flaps);
        var p51 = new Aircraft(TestData.Aircraft("p51"));
        Assert.Equal(FlapIndicator.Up, Osd(level, p51, flap: 0).Flaps);
        Assert.Equal(FlapIndicator.Half, Osd(level, p51, flap: SimLab.App.Session.FlapSetting.Half).Flaps);
        Assert.Equal(FlapIndicator.Landing, Osd(level, p51, flap: 1).Flaps);
    }
}
