using System.Globalization;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Flight.Geometry;
using SimLab.Flight.Sim;

namespace SimLab.Flight.Recording;

/// <summary>
/// Writes one CSV row every <c>decimation</c> physics steps (default 100 Hz). The header depends on the
/// aircraft (one deflection column per control) and on the optional raw input channels.
/// Position, velocity and wind columns are world ENU (x east, y north, z up); the quaternion maps body to world;
/// angular velocity (wx, wy, wz) is in body axes.
/// </summary>
public sealed class FlightRecorder : IDisposable
{
    /// <summary>Columns every recording starts with, in order.</summary>
    public const string CommonColumns =
        "t,throttle,aileron,elevator,rudder,x,y,z,vx,vy,vz,qx,qy,qz,qw,wx,wy,wz," +
        "airspeed,alpha_deg,beta_deg,rpm,thrust_n,battery_v,battery_a,soc,crash";

    readonly TextWriter _writer;
    readonly int _decimation;
    readonly int _rawCount;
    double[]? _rawChannels;
    long _count;

    /// <param name="rawChannelNames">Names of raw input channels (columns <c>raw_&lt;name&gt;</c>); values come from <see cref="RawChannels"/>.</param>
    public FlightRecorder(TextWriter writer, AircraftDefinition aircraft, int decimation = 5, IReadOnlyList<string>? rawChannelNames = null)
    {
        if (decimation < 1) throw new ArgumentOutOfRangeException(nameof(decimation));
        _writer = writer;
        _decimation = decimation;
        _rawCount = rawChannelNames?.Count ?? 0;
        Header = string.Join(',',
            new[] { CommonColumns, "flap" }
                .Concat(aircraft.Controls.Select(c => $"defl_{c.Name}_deg"))
                .Concat(["wind_x", "wind_y", "wind_z", "motor_a"])
                .Concat((rawChannelNames ?? []).Select(n => $"raw_{n}")));
        _writer.WriteLine(Header);
    }

    public string Header { get; }

    /// <summary>Latest raw channel values, one per name given to the constructor; written as NaN while unset.</summary>
    public double[]? RawChannels
    {
        get => _rawChannels;
        set
        {
            if (value is not null && value.Length != _rawCount)
                throw new ArgumentException($"Expected {_rawCount} raw channel values, got {value.Length}.", nameof(value));
            _rawChannels = value;
        }
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
        Write(values);
        _writer.Write(',');
        _writer.Write(a.Crash.ToString());

        var wind = a.LastWind;
        var extra = new List<double>(8 + a.Deflections.Count + _rawCount) { input.Flap };
        extra.AddRange(a.Deflections.Select(Angle.Deg));
        extra.AddRange([wind.X, wind.Y, wind.Z, p.MotorCurrent]);
        for (int i = 0; i < _rawCount; i++) extra.Add(_rawChannels is null ? double.NaN : _rawChannels[i]);
        _writer.Write(',');
        Write(extra);
        _writer.WriteLine();
    }

    void Write(IEnumerable<double> values) =>
        _writer.Write(string.Join(',', values.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))));

    public void Dispose() => _writer.Dispose();
}
