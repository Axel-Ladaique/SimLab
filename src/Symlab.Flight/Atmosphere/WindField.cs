using Symlab.Flight.Geometry;

namespace Symlab.Flight.Atmosphere;

/// <summary>
/// Steady wind with a logarithmic ground profile plus low-altitude Dryden turbulence
/// (MIL-F-8785C), implemented as seeded first-order shaping filters.
/// </summary>
public sealed class WindField
{
    const double ReferenceHeight = 10.0;
    const double TwentyFeet = 6.1;
    readonly int _seed;
    Random _random;
    double _u, _v, _w;

    public WindField(WindSettings settings, int seed)
    {
        if (settings.RoughnessLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.RoughnessLength, "RoughnessLength must be positive.");
        Settings = settings;
        _seed = seed;
        _random = new Random(seed);
    }

    public WindSettings Settings { get; }
    public Vec3 Turbulence { get; private set; }

    Vec3 Downwind
    {
        get
        {
            var from = Angle.Rad(Settings.FromDirectionDeg);
            return new Vec3(-Math.Sin(from), 0, Math.Cos(from));
        }
    }

    public Vec3 SteadyAt(double heightAgl)
    {
        if (Settings.SpeedAt10m <= 0) return Vec3.Zero;
        double z0 = Settings.RoughnessLength;
        double h = Math.Max(heightAgl, 0.5);
        double speed = Settings.SpeedAt10m * Math.Log(h / z0) / Math.Log(ReferenceHeight / z0);
        return Downwind * speed;
    }

    /// <summary>Restarts the turbulence: re-seeds the generator with the constructor seed and zeroes the filter states.</summary>
    public void Reset()
    {
        _random = new Random(_seed);
        _u = _v = _w = 0;
        Turbulence = Vec3.Zero;
    }

    public Vec3 At(double heightAgl) => SteadyAt(heightAgl) + Turbulence;

    public void Advance(double dt, double heightAgl, double airspeed)
    {
        double sigmaW = 0.1 * SteadyAt(TwentyFeet).Length * Settings.Turbulence;
        if (sigmaW <= 0)
        {
            _u = _v = _w = 0;
            Turbulence = Vec3.Zero;
            return;
        }

        double h = Math.Clamp(heightAgl, 3.0, 300.0);
        double k = 0.177 + 0.000823 * h * 3.28084;
        double sigmaUV = sigmaW / Math.Pow(k, 0.4);
        double lengthW = h;
        double lengthUV = h / Math.Pow(k, 1.2);
        double speed = Math.Max(airspeed, 1.0);

        _u = Filter(_u, sigmaUV, lengthUV, speed, dt);
        _v = Filter(_v, sigmaUV, lengthUV, speed, dt);
        _w = Filter(_w, sigmaW, lengthW, speed, dt);

        var along = Downwind;
        var across = Vec3.Cross(Vec3.UnitY, along);
        Turbulence = along * _u + across * _v + Vec3.UnitY * _w;
    }

    double Filter(double x, double sigma, double length, double speed, double dt)
    {
        double a = Math.Exp(-speed * dt / length);
        return a * x + sigma * Math.Sqrt(1 - a * a) * Gaussian();
    }

    double Gaussian()
    {
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
