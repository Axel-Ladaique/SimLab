using SimLab.App.Maps;
using SimLab.App.Maps.Mountain;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.Flight.Controls;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;
using SimLab.Flight.Ground;
using Xunit.Abstractions;

namespace SimLab.App.Tests.Session;

/// <summary>Acceptance tests of the slope wind on the mountain: lift in front of the pilot in a west wind, rotor in an
/// east wind, and the flying wing staying up on the ridge with the motor off.</summary>
public class MountainSoaringTests(ITestOutputHelper output)
{
    const double WindSpeed = 8;

    static FlightSession Session(string aircraft, double windFromDeg) =>
        new(TestData.Aircraft(aircraft), new FlightConditions(WindSpeed: WindSpeed, WindFromDeg: windFromDeg, Turbulence: 0),
            FieldCatalog.Load("mountain"));

    static Vec3 WindAt(FlightSession session, double x, double y, double heightAgl) =>
        session.Simulation.Environment.Wind.At(new Vec3(x, y, session.Terrain.Height(x, y) + heightAgl), heightAgl);

    [Fact]
    public void Pilot_position_is_in_lift_with_a_west_wind()
    {
        using var session = Session("wing", 270);
        var wind = WindAt(session, MountainRelief.CrestX(0) - 60, 0, 30);
        output.WriteLine($"wind 60 m in front of the crest, 30 m AGL: {wind}");
        Assert.True(wind.Z > 2, $"vertical wind {wind.Z:F2} m/s");
    }

    [Fact]
    public void Pilot_position_is_in_the_rotor_with_an_east_wind()
    {
        using var session = Session("wing", 90);
        double x = MountainRelief.CrestX(-22) - 60, y = -22;
        var wind = WindAt(session, x, y, 20);
        var sample = session.Simulation.Environment.Wind.Terrain!.Sample(x, y);
        output.WriteLine($"wind 60 m below the crest, 20 m AGL: {wind}; {sample}");
        Assert.True(wind.Z < 0, $"vertical wind {wind.Z:F2} m/s");
        Assert.True(sample.Shelter > 0.5, $"shelter {sample.Shelter:F2}");
    }

    [Fact]
    public void Wing_soars_the_ridge_without_motor()
    {
        using var session = Session("wing", 270);
        var pilot = MountainMap.Layout.PilotPosition;
        double pilotZ = session.Terrain.Height(pilot.X, pilot.Y);
        var start = session.Aircraft.State;
        Assert.Equal(270, Angle.Deg(Attitude.FromOrientation(start.Orientation).Heading), 6);

        var autopilot = new RidgeAutopilot();
        const double dt = RidgeAutopilot.Dt;

        // The hand launch point is on the level pad, 30–40 m short of the face: a pusher wing launched into the wind
        // there sinks onto the pad before reaching the lift, so the motor carries it out over the face, then stops.
        double launchTime = 0;
        while (session.Aircraft.State.Position.X > MountainRelief.CrestX(session.Aircraft.State.Position.Y) - 40)
        {
            Assert.True(launchTime < 10, "the wing did not reach the face within 10 s of the launch");
            var s = session.Aircraft.State;
            session.Tick(dt, autopilot.Command(s, Airspeed(session)) with { Throttle = 1 });
            launchTime += dt;
            Assert.Equal(CrashCause.None, session.Aircraft.Crash);
        }
        output.WriteLine($"over the face {launchTime:F1} s after the launch; motor off from here");

        double maxDistance = 0, minAgl = double.PositiveInfinity;
        int steps = (int)Math.Round(180 / dt);
        for (int n = 0; n <= steps; n++)
        {
            var s = session.Aircraft.State;
            double airspeed = Airspeed(session);
            maxDistance = Math.Max(maxDistance, Math.Sqrt(Sq(s.Position.X - pilot.X) + Sq(s.Position.Y - pilot.Y)));
            minAgl = Math.Min(minAgl, session.HeightAgl);
            if (n % (int)Math.Round(10 / dt) == 0)
                output.WriteLine($"t={n * dt,4:F0} s  x={s.Position.X,7:F1}  y={s.Position.Y,7:F1}  " +
                                 $"above pilot={s.Position.Z - pilotZ,6:F1} m  AGL={session.HeightAgl,6:F1} m  airspeed={airspeed,5:F1} m/s");
            if (n == steps) break;
            session.Tick(dt, autopilot.Command(s, airspeed));
            Assert.Equal(CrashCause.None, session.Aircraft.Crash);
        }

        double height = session.Aircraft.State.Position.Z - pilotZ;
        output.WriteLine($"max distance from the pilot {maxDistance:F0} m, lowest {minAgl:F1} m above the ground");
        Assert.True(height >= -20, $"height above the pilot after 180 s: {height:F1} m");
        Assert.True(maxDistance <= 500, $"max distance from the pilot: {maxDistance:F0} m");
        Assert.True(minAgl > 3, $"came down to {minAgl:F1} m above the ground");
    }

    static double Airspeed(FlightSession session)
    {
        var s = session.Aircraft.State;
        return (s.Velocity - session.Simulation.Environment.Wind.At(s.Position, session.HeightAgl)).Length;
    }

    static double Sq(double v) => v * v;

    /// <summary>Beats north and south along the crest 40–120 m in front of it, motor off, aiming for 11 m/s (the speed loop settles a little faster in the lift).</summary>
    sealed class RidgeAutopilot
    {
        public const double Dt = 0.02;
        const double BeatHalfLength = 300, TargetAirspeed = 11;
        const double PitchGain = 2, TrimGain = 0.02, ElevatorGain = 0.05, DampGain = 0.01;
        bool _beatNorth = true;
        double _elevatorTrim;

        public ControlInputs Command(in RigidBodyState s, double airspeed)
        {
            double x = s.Position.X, y = s.Position.Y;
            var attitude = Attitude.FromOrientation(s.Orientation);
            double heading = Angle.Deg(attitude.Heading), bank = Angle.Deg(attitude.Roll);
            double rollRate = Angle.Deg(-s.AngularVelocity.X), pitchRateUp = Angle.Deg(s.AngularVelocity.Y);

            if (_beatNorth && y > BeatHalfLength) _beatNorth = false;
            else if (!_beatNorth && y < -BeatHalfLength) _beatNorth = true;

            double targetHeading = _beatNorth ? 0 : 180;
            // Keep over the face: crab into the (west) wind, turning further into it when drifting east of CrestX − 70.
            double drift = Math.Clamp((x - (MountainRelief.CrestX(y) - 70)) / 60, -1, 1);
            targetHeading += (_beatNorth ? -1 : 1) * Math.Clamp(45 + 45 * drift, 0, 90);
            double headingError = Math.IEEERemainder(targetHeading - heading, 360);
            double bankTarget = Math.Clamp(1.2 * headingError, -35, 35);
            double aileron = Math.Clamp(0.04 * (bankTarget - bank) - 0.01 * rollRate, -1, 1);
            // Speed on pitch: an attitude loop (with an integral term for the elevon trim) under an airspeed loop.
            double pitchTarget = Math.Clamp(PitchGain * (airspeed - TargetAirspeed), -12, 12);
            double pitchError = pitchTarget - Angle.Deg(attitude.Pitch);
            _elevatorTrim = Math.Clamp(_elevatorTrim + TrimGain * pitchError * Dt, -0.5, 0.5);
            double elevator = Math.Clamp(ElevatorGain * pitchError + _elevatorTrim - DampGain * pitchRateUp, -1, 1);
            return new ControlInputs(0, aileron, elevator, 0);
        }
    }
}
