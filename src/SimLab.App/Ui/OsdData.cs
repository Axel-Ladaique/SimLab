using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.App.Ui;

public enum GearIndicator { Down, Moving, Up }

public enum FlapIndicator { Up, Half, Landing }

/// <summary>
/// Everything the flight OSD shows, in pilot units: km/h, m, m/s, degrees (heading clockwise from north, roll right
/// and pitch up positive), percent, volts, amps and mAh. Battery fields are null without a power plant.
/// </summary>
/// <param name="HomeRelativeBearingDeg">Direction of the pilot relative to the nose, (−180, 180], positive to the right.</param>
/// <param name="Gear">Retractable gear state, null for fixed gear.</param>
/// <param name="Flaps">Commanded flap setting, null for aircraft without flaps.</param>
/// <param name="FuelPercent">Fuel left for piston and turbine engines (the battery fields are then null).</param>
/// <param name="ThrottleCut">The throttle-cut switch is on.</param>
public sealed record OsdData(
    double AirspeedKmh, double HeightM, double VarioMs,
    double HeadingDeg, double RollDeg, double PitchDeg,
    double HomeDistanceM, double HomeRelativeBearingDeg,
    double ThrottlePercent, double? BatteryVolts, double? CurrentAmps, double? ConsumedMah,
    double FlightTimeSeconds,
    GearIndicator? Gear = null,
    double? FuelPercent = null, double? FuelMl = null,
    FlapIndicator? Flaps = null,
    bool ThrottleCut = false)
{
    public static OsdData From(Aircraft aircraft, in RigidBodyState display, double heightAgl, double flightTimeSeconds,
        double throttle, Vec3 pilot, double flap = 0, bool throttleCut = false)
    {
        var attitude = Attitude.FromOrientation(display.Orientation);
        double heading = Angle.Deg(attitude.Heading);
        double dx = pilot.X - display.Position.X, dy = pilot.Y - display.Position.Y;
        double bearing = Angle.Deg(Math.Atan2(dx, dy));

        double? volts = null, amps = null, mah = null, fuelPercent = null, fuelMl = null;
        if (aircraft.Power is { Spec.TankMl: { } tank } engine)
        {
            fuelPercent = engine.StateOfCharge * 100;
            fuelMl = engine.StateOfCharge * tank;
        }
        else if (aircraft.Power is { Spec.Battery: { } battery } power)
        {
            var t = power.Telemetry;
            // Before the first physics step the telemetry is empty: show the resting voltage instead.
            volts = t.BatteryVoltage > 0 ? t.BatteryVoltage : battery.OpenCircuitVoltage(power.StateOfCharge);
            amps = t.BatteryCurrent;
            mah = (1 - power.StateOfCharge) * battery.CapacityAh * 1000;
        }

        return new OsdData(
            aircraft.AirData.Airspeed * 3.6, Math.Max(0, heightAgl), display.Velocity.Z,
            heading, Angle.Deg(attitude.Roll), Angle.Deg(attitude.Pitch),
            Math.Sqrt(dx * dx + dy * dy), WrapDeg(bearing - heading),
            Math.Clamp(throttle, 0, 1) * 100, volts, amps, mah,
            flightTimeSeconds,
            aircraft.Definition.GearRetract is null ? null
                : aircraft.GearPosition == 0 ? GearIndicator.Down
                : aircraft.GearPosition == 1 ? GearIndicator.Up
                : GearIndicator.Moving,
            fuelPercent, fuelMl,
            !aircraft.Definition.Controls.Any(c => c.Mix.ContainsKey("flap")) ? null
                : flap < Session.FlapSetting.Half / 2 ? FlapIndicator.Up
                : flap < (Session.FlapSetting.Half + Session.FlapSetting.Landing) / 2 ? FlapIndicator.Half
                : FlapIndicator.Landing,
            throttleCut);
    }

    /// <summary>An angle in degrees brought into (−180, 180].</summary>
    public static double WrapDeg(double deg)
    {
        double w = deg % 360;
        if (w <= -180) w += 360;
        else if (w > 180) w -= 360;
        return w;
    }
}
