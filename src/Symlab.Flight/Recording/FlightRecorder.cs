using System.Globalization;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Recording;

/// <summary>Writes one CSV row every <c>decimation</c> physics steps (default 100 Hz).</summary>
public sealed class FlightRecorder : IDisposable
{
    public const string Header =
        "t,throttle,aileron,elevator,rudder,x,y,z,vx,vy,vz,qx,qy,qz,qw,wx,wy,wz," +
        "airspeed,alpha_deg,beta_deg,rpm,thrust_n,battery_v,battery_a,soc,crash";

    readonly TextWriter _writer;
    readonly int _decimation;
    long _count;

    public FlightRecorder(TextWriter writer, int decimation = 5)
    {
        if (decimation < 1) throw new ArgumentOutOfRangeException(nameof(decimation));
        _writer = writer;
        _decimation = decimation;
        _writer.WriteLine(Header);
    }

    public void OnStep(Simulation simulation, in ControlInputs input)
    {
        if (_count++ % _decimation != 0) return;
        var a = simulation.Aircraft;
        var s = a.State;
        var p = a.Power?.Telemetry ?? default;
        double[] values =
        [
            simulation.Time, input.Throttle, input.Aileron, input.Elevator, input.Rudder,
            s.Position.X, s.Position.Y, s.Position.Z, s.Velocity.X, s.Velocity.Y, s.Velocity.Z,
            s.Orientation.X, s.Orientation.Y, s.Orientation.Z, s.Orientation.W,
            s.AngularVelocity.X, s.AngularVelocity.Y, s.AngularVelocity.Z,
            a.AirData.Airspeed, Angle.Deg(a.AirData.Alpha), Angle.Deg(a.AirData.Beta),
            p.Rpm, p.Thrust, p.BatteryVoltage, p.BatteryCurrent, p.StateOfCharge,
        ];
        _writer.Write(string.Join(',', values.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))));
        _writer.Write(',');
        _writer.WriteLine(a.Crash.ToString());
    }

    public void Dispose() => _writer.Dispose();
}
