using SimLab.Flight.Airframe;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.App.Ui;

/// <summary>
/// Everything the flight OSD shows, in pilot units: km/h, m, m/s, degrees (heading clockwise from north, roll right
/// and pitch up positive), percent, volts, amps and mAh. Battery fields are null without a power plant.
/// </summary>
/// <param name="HomeRelativeBearingDeg">Direction of the pilot relative to the nose, (−180, 180], positive to the right.</param>
public sealed record OsdData(
    double AirspeedKmh, double HeightM, double VarioMs,
    double HeadingDeg, double RollDeg, double PitchDeg,
    double HomeDistanceM, double HomeRelativeBearingDeg,
    double ThrottlePercent, double? BatteryVolts, double? CurrentAmps, double? ConsumedMah,
    double FlightTimeSeconds)
{
    public static OsdData From(Aircraft aircraft, in RigidBodyState display, double heightAgl, double flightTimeSeconds,
        double throttle, Vec3 pilot)
    {
        var attitude = Attitude.FromOrientation(display.Orientation);
        double heading = Angle.Deg(attitude.Heading);
        double dx = pilot.X - display.Position.X, dy = pilot.Y - display.Position.Y;
        double bearing = Angle.Deg(Math.Atan2(dx, dy));

        double? volts = null, amps = null, mah = null;
        if (aircraft.Power is { } power)
        {
            var t = power.Telemetry;
            // Before the first physics step the telemetry is empty: show the resting voltage instead.
            volts = t.BatteryVoltage > 0 ? t.BatteryVoltage : power.Spec.Battery.OpenCircuitVoltage(power.StateOfCharge);
            amps = t.BatteryCurrent;
            mah = (1 - power.StateOfCharge) * power.Spec.Battery.CapacityAh * 1000;
        }

        return new OsdData(
            aircraft.AirData.Airspeed * 3.6, Math.Max(0, heightAgl), display.Velocity.Z,
            heading, Angle.Deg(attitude.Roll), Angle.Deg(attitude.Pitch),
            Math.Sqrt(dx * dx + dy * dy), WrapDeg(bearing - heading),
            Math.Clamp(throttle, 0, 1) * 100, volts, amps, mah,
            flightTimeSeconds);
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
