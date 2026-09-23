using System.Globalization;
using Symlab.Flight.Airframe;
using Symlab.Flight.Ground;

namespace Symlab.App.Ui;

public readonly record struct FlightDataLine(string Key, string Value);

public static class FlightDataFormatter
{
    public static IReadOnlyList<FlightDataLine> Format(Aircraft aircraft, double heightAgl, double flightTimeSeconds, double throttle)
    {
        var inv = CultureInfo.InvariantCulture;
        var seconds = (int)Math.Max(0, flightTimeSeconds);
        string battery = aircraft.Power is null ? "—" : aircraft.Power.Telemetry.BatteryVoltage.ToString("0.0", inv) + " V";
        if (aircraft.Power is not null && aircraft.Power.Telemetry.BatteryVoltage <= 0)
            battery = aircraft.Power.Spec.Battery.OpenCircuitVoltage(aircraft.Power.StateOfCharge).ToString("0.0", inv) + " V";
        return
        [
            new("HUD_AIRSPEED", (aircraft.AirData.Airspeed * 3.6).ToString("0", inv) + " km/h"),
            new("HUD_ALTITUDE", Math.Max(0, heightAgl).ToString("0", inv) + " m"),
            new("HUD_THROTTLE", (Math.Clamp(throttle, 0, 1) * 100).ToString("0", inv) + " %"),
            new("HUD_BATTERY", battery),
            new("HUD_TIMER", $"{seconds / 60:00}:{seconds % 60:00}"),
        ];
    }

    public static string CrashKey(CrashCause cause) => "CRASH_" + cause.ToString().ToUpperInvariant();
}
