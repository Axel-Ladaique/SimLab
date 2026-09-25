using SimLab.Flight.Ground;

namespace SimLab.App.Ui;

public static class FlightDataFormatter
{
    public static string CrashKey(CrashCause cause) => "CRASH_" + cause.ToString().ToUpperInvariant();
}
